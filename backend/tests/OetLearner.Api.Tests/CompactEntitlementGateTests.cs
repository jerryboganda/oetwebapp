using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Pronunciation;

namespace OetLearner.Api.Tests;

public class CompactEntitlementGateTests
{
    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    [Fact]
    public async Task PronunciationEntitlement_LatestCancelledSubscriptionUsesFreeTier()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan { Id = "plan-pro", Code = "pro", Name = "Pro" });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-cancelled",
            UserId = "learner-pron",
            PlanId = "plan-pro",
            Status = SubscriptionStatus.Cancelled,
            StartedAt = now.AddMonths(-1),
            ChangedAt = now,
        });
        await db.SaveChangesAsync();

        var service = new PronunciationEntitlementService(
            db,
            Options.Create(new PronunciationOptions { FreeTierWeeklyAttemptLimit = 2, FreeTierWindowDays = 7 }),
            new EffectiveEntitlementResolver(db));

        var result = await service.CheckAsync("learner-pron", default);

        Assert.True(result.Allowed);
        Assert.Equal("free", result.Tier);
        Assert.Equal(2, result.Remaining);
    }

    [Fact]
    public async Task PronunciationEntitlement_ActiveSubscriptionUnlimited()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan { Id = "plan-pro", Code = "pro", Name = "Pro" });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-active",
            UserId = "learner-pron-paid",
            PlanId = "plan-pro",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddMonths(-1),
            ChangedAt = now,
        });
        await db.SaveChangesAsync();

        var service = new PronunciationEntitlementService(
            db,
            Options.Create(new PronunciationOptions { FreeTierWeeklyAttemptLimit = 1, FreeTierWindowDays = 7 }),
            new EffectiveEntitlementResolver(db));

        var result = await service.CheckAsync("learner-pron-paid", default);

        Assert.True(result.Allowed);
        Assert.Equal("paid", result.Tier);
        Assert.Equal(int.MaxValue, result.Remaining);
    }

    [Fact]
    public async Task ConversationEntitlement_LatestCancelledSubscriptionUsesFreeTier()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan { Id = "plan-pro", Code = "pro", Name = "Pro" });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-cancelled",
            UserId = "learner-conversation",
            PlanId = "plan-pro",
            Status = SubscriptionStatus.Cancelled,
            StartedAt = now.AddMonths(-1),
            ChangedAt = now,
        });
        await db.SaveChangesAsync();

        var service = new ConversationEntitlementService(
            db,
            new StaticConversationOptionsProvider(new ConversationOptions { Enabled = true, FreeTierSessionsLimit = 2, FreeTierWindowDays = 7 }),
            new EffectiveEntitlementResolver(db));

        var result = await service.CheckAsync("learner-conversation", default);

        Assert.True(result.Allowed);
        Assert.Equal("free", result.Tier);
        Assert.Equal(2, result.Remaining);
    }

    [Fact]
    public async Task ConversationEntitlement_TrialSubscriptionUnlimited()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan { Id = "plan-pro", Code = "pro", Name = "Pro" });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-trial",
            UserId = "learner-conversation-trial",
            PlanId = "plan-pro",
            Status = SubscriptionStatus.Trial,
            StartedAt = now.AddDays(-3),
            ChangedAt = now,
        });
        await db.SaveChangesAsync();

        var service = new ConversationEntitlementService(
            db,
            new StaticConversationOptionsProvider(new ConversationOptions { Enabled = true, FreeTierSessionsLimit = 1, FreeTierWindowDays = 7 }),
            new EffectiveEntitlementResolver(db));

        var result = await service.CheckAsync("learner-conversation-trial", default);

        Assert.True(result.Allowed);
        Assert.Equal("trial", result.Tier);
        Assert.Equal(int.MaxValue, result.Remaining);
    }

    private sealed class StaticConversationOptionsProvider(ConversationOptions options) : IConversationOptionsProvider
    {
        public Task<ConversationOptions> GetAsync(CancellationToken ct = default) => Task.FromResult(options);

        public void Invalidate()
        {
        }
    }
}