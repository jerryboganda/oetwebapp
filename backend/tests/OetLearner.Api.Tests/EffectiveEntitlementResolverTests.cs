using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

public class EffectiveEntitlementResolverTests
{
    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    [Fact]
    public async Task ResolveAsync_UsesLatestSubscriptionAndDoesNotRescueCancelledWithStaleActive()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan { Id = "plan-pro", Code = "Pro", Name = "Pro" });
        db.Subscriptions.AddRange(
            new Subscription
            {
                Id = "sub-old-active",
                UserId = "learner-1",
                PlanId = "plan-pro",
                Status = SubscriptionStatus.Active,
                StartedAt = now.AddMonths(-2),
                ChangedAt = now.AddMonths(-2),
            },
            new Subscription
            {
                Id = "sub-new-cancelled",
                UserId = "learner-1",
                PlanId = "plan-pro",
                Status = SubscriptionStatus.Cancelled,
                StartedAt = now.AddMonths(-1),
                ChangedAt = now,
            });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-1", default);

        Assert.False(snapshot.HasEligibleSubscription);
        Assert.Equal("free", snapshot.Tier);
        Assert.Equal(SubscriptionStatus.Cancelled, snapshot.SubscriptionStatus);
        Assert.Null(snapshot.PlanCode);
    }

    [Fact]
    public async Task ResolveAsync_ResolvesPlanByIdOrCodeAndCapturesActiveAddOns()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan { Id = "plan-pro", Code = "pro", Name = "Pro" });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-active",
            UserId = "learner-2",
            PlanId = "PRO",
            Status = SubscriptionStatus.Trial,
            StartedAt = now.AddDays(-2),
            ChangedAt = now.AddDays(-2),
        });
        db.SubscriptionItems.AddRange(
            new SubscriptionItem
            {
                Id = "addon-active",
                SubscriptionId = "sub-active",
                ItemCode = "review_booster",
                Status = SubscriptionItemStatus.Active,
                StartsAt = now.AddDays(-1),
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now.AddDays(-1),
            },
            new SubscriptionItem
            {
                Id = "addon-expired",
                SubscriptionId = "sub-active",
                ItemCode = "expired_booster",
                Status = SubscriptionItemStatus.Active,
                StartsAt = now.AddDays(-10),
                EndsAt = now.AddDays(-1),
                CreatedAt = now.AddDays(-10),
                UpdatedAt = now.AddDays(-1),
            });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-2", default);

        Assert.True(snapshot.HasEligibleSubscription);
        Assert.True(snapshot.IsTrial);
        Assert.Equal("trial", snapshot.Tier);
        Assert.Equal("pro", snapshot.PlanCode);
        Assert.Equal(new[] { "review_booster" }, snapshot.ActiveAddOnCodes);
    }

    [Fact]
    public async Task ResolveAsync_SurfacesActiveFreezeOverlayWithoutRemovingPlan()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan { Id = "plan-pro", Code = "pro", Name = "Pro" });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-active",
            UserId = "learner-3",
            PlanId = "plan-pro",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-10),
            ChangedAt = now.AddDays(-10),
        });
        db.AccountFreezeRecords.Add(new AccountFreezeRecord
        {
            Id = "freeze-active",
            UserId = "learner-3",
            Status = FreezeStatus.Active,
            IsCurrent = true,
            RequestedAt = now,
            StartedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-3", default);

        Assert.True(snapshot.HasEligibleSubscription);
        Assert.True(snapshot.IsFrozen);
        Assert.Equal("pro", snapshot.PlanCode);
        Assert.Contains("freeze.active", snapshot.Trace);
    }
}