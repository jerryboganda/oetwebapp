using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Entitlements;

public interface IEffectiveEntitlementResolver
{
    Task<EffectiveEntitlementSnapshot> ResolveAsync(string? userId, CancellationToken ct);
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

public sealed class EffectiveEntitlementResolver(
    LearnerDbContext db,
    ILogger<EffectiveEntitlementResolver>? logger = null) : IEffectiveEntitlementResolver
{
    public async Task<EffectiveEntitlementSnapshot> ResolveAsync(string? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Empty(userId, "anonymous");
        }

        var trace = new List<string>();
        var subscriptions = await LoadOrderedSubscriptionsAsync(userId, ct);
        var subscription = subscriptions.Count > 0 ? subscriptions[0] : null;
        var isFrozen = await ResolveIsFrozenAsync(userId, ct);

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
                Trace: trace);
        }

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
                plan = await ResolveBillingPlanAsync(subscription.PlanId, ct);
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
                        var versionExists = await db.BillingPlanVersions.AsNoTracking()
                            .AnyAsync(v => v.Id == subscription.PlanVersionId, ct);
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
            ? await ResolveActiveAddOnCodesAsync(subscription.Id, ct)
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
            Trace: trace);

        if (eligible && plan is not null)
        {
            snapshot = snapshot with
            {
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
        var now = DateTimeOffset.UtcNow;
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
        var permanentTutorBook = await db.Subscriptions.AsNoTracking().AnyAsync(s =>
            s.UserId == userId
            && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial)
            && s.TutorBookUnlocked, ct);
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
            var pkg = await TryResolveEffectivePackageAsync(candidate, ct);
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
                aggAddOns.AddRange(await ResolveActiveAddOnCodesAsync(pkg.Sub.Id, ct));
            }

            // Expiry: a null (permanent) member wins; otherwise the furthest date.
            DateTimeOffset? aggregatedExpiry = effectivePackages.Any(p => p.Sub.ExpiresAt is null)
                ? null
                : effectivePackages.Max(p => p.Sub.ExpiresAt);

            var aiQuota = AiQuotaPlanMappingResolver.Resolve(primaryPkg.Plan);
            var aggIsTrial = primarySub.Status == SubscriptionStatus.Trial;

            if (effectivePackages.Count > 1)
            {
                trace.Add($"packages.{effectivePackages.Count}");
            }

            snapshot = snapshot with
            {
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
        var moduleOverrides = await db.UserModuleOverrides.AsNoTracking()
            .Where(o => o.UserId == userId)
            .ToListAsync(ct);
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

    /// <summary>
    /// Returns the resolved plan + parsed modules for a subscription that is
    /// INDIVIDUALLY effective right now — mirroring the same eligibility, fail-low
    /// and expiry checks the primary path applies — or null if it is not effective.
    /// Used by the multi-package aggregation to union across a learner's packages.
    /// </summary>
    private async Task<EffectivePackage?> TryResolveEffectivePackageAsync(Subscription sub, CancellationToken ct)
    {
        if (sub.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Trial or SubscriptionStatus.FreezeRequested))
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(sub.PlanId)) return null;

        var plan = await ResolveBillingPlanAsync(sub.PlanId, ct);
        if (plan is null) return null;

        if (!string.IsNullOrWhiteSpace(plan.EntitlementsJson) && !IsValidJsonObject(plan.EntitlementsJson))
        {
            return null;
        }
        if (!string.IsNullOrWhiteSpace(sub.PlanVersionId))
        {
            var versionExists = await db.BillingPlanVersions.AsNoTracking()
                .AnyAsync(v => v.Id == sub.PlanVersionId, ct);
            if (!versionExists) return null;
        }

        var now = DateTimeOffset.UtcNow;
        var expired = sub.Status != SubscriptionStatus.Frozen
            && sub.ExpiresAt is { } exp
            && exp <= now;
        if (expired) return null;

        return new EffectivePackage(sub, plan, ParseDashboardModules(plan.DashboardModulesJson));
    }

    private static IReadOnlyList<string> ParseDashboardModules(string json)
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

    private async Task<BillingPlan?> ResolveBillingPlanAsync(string planIdOrCode, CancellationToken ct)
    {
        var normalizedPlan = planIdOrCode.Trim().ToLowerInvariant();
        return await db.BillingPlans.AsNoTracking()
            .FirstOrDefaultAsync(plan => plan.Id.ToLower() == normalizedPlan || plan.Code.ToLower() == normalizedPlan, ct);
    }

    private async Task<IReadOnlyList<string>> ResolveActiveAddOnCodesAsync(string subscriptionId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var items = await db.SubscriptionItems.AsNoTracking()
            .Where(item => item.SubscriptionId == subscriptionId
                && item.Status == SubscriptionItemStatus.Active
                && item.StartsAt <= now
                && (item.EndsAt == null || item.EndsAt > now))
            .Select(item => item.ItemCode)
            .ToListAsync(ct);

        return items
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<bool> ResolveIsFrozenAsync(string userId, CancellationToken ct)
    {
        var records = await db.AccountFreezeRecords.AsNoTracking()
            .Where(record => record.UserId == userId && record.IsCurrent)
            .ToListAsync(ct);

        var current = records
            .OrderByDescending(record => record.RequestedAt)
            .FirstOrDefault();

        return current?.Status == FreezeStatus.Active
            || (current?.Status == FreezeStatus.Scheduled
                && current.ScheduledStartAt is not null
                && current.ScheduledStartAt <= DateTimeOffset.UtcNow);
    }
}
