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
    public bool TutorBookDiscountEnabled { get; init; }
    public int WritingAssessmentsRemaining { get; init; }
    public int SpeakingSessionsRemaining { get; init; }
    public int AiCreditsRemaining { get; init; }
    public bool TutorBookUnlocked { get; init; }
    public bool BasicEnglishUnlocked { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? ProductCategory { get; init; }
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
        var subscription = await ResolveLatestSubscriptionAsync(userId, ct);
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
        var eligible = subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial;
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
        var isExpired = subscription.ExpiresAt is { } exp && exp <= now;
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
        // The standalone `tutor-book` plan grants accessDays 9999 => ExpiresAt
        // == null, which is permanent and survives a separate course's expiry.
        // This is a NARROW, additive grant: it never elevates the course, only
        // re-enables the Tutor Book modules the holder paid for outright. The
        // ExpiresAt==null filter is the product rule — an add-on Tutor Book on
        // a COURSE sub (ExpiresAt set) expires WITH the course and is excluded.
        var permanentTutorBook = await db.Subscriptions.AsNoTracking().AnyAsync(s =>
            s.UserId == userId
            && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial)
            && s.ExpiresAt == null
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

        return snapshot;
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

    private async Task<Subscription?> ResolveLatestSubscriptionAsync(string userId, CancellationToken ct)
    {
        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .ToListAsync(ct);

        return subscriptions
            .OrderByDescending(subscription => subscription.ChangedAt)
            .ThenByDescending(subscription => subscription.StartedAt)
            .ThenByDescending(subscription => subscription.Id, StringComparer.Ordinal)
            .FirstOrDefault();
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