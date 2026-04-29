using Microsoft.EntityFrameworkCore;
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
    string? PlanCode,
    IReadOnlyList<string> ActiveAddOnCodes,
    bool IsFrozen,
    IReadOnlyList<string> Trace);

public sealed class EffectiveEntitlementResolver(LearnerDbContext db) : IEffectiveEntitlementResolver
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
                PlanCode: null,
                ActiveAddOnCodes: Array.Empty<string>(),
                IsFrozen: isFrozen,
                Trace: trace);
        }

        trace.Add($"subscription.latest.{subscription.Status}");
        var eligible = subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial;
        var isTrial = subscription.Status == SubscriptionStatus.Trial;

        BillingPlan? plan = null;
        if (eligible && !string.IsNullOrWhiteSpace(subscription.PlanId))
        {
            plan = await ResolveBillingPlanAsync(subscription.PlanId, ct);
            trace.Add(plan is null ? "plan.missing" : $"plan.{plan.Code}");
        }

        var addOnCodes = eligible
            ? await ResolveActiveAddOnCodesAsync(subscription.Id, ct)
            : Array.Empty<string>();

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
            PlanCode: NormalizeCode(plan?.Code),
            ActiveAddOnCodes: addOnCodes,
            IsFrozen: isFrozen,
            Trace: trace);
    }

    private static EffectiveEntitlementSnapshot Empty(string? userId, string trace) => new(
        userId,
        HasEligibleSubscription: false,
        IsTrial: false,
        Tier: string.Equals(trace, "anonymous", StringComparison.Ordinal) ? "anonymous" : "free",
        SubscriptionId: null,
        SubscriptionStatus: null,
        PlanId: null,
        PlanCode: null,
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

    private static string? NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToLowerInvariant();

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