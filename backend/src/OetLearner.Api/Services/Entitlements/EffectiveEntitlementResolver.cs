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
    IReadOnlyList<string> Trace);

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

        return new EffectiveEntitlementSnapshot(
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