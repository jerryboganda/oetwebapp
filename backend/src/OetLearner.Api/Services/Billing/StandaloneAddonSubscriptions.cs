using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Hidden container for AI / skill / mock packs bought (or granted) with no
/// course package. The entitlement resolver ignores this PlanId, so it cannot
/// steal Full Course access.
/// </summary>
public static class StandaloneAddonSubscriptions
{
    public static async Task<Subscription> EnsureAsync(
        LearnerDbContext db,
        string userId,
        DateTimeOffset now,
        CancellationToken ct,
        SubscriptionStatus status = SubscriptionStatus.Active)
    {
        var existing = await db.Subscriptions.FirstOrDefaultAsync(
            s => s.UserId == userId
                 && s.PlanId == Subscription.StandaloneAddonPlanId
                 && s.Status != SubscriptionStatus.Cancelled,
            ct);
        if (existing is not null)
        {
            if (status == SubscriptionStatus.Active
                && existing.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Trial or SubscriptionStatus.FreezeRequested))
            {
                existing.Status = SubscriptionStatus.Active;
                existing.ChangedAt = now;
                await db.SaveChangesAsync(ct);
            }

            return existing;
        }

        var subscription = new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = userId,
            PlanId = Subscription.StandaloneAddonPlanId,
            Status = status,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddYears(10),
            PriceAmount = 0,
            Currency = "AUD",
            Interval = "one_time",
            AccessDurationDays = 0,
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);
        return subscription;
    }
}
