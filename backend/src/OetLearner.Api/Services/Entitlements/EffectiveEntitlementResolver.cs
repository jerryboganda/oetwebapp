using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Entitlements;

public interface IEffectiveEntitlementResolver
{
    Task<EffectiveEntitlementSnapshot> ResolveAsync(string? userId, CancellationToken ct);

    /// <summary>
    /// Invalidates request-scoped memoized entitlement state after a mutation that
    /// bypasses this resolver's scoped <see cref="DbContext"/> change tracker.
    /// Normal tracked saves are observed automatically.
    /// </summary>
    void Invalidate(string? userId = null)
    {
    }
}

/// <summary>
/// Merged per-plan content include/exclude overrides (access &amp; payment spec §3,
/// <see cref="Domain.BillingPlan.ContentOverridesJson"/>). Unioned across the learner's
/// active packages. An explicit include wins over the subtest/profession scope AND over an
/// exclude; neither ever bypasses the module gate.
/// </summary>
public sealed record ContentOverrideSets(
    IReadOnlySet<string> VideoIncludes,
    IReadOnlySet<string> VideoExcludes,
    IReadOnlySet<string> MaterialFolderIncludes,
    IReadOnlySet<string> MaterialFolderExcludes)
{
    public static readonly ContentOverrideSets Empty = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public bool IsEmpty => VideoIncludes.Count == 0
        && VideoExcludes.Count == 0
        && MaterialFolderIncludes.Count == 0
        && MaterialFolderExcludes.Count == 0;
}

public sealed record EffectiveEntitlementSnapshot(
    string? UserId,
    bool HasEligibleSubscription,
    bool IsTrial,
    string Tier,
    string? SubscriptionId,
    SubscriptionStatus? SubscriptionStatus,
    string? PlanId,
    string? PlanVersionId,
    string? PlanCode,
    string? AiQuotaPlanCode,
    string? AiQuotaPlanCodeSource,
    IReadOnlyList<string> ActiveAddOnCodes,
    bool IsFrozen,
    IReadOnlyList<string> Trace)
{
    // ── OET 2026 catalog extensions ─────────────────────────────────────────
    public IReadOnlyList<string> EnabledModules { get; init; } = Array.Empty<string>();
    public bool WritingAddonsEnabled { get; init; }
    public bool SpeakingAddonsEnabled { get; init; }

    /// <summary>Gates Speaking "Practice Card Access" (ai_self_practice
    /// mode). Defaults true (see <see cref="Domain.BillingPlan.SpeakingPracticeAccessEnabled"/>)
    /// so accounts with no eligible subscription — who today buy AI package
    /// credits a la carte regardless of plan — are never blocked by this
    /// flag; it only takes effect for a resolved, eligible plan that
    /// explicitly disables it.</summary>
    public bool SpeakingPracticeAccessEnabled { get; init; } = true;
    public bool TutorBookDiscountEnabled { get; init; }
    public int WritingAssessmentsRemaining { get; init; }
    public int SpeakingSessionsRemaining { get; init; }
    public int AiCreditsRemaining { get; init; }
    public bool TutorBookUnlocked { get; init; }
    public bool BasicEnglishUnlocked { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? ProductCategory { get; init; }

    // ── Subtest × profession content model (access & payment spec §3) ────────

    /// <summary>The learner's registered profession (<see cref="Domain.ApplicationUser.ActiveProfessionId"/>),
    /// trimmed and lower-cased, or null when they have none. This is the profession axis of the
    /// "subtest × profession" content model; resolved here once so every content gate agrees.
    /// Populated regardless of eligibility — it is a property of the user, not of the plan.</summary>
    public string? ProfessionId { get; init; }

    /// <summary>True when NO active package restricts the subtest axis. A plan whose
    /// <see cref="Domain.BillingPlan.IncludedSubtestsJson"/> is empty/"[]" means "all subtests"
    /// and therefore dominates the union across packages. When false, only
    /// <see cref="IncludedSubtests"/> are in scope. Defaults true (non-restricting) so every
    /// caller that predates this axis keeps today's behaviour.</summary>
    public bool AllSubtestsIncluded { get; init; } = true;

    /// <summary>Union of the OET subtest codes (listening|reading|writing|speaking) the learner's
    /// active packages include. Meaningful only when <see cref="AllSubtestsIncluded"/> is false.</summary>
    public IReadOnlySet<string> IncludedSubtests { get; init; } = EmptyStringSet;

    /// <summary>Merged per-plan content include/exclude overrides across the learner's active packages.</summary>
    public ContentOverrideSets ContentOverrides { get; init; } = ContentOverrideSets.Empty;

    private static readonly IReadOnlySet<string> EmptyStringSet =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether an admin-togglable subscription module (see <see cref="ModuleKeys"/>) is enabled
    /// for this snapshot. FAIL-OPEN by design: when the plan carries no explicit module list
    /// (legacy plans, malformed JSON, non-subscribers whose list is empty) every module reads
    /// as enabled, so this check can only ever RESTRICT — it never elevates access beyond the
    /// module's own subscription/credit gate, and never locks out a plan that simply predates
    /// the module list. A module is blocked only when the list is present and non-empty AND does
    /// not contain the key (case-insensitive). Owner directive 2026-07-11: existing plans are
    /// back-filled with all four keys ON, so this only bites once an admin explicitly disables one.
    /// </summary>
    public bool IsModuleEnabled(string moduleKey)
    {
        // Per-user explicit DISABLE always wins, and is checked BEFORE the
        // fail-open empty-list branch. This is what lets an admin disable the
        // last remaining module for a single learner without the empty
        // EnabledModules list silently re-enabling everything.
        if (DisabledModules is { Count: > 0 })
        {
            foreach (var disabled in DisabledModules)
            {
                if (string.Equals(disabled, moduleKey, StringComparison.OrdinalIgnoreCase)) return false;
            }
        }

        if (EnabledModules is null || EnabledModules.Count == 0) return true;
        foreach (var enabled in EnabledModules)
        {
            if (string.Equals(enabled, moduleKey, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Per-user explicit deny-set (from <see cref="Domain.UserModuleOverride"/> rows with
    /// Enabled=false). Subtracted first in <see cref="IsModuleEnabled"/> so a disable survives the
    /// fail-open empty-list contract. Empty for every user without overrides.
    /// </summary>
    public IReadOnlyList<string> DisabledModules { get; init; } = Array.Empty<string>();
}

public sealed class EffectiveEntitlementResolver : IEffectiveEntitlementResolver
{
    private readonly LearnerDbContext db;
    private readonly ILogger<EffectiveEntitlementResolver>? logger;
    private readonly Dictionary<string, EffectiveEntitlementSnapshot> memoizedSnapshots =
        new(StringComparer.Ordinal);
    private bool observesDbContextMutations;

    public EffectiveEntitlementResolver(
        LearnerDbContext db,
        ILogger<EffectiveEntitlementResolver>? logger = null)
    {
        this.db = db;
        this.logger = logger;
    }

    public async Task<EffectiveEntitlementSnapshot> ResolveAsync(string? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Empty(userId, "anonymous");
        }

        ct.ThrowIfCancellationRequested();
        ObserveDbContextMutations();

        // Detect pending tracked changes before consulting the cache. This also
        // raises StateChanged for snapshot-tracked entities and clears entries.
        if (db.ChangeTracker.HasChanges())
        {
            Invalidate();
        }

        if (memoizedSnapshots.TryGetValue(userId, out var memoized))
        {
            return memoized;
        }

        var snapshot = await ResolveCoreAsync(userId, ct);

        // Never memoize while a caller has an uncommitted mutation. A successful
        // SaveChanges later raises SavedChanges and invalidates prior snapshots.
        if (!db.ChangeTracker.HasChanges())
        {
            memoizedSnapshots[userId] = snapshot;
        }

        return snapshot;
    }

    public void Invalidate(string? userId = null)
    {
        if (userId is null)
        {
            memoizedSnapshots.Clear();
            return;
        }

        memoizedSnapshots.Remove(userId);
    }

    private async Task<EffectiveEntitlementSnapshot> ResolveCoreAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var trace = new List<string>();
        var subscriptions = await LoadOrderedSubscriptionsAsync(userId, ct);
        var subscription = subscriptions.Count > 0 ? subscriptions[0] : null;
        var overlays = await LoadResolverOverlaysAsync(userId, ct);
        var isFrozen = ResolveIsFrozen(overlays, now);
        var professionId = await LoadActiveProfessionIdAsync(userId, ct);

        if (subscription is null)
        {
            trace.Add("subscription.none");
            if (isFrozen) trace.Add("freeze.active");
            return new EffectiveEntitlementSnapshot(
                userId,
                HasEligibleSubscription: false,
                IsTrial: false,
                Tier: "free",
                SubscriptionId: null,
                SubscriptionStatus: null,
                PlanId: null,
                PlanVersionId: null,
                PlanCode: null,
                AiQuotaPlanCode: null,
                AiQuotaPlanCodeSource: null,
                ActiveAddOnCodes: Array.Empty<string>(),
                IsFrozen: isFrozen,
                Trace: trace)
            {
                ProfessionId = professionId,
            };
        }

        var plans = await LoadBillingPlansAsync(subscriptions, ct);
        var planVersionIds = await LoadPlanVersionIdsAsync(subscriptions, ct);
        var activeAddOns = await LoadActiveAddOnCodesAsync(subscriptions, now, ct);

        trace.Add($"subscription.latest.{subscription.Status}");

        // ── Eligibility gate ────────────────────────────────────────────────
        // Only Active/Trial confer entitlements. Suspended / PastDue /
        // Pending / Cancelled / Expired all fail-low to FREE. This branch is
        // single-source-of-truth — never elevate downstream.
        var eligible = subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial or SubscriptionStatus.FreezeRequested;
        var isTrial = subscription.Status == SubscriptionStatus.Trial;

        BillingPlan? plan = null;
        var failLowReason = (string?)null;

        if (eligible)
        {
            if (string.IsNullOrWhiteSpace(subscription.PlanId))
            {
                failLowReason = "plan.id_missing";
            }
            else
            {
                plan = ResolveBillingPlan(subscription.PlanId, plans);
                if (plan is null)
                {
                    failLowReason = "plan.missing";
                    trace.Add("plan.missing");
                }
                else
                {
                    trace.Add($"plan.{plan.Code}");

                    // Malformed entitlements JSON => fail-low. Cannot trust
                    // any feature flag derived from this row.
                    if (!string.IsNullOrWhiteSpace(plan.EntitlementsJson)
                        && !IsValidJsonObject(plan.EntitlementsJson))
                    {
                        failLowReason = "entitlements.malformed";
                        trace.Add("entitlements.malformed");
                    }
                    // Snapshot integrity: if a PlanVersionId is recorded on
                    // the subscription it MUST resolve to an existing version
                    // row. A dangling pointer = catalog drift = fail-low.
                    else if (!string.IsNullOrWhiteSpace(subscription.PlanVersionId))
                    {
                        var versionExists = planVersionIds.Contains(subscription.PlanVersionId);
                        if (!versionExists)
                        {
                            failLowReason = "plan.version.missing";
                            trace.Add("plan.version.missing");
                        }
                    }
                }
            }
        }

        if (failLowReason is not null)
        {
            logger?.LogWarning(
                "EntitlementResolver fail-low userId={UserId} subscriptionId={SubscriptionId} status={Status} planId={PlanId} planVersionId={PlanVersionId} reason={Reason}",
                userId,
                subscription.Id,
                subscription.Status,
                subscription.PlanId,
                subscription.PlanVersionId,
                failLowReason);
            eligible = false;
            isTrial = false;
            plan = null;
            trace.Add($"fail_low.{failLowReason}");
        }

        var addOnCodes = eligible
            ? ResolveActiveAddOnCodes(subscription.Id, activeAddOns)
            : Array.Empty<string>();
        var aiQuotaMapping = eligible ? AiQuotaPlanMappingResolver.Resolve(plan) : null;

        if (addOnCodes.Count > 0)
        {
            trace.Add($"addons.{addOnCodes.Count}");
        }

        if (isFrozen)
        {
            trace.Add("freeze.active");
        }

        var snapshot = new EffectiveEntitlementSnapshot(
            userId,
            HasEligibleSubscription: eligible,
            IsTrial: isTrial,
            Tier: eligible ? (isTrial ? "trial" : "paid") : "free",
            SubscriptionId: eligible ? subscription.Id : null,
            SubscriptionStatus: subscription.Status,
            PlanId: eligible ? subscription.PlanId : null,
            PlanVersionId: eligible ? subscription.PlanVersionId : null,
            PlanCode: eligible ? AiQuotaPlanMappingResolver.NormalizeCode(plan?.Code) : null,
            AiQuotaPlanCode: aiQuotaMapping?.Code,
            AiQuotaPlanCodeSource: aiQuotaMapping?.Source,
            ActiveAddOnCodes: addOnCodes,
            IsFrozen: isFrozen,
            Trace: trace)
        {
            ProfessionId = professionId,
        };

        if (eligible && plan is not null)
        {
            var (allSubtests, includedSubtests) = UnionIncludedSubtests(new[] { plan });
            snapshot = snapshot with
            {
                AllSubtestsIncluded = allSubtests,
                IncludedSubtests = includedSubtests,
                ContentOverrides = MergeContentOverrides(new[] { plan }),
                EnabledModules = ParseDashboardModules(plan.DashboardModulesJson),
                WritingAddonsEnabled = plan.WritingAddonsEnabled,
                SpeakingAddonsEnabled = plan.SpeakingAddonsEnabled,
                SpeakingPracticeAccessEnabled = plan.SpeakingPracticeAccessEnabled,
                TutorBookDiscountEnabled = plan.TutorBookDiscountEnabled,
                WritingAssessmentsRemaining = subscription.WritingAssessmentsRemaining,
                SpeakingSessionsRemaining = subscription.SpeakingSessionsRemaining,
                AiCreditsRemaining = subscription.AiCreditsRemaining,
                TutorBookUnlocked = subscription.TutorBookUnlocked,
                BasicEnglishUnlocked = subscription.BasicEnglishUnlocked,
                ExpiresAt = subscription.ExpiresAt,
                ProductCategory = string.IsNullOrEmpty(plan.ProductCategory) ? null : plan.ProductCategory,
            };
        }

        // ── Expiry lock ─────────────────────────────────────────────────────
        // A non-null ExpiresAt that has elapsed locks the course: no modules
        // and no eligible-subscription flag, so every downstream consumer that
        // gates on HasEligibleSubscription (content/recalls/vocab/AI/mocks/
        // speaking) fails-low to FREE. Null ExpiresAt == permanent (never
        // expires). ExpiresAt/ProductCategory/PlanCode stay populated for the
        // renewal UI. This only ever STRIPS access — fail-low is preserved (a
        // subscription that already failed-low has empty modules and
        // HasEligibleSubscription=false, so this is a no-op for it).
        var isExpired = subscription.Status != SubscriptionStatus.Frozen
            && subscription.ExpiresAt is { } exp
            && exp <= now;
        if (isExpired)
        {
            trace.Add("subscription.expired");
            snapshot = snapshot with
            {
                HasEligibleSubscription = false,
                EnabledModules = Array.Empty<string>(),
            };
        }

        // ── Permanent Tutor Book (resolved ACROSS all subscriptions) ────────
        // Per spec rule #8 ("Permanent entitlements, especially Tutor Book,
        // ignore expiry") the Tutor Book is permanent however it was acquired —
        // both the standalone `tutor-book` plan AND the £32 `tutor-book-addon`
        // (listed as a "Permanent entitlement") flip TutorBookUnlocked, the
        // latter on a parent COURSE sub that carries a real ExpiresAt. So this
        // grant must survive course expiry regardless of the row's ExpiresAt.
        // NARROW + additive: it never elevates the course, only re-enables the
        // Tutor Book modules the holder paid for. This mirrors the direct gate
        // in TutorBookEndpoints (TutorBookUnlocked && Active/Trial, no expiry).
        var permanentTutorBook = subscriptions.Any(s =>
            (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial)
            && s.TutorBookUnlocked);
        if (permanentTutorBook)
        {
            trace.Add("tutorbook.permanent");
            snapshot = snapshot with
            {
                TutorBookUnlocked = true,
                EnabledModules = snapshot.EnabledModules
                    .Union(new[] { "TutorBook", "AudioScripts", "Updates" }, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            };
        }

        // ── Multi-package aggregation (admin can allocate ≥1 packages) ───────
        // Owner directive 2026-07-12: a learner may hold MULTIPLE full course
        // packages at once (e.g. two professions). Union the entitlements of
        // every currently-effective COURSE subscription onto the snapshot.
        //
        // "Course subscription" = eligible status + resolvable plan + not expired
        // AND the plan grants at least one eligibility module (see
        // IsCourseEligiblePlan). That module test is what keeps this ADDITIVE and
        // safe: a permanent Tutor-Book / add-on overlay sub (modules TutorBook /
        // AudioScripts / Updates only) is NOT course-eligible, so it never flips
        // HasEligibleSubscription — the existing latest-sub + permanent-Tutor-Book
        // semantics (and every existing resolver test) are preserved unchanged.
        //
        // For a single-package learner the effective set is exactly their own
        // row, so this re-derives the identical snapshot (union / sum / max over
        // one element). It only changes behaviour once a learner has 2+ effective
        // course subs — which only the new admin allocation flow can create.
        var effectivePackages = new List<EffectivePackage>();
        foreach (var candidate in subscriptions)
        {
            var pkg = TryResolveEffectivePackage(candidate, plans, planVersionIds, now);
            if (pkg is not null && IsCourseEligiblePlan(pkg.Modules))
            {
                effectivePackages.Add(pkg);
            }
        }

        if (effectivePackages.Count > 0)
        {
            // subscriptions is ordered latest-first, so effectivePackages inherits
            // that order — the first entry is the newest effective course sub.
            var primaryPkg = effectivePackages[0];
            var primarySub = primaryPkg.Sub;
            var aggAddOns = new List<string>();
            foreach (var pkg in effectivePackages)
            {
                aggAddOns.AddRange(ResolveActiveAddOnCodes(pkg.Sub.Id, activeAddOns));
            }

            // Expiry: a null (permanent) member wins; otherwise the furthest date.
            DateTimeOffset? aggregatedExpiry = effectivePackages.Any(p => p.Sub.ExpiresAt is null)
                ? null
                : effectivePackages.Max(p => p.Sub.ExpiresAt);

            var aiQuota = AiQuotaPlanMappingResolver.Resolve(primaryPkg.Plan);
            var aggIsTrial = primarySub.Status == SubscriptionStatus.Trial;

            // Subtest × profession scope, re-derived across the effective set. Computed from
            // effectivePackages ALONE (not folded into whatever the single-plan pass above
            // resolved): a primary plan that is itself effective is already a member, and one
            // that is not — failed-low, expired, or a non-course overlay — must not widen the
            // scope of the packages that ARE live.
            var aggPlans = effectivePackages.Select(p => p.Plan).ToList();
            var (aggAllSubtests, aggSubtests) = UnionIncludedSubtests(aggPlans);

            if (effectivePackages.Count > 1)
            {
                trace.Add($"packages.{effectivePackages.Count}");
            }

            snapshot = snapshot with
            {
                AllSubtestsIncluded = aggAllSubtests,
                IncludedSubtests = aggSubtests,
                ContentOverrides = MergeContentOverrides(aggPlans),
                HasEligibleSubscription = true,
                IsTrial = aggIsTrial,
                Tier = aggIsTrial ? "trial" : "paid",
                SubscriptionId = primarySub.Id,
                SubscriptionStatus = primarySub.Status,
                PlanId = primarySub.PlanId,
                PlanVersionId = primarySub.PlanVersionId,
                PlanCode = AiQuotaPlanMappingResolver.NormalizeCode(primaryPkg.Plan.Code),
                AiQuotaPlanCode = aiQuota?.Code,
                AiQuotaPlanCodeSource = aiQuota?.Source,
                ActiveAddOnCodes = aggAddOns
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                EnabledModules = snapshot.EnabledModules
                    .Concat(effectivePackages.SelectMany(p => p.Modules))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                WritingAddonsEnabled = effectivePackages.Any(p => p.Plan.WritingAddonsEnabled),
                SpeakingAddonsEnabled = effectivePackages.Any(p => p.Plan.SpeakingAddonsEnabled),
                SpeakingPracticeAccessEnabled = effectivePackages.Any(p => p.Plan.SpeakingPracticeAccessEnabled),
                TutorBookDiscountEnabled = effectivePackages.Any(p => p.Plan.TutorBookDiscountEnabled),
                WritingAssessmentsRemaining = effectivePackages.Sum(p => p.Sub.WritingAssessmentsRemaining),
                SpeakingSessionsRemaining = effectivePackages.Sum(p => p.Sub.SpeakingSessionsRemaining),
                AiCreditsRemaining = effectivePackages.Sum(p => p.Sub.AiCreditsRemaining),
                TutorBookUnlocked = snapshot.TutorBookUnlocked || effectivePackages.Any(p => p.Sub.TutorBookUnlocked),
                BasicEnglishUnlocked = effectivePackages.Any(p => p.Sub.BasicEnglishUnlocked),
                ExpiresAt = aggregatedExpiry,
                ProductCategory = string.IsNullOrEmpty(primaryPkg.Plan.ProductCategory)
                    ? snapshot.ProductCategory
                    : primaryPkg.Plan.ProductCategory,
            };
        }

        // ── Per-user module overrides (admin per-learner enable/disable) ─────
        // Applied LAST so an admin override is authoritative over both the plan
        // list and the aggregation. Enable => add to the enabled set; disable =>
        // remove from the enabled set AND record in the deny-set so IsModuleEnabled
        // returns false even if the enabled list is now empty (fail-open guard).
        var moduleOverrides = overlays
            .Where(overlay => overlay.Kind == ModuleOverlayKind)
            .ToList();
        if (moduleOverrides.Count > 0)
        {
            var enabledSet = snapshot.EnabledModules.ToList();
            var disabledSet = new List<string>();
            foreach (var ov in moduleOverrides)
            {
                if (string.IsNullOrWhiteSpace(ov.ModuleKey)) continue;
                if (ov.Enabled)
                {
                    disabledSet.RemoveAll(m => string.Equals(m, ov.ModuleKey, StringComparison.OrdinalIgnoreCase));
                    if (!enabledSet.Any(m => string.Equals(m, ov.ModuleKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        enabledSet.Add(ov.ModuleKey);
                    }
                }
                else
                {
                    enabledSet.RemoveAll(m => string.Equals(m, ov.ModuleKey, StringComparison.OrdinalIgnoreCase));
                    if (!disabledSet.Any(m => string.Equals(m, ov.ModuleKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        disabledSet.Add(ov.ModuleKey);
                    }
                }
            }

            trace.Add($"module_overrides.{moduleOverrides.Count}");
            snapshot = snapshot with
            {
                EnabledModules = enabledSet.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                DisabledModules = disabledSet.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            };
        }

        return snapshot;
    }

    /// <summary>The module keys whose presence marks a plan as a real course/access
    /// package (as opposed to a permanent Tutor-Book / add-on overlay whose modules
    /// are TutorBook / AudioScripts / Updates only). A plan is course-eligible — and
    /// therefore contributes to multi-package eligibility aggregation — when its
    /// dashboard-module list intersects this set. Covers both the four admin
    /// dashboard keys and the per-skill keys real and seeded plans use.</summary>
    private static readonly HashSet<string> EligibilityModules = new(StringComparer.OrdinalIgnoreCase)
    {
        ModuleKeys.Recalls, ModuleKeys.MaterialsLibrary, ModuleKeys.VideoLibrary, ModuleKeys.Mocks,
        "Reading", "Listening", "Writing", "Speaking",
    };

    private static bool IsCourseEligiblePlan(IReadOnlyList<string> modules)
    {
        foreach (var module in modules)
        {
            if (EligibilityModules.Contains(module)) return true;
        }
        return false;
    }

    private sealed record EffectivePackage(Subscription Sub, BillingPlan Plan, IReadOnlyList<string> Modules);
    private sealed record BillingPlanLookup(
        IReadOnlyDictionary<string, BillingPlan> ById,
        IReadOnlyDictionary<string, BillingPlan> ByCode);
    private sealed record ResolverOverlayRow(
        string Kind,
        string? ModuleKey,
        bool Enabled,
        FreezeStatus? FreezeStatus,
        DateTimeOffset SortAt,
        DateTimeOffset? ScheduledStartAt);

    private const string FreezeOverlayKind = "freeze";
    private const string ModuleOverlayKind = "module";

    /// <summary>
    /// Returns the resolved plan + parsed modules for a subscription that is
    /// INDIVIDUALLY effective right now — mirroring the same eligibility, fail-low
    /// and expiry checks the primary path applies — or null if it is not effective.
    /// Used by the multi-package aggregation to union across a learner's packages.
    /// </summary>
    private static EffectivePackage? TryResolveEffectivePackage(
        Subscription sub,
        BillingPlanLookup plans,
        IReadOnlySet<string> planVersionIds,
        DateTimeOffset now)
    {
        if (sub.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Trial or SubscriptionStatus.FreezeRequested))
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(sub.PlanId)) return null;

        var plan = ResolveBillingPlan(sub.PlanId, plans);
        if (plan is null) return null;

        if (!string.IsNullOrWhiteSpace(plan.EntitlementsJson) && !IsValidJsonObject(plan.EntitlementsJson))
        {
            return null;
        }
        if (!string.IsNullOrWhiteSpace(sub.PlanVersionId))
        {
            var versionExists = planVersionIds.Contains(sub.PlanVersionId);
            if (!versionExists) return null;
        }

        var expired = sub.Status != SubscriptionStatus.Frozen
            && sub.ExpiresAt is { } exp
            && exp <= now;
        if (expired) return null;

        return new EffectivePackage(sub, plan, ParseDashboardModules(plan.DashboardModulesJson));
    }

    public static IReadOnlyList<string> ParseDashboardModules(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var list = new List<string>(doc.RootElement.GetArrayLength());
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) list.Add(value.Trim());
                }
            }
            return list;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Unions the subtest axis across a learner's plans. A plan whose IncludedSubtestsJson is
    /// empty, "[]" or unparseable imposes no restriction ("all subtests") and therefore dominates
    /// the union — the column is NOT NULL default "[]" and only ever written through the admin's
    /// validated string-array path, so unparseable can only mean legacy data, which must not
    /// silently lock a paying learner out of every subtest.
    /// </summary>
    public static (bool allSubtests, IReadOnlySet<string> subtests) UnionIncludedSubtests(
        IReadOnlyList<BillingPlan> plans)
    {
        var subtests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            var included = ParseStringArray(plan.IncludedSubtestsJson);
            if (included.Count == 0) return (true, subtests);
            foreach (var code in included) subtests.Add(code.ToLowerInvariant());
        }

        return subtests.Count == 0 ? (true, subtests) : (false, subtests);
    }

    /// <summary>
    /// Merges every plan's ContentOverridesJson into one set-of-sets. Shape:
    /// <c>{"videos":{"include":[],"exclude":[]},"materialFolders":{"include":[],"exclude":[]}}</c>.
    /// Malformed JSON yields no overrides — an override can only ever refine the subtest ×
    /// profession resolution, so dropping it falls back to that resolution rather than to open access.
    /// </summary>
    public static ContentOverrideSets MergeContentOverrides(IReadOnlyList<BillingPlan> plans)
    {
        var videoIncludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var videoExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folderIncludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folderExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plan in plans)
        {
            if (string.IsNullOrWhiteSpace(plan.ContentOverridesJson)) continue;
            try
            {
                using var doc = JsonDocument.Parse(plan.ContentOverridesJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                ReadOverrideNode(doc.RootElement, "videos", videoIncludes, videoExcludes);
                ReadOverrideNode(doc.RootElement, "materialFolders", folderIncludes, folderExcludes);
                ReadOverrideNode(doc.RootElement, "material_folders", folderIncludes, folderExcludes);
            }
            catch (JsonException)
            {
                // Ignore this plan's overrides; the subtest × profession resolution still applies.
            }
        }

        var merged = new ContentOverrideSets(videoIncludes, videoExcludes, folderIncludes, folderExcludes);
        return merged.IsEmpty ? ContentOverrideSets.Empty : merged;
    }

    private static void ReadOverrideNode(
        JsonElement root,
        string nodeName,
        HashSet<string> includes,
        HashSet<string> excludes)
    {
        if (!root.TryGetProperty(nodeName, out var node) || node.ValueKind != JsonValueKind.Object) return;
        if (node.TryGetProperty("include", out var inc))
        {
            foreach (var id in ReadStringArray(inc)) includes.Add(id);
        }
        if (node.TryGetProperty("exclude", out var exc))
        {
            foreach (var id in ReadStringArray(exc)) excludes.Add(id);
        }
    }

    private static IReadOnlyList<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ReadStringArray(doc.RootElement);
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        var list = new List<string>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value)) list.Add(value.Trim());
        }
        return list;
    }

    private async Task<string?> LoadActiveProfessionIdAsync(string userId, CancellationToken ct)
    {
        var profession = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.ActiveProfessionId)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(profession) ? null : profession.Trim().ToLowerInvariant();
    }

    private static bool IsValidJsonObject(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static EffectiveEntitlementSnapshot Empty(string? userId, string trace) => new(
        userId,
        HasEligibleSubscription: false,
        IsTrial: false,
        Tier: string.Equals(trace, "anonymous", StringComparison.Ordinal) ? "anonymous" : "free",
        SubscriptionId: null,
        SubscriptionStatus: null,
        PlanId: null,
        PlanVersionId: null,
        PlanCode: null,
        AiQuotaPlanCode: null,
        AiQuotaPlanCodeSource: null,
        ActiveAddOnCodes: Array.Empty<string>(),
        IsFrozen: false,
        Trace: new[] { trace });

    private async Task<IReadOnlyList<Subscription>> LoadOrderedSubscriptionsAsync(string userId, CancellationToken ct)
    {
        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .ToListAsync(ct);

        return subscriptions
            .OrderByDescending(subscription => subscription.ChangedAt)
            .ThenByDescending(subscription => subscription.StartedAt)
            .ThenByDescending(subscription => subscription.Id, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<BillingPlanLookup> LoadBillingPlansAsync(
        IReadOnlyList<Subscription> subscriptions,
        CancellationToken ct)
    {
        var identifiers = subscriptions
            .Select(subscription => subscription.PlanId)
            .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
            .Select(identifier => identifier.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (identifiers.Length == 0)
        {
            return EmptyPlanLookup();
        }

        // IDs and newly-written codes are canonical. Include the existing code
        // normalizer's result so the indexed equality query handles canonical
        // subscriptions without applying LOWER() to either indexed column.
        var queryIdentifiers = identifiers
            .SelectMany(identifier => new[]
            {
                identifier,
                AiQuotaPlanMappingResolver.NormalizeCode(identifier)!,
            })
            .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var loadedPlans = await db.BillingPlans.AsNoTracking()
            .Where(plan => queryIdentifiers.Contains(plan.Id) || queryIdentifiers.Contains(plan.Code))
            .ToListAsync(ct);
        var lookup = CreatePlanLookup(loadedPlans);

        var unresolved = identifiers
            .Where(identifier => ResolveBillingPlan(identifier, lookup) is null)
            .ToArray();
        if (unresolved.Length == 0)
        {
            return lookup;
        }

        // Legacy rows may contain mixed-case codes. PostgreSQL ILIKE is the
        // provider-safe fallback and runs once for the entire distinct key set;
        // non-production providers load once and perform the same comparison in
        // memory. This preserves legacy case-insensitive resolution without
        // wrapping indexed columns in LOWER().
        List<BillingPlan> legacyCandidates;
        if (string.Equals(
            db.Database.ProviderName,
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            StringComparison.Ordinal))
        {
            legacyCandidates = await db.BillingPlans.AsNoTracking()
                .Where(BuildCaseInsensitivePlanPredicate(unresolved))
                .ToListAsync(ct);
        }
        else
        {
            legacyCandidates = await db.BillingPlans.AsNoTracking().ToListAsync(ct);
        }

        loadedPlans.AddRange(legacyCandidates.Where(candidate =>
            unresolved.Any(identifier =>
                string.Equals(candidate.Id, identifier, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Code, identifier, StringComparison.OrdinalIgnoreCase))));
        return CreatePlanLookup(loadedPlans);
    }

    private async Task<HashSet<string>> LoadPlanVersionIdsAsync(
        IReadOnlyList<Subscription> subscriptions,
        CancellationToken ct)
    {
        var requestedVersionIds = subscriptions
            .Select(subscription => subscription.PlanVersionId)
            .Where(versionId => !string.IsNullOrWhiteSpace(versionId))
            .Select(versionId => versionId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestedVersionIds.Length == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var existingVersionIds = await db.BillingPlanVersions.AsNoTracking()
            .Where(version => requestedVersionIds.Contains(version.Id))
            .Select(version => version.Id)
            .ToListAsync(ct);
        return existingVersionIds.ToHashSet(StringComparer.Ordinal);
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadActiveAddOnCodesAsync(
        IReadOnlyList<Subscription> subscriptions,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var subscriptionIds = subscriptions
            .Select(subscription => subscription.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (subscriptionIds.Length == 0)
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        }

        var items = await db.SubscriptionItems.AsNoTracking()
            .Where(item => subscriptionIds.Contains(item.SubscriptionId)
                && item.Status == SubscriptionItemStatus.Active
                && item.StartsAt <= now
                && (item.EndsAt == null || item.EndsAt > now))
            .Select(item => new { item.SubscriptionId, item.ItemCode })
            .ToListAsync(ct);

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var group in items.GroupBy(item => item.SubscriptionId, StringComparer.Ordinal))
        {
            result[group.Key] = group
                .Select(item => item.ItemCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return result;
    }

    private async Task<IReadOnlyList<ResolverOverlayRow>> LoadResolverOverlaysAsync(
        string userId,
        CancellationToken ct)
    {
        var freezes = db.AccountFreezeRecords.AsNoTracking()
            .Where(record => record.UserId == userId && record.IsCurrent)
            .Select(record => new
            {
                Kind = FreezeOverlayKind,
                ModuleKey = (string?)null,
                Enabled = false,
                FreezeStatus = (FreezeStatus?)record.Status,
                SortAt = record.RequestedAt,
                record.ScheduledStartAt,
            });
        var moduleOverrides = db.UserModuleOverrides.AsNoTracking()
            .Where(moduleOverride => moduleOverride.UserId == userId)
            .Select(moduleOverride => new
            {
                Kind = ModuleOverlayKind,
                ModuleKey = (string?)moduleOverride.ModuleKey,
                moduleOverride.Enabled,
                FreezeStatus = (FreezeStatus?)null,
                SortAt = moduleOverride.UpdatedAt,
                ScheduledStartAt = (DateTimeOffset?)null,
            });

        var rows = await freezes.Concat(moduleOverrides).ToListAsync(ct);
        return rows
            .Select(row => new ResolverOverlayRow(
                row.Kind,
                row.ModuleKey,
                row.Enabled,
                row.FreezeStatus,
                row.SortAt,
                row.ScheduledStartAt))
            .ToList();
    }

    private static bool ResolveIsFrozen(
        IReadOnlyList<ResolverOverlayRow> overlays,
        DateTimeOffset now)
    {
        var current = overlays
            .Where(overlay => overlay.Kind == FreezeOverlayKind)
            .OrderByDescending(overlay => overlay.SortAt)
            .FirstOrDefault();

        return current?.FreezeStatus == FreezeStatus.Active
            || (current?.FreezeStatus == FreezeStatus.Scheduled
                && current.ScheduledStartAt is not null
                && current.ScheduledStartAt <= now);
    }

    private static IReadOnlyList<string> ResolveActiveAddOnCodes(
        string subscriptionId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> activeAddOns)
    {
        return activeAddOns.TryGetValue(subscriptionId, out var codes)
            ? codes
            : Array.Empty<string>();
    }

    private static BillingPlan? ResolveBillingPlan(string planIdOrCode, BillingPlanLookup plans)
    {
        var identifier = planIdOrCode.Trim();
        return plans.ById.TryGetValue(identifier, out var byId)
            ? byId
            : plans.ByCode.TryGetValue(identifier, out var byCode)
                ? byCode
                : null;
    }

    private static BillingPlanLookup CreatePlanLookup(IEnumerable<BillingPlan> plans)
    {
        var byId = new Dictionary<string, BillingPlan>(StringComparer.OrdinalIgnoreCase);
        var byCode = new Dictionary<string, BillingPlan>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            byId.TryAdd(plan.Id.Trim(), plan);
            byCode.TryAdd(plan.Code.Trim(), plan);
        }

        return new BillingPlanLookup(byId, byCode);
    }

    private static BillingPlanLookup EmptyPlanLookup() => new(
        new Dictionary<string, BillingPlan>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, BillingPlan>(StringComparer.OrdinalIgnoreCase));

    private static Expression<Func<BillingPlan, bool>> BuildCaseInsensitivePlanPredicate(
        IReadOnlyList<string> identifiers)
    {
        var plan = Expression.Parameter(typeof(BillingPlan), "plan");
        var functions = Expression.Property(
            null,
            typeof(EF).GetProperty(nameof(EF.Functions))!);
        var iLike = typeof(NpgsqlDbFunctionsExtensions).GetMethod(
            nameof(NpgsqlDbFunctionsExtensions.ILike),
            new[] { typeof(DbFunctions), typeof(string), typeof(string), typeof(string) })!;
        var escape = Expression.Constant("\\");
        Expression body = Expression.Constant(false);

        foreach (var identifier in identifiers)
        {
            var pattern = Expression.Constant(EscapeLikePattern(identifier));
            var idMatches = Expression.Call(
                iLike,
                functions,
                Expression.Property(plan, nameof(BillingPlan.Id)),
                pattern,
                escape);
            var codeMatches = Expression.Call(
                iLike,
                functions,
                Expression.Property(plan, nameof(BillingPlan.Code)),
                pattern,
                escape);
            body = Expression.OrElse(body, Expression.OrElse(idMatches, codeMatches));
        }

        return Expression.Lambda<Func<BillingPlan, bool>>(body, plan);
    }

    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private void ObserveDbContextMutations()
    {
        if (observesDbContextMutations)
        {
            return;
        }

        observesDbContextMutations = true;
        db.ChangeTracker.Tracked += (_, args) =>
        {
            if (args.Entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                Invalidate();
            }
        };
        db.ChangeTracker.StateChanged += (_, args) =>
        {
            if (args.NewState is EntityState.Added or EntityState.Modified or EntityState.Deleted
                || args.OldState is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                Invalidate();
            }
        };
        db.SavedChanges += (_, _) => Invalidate();
        db.SaveChangesFailed += (_, _) => Invalidate();
    }
}
