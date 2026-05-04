using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Content;
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

    [Fact]
    public async Task ContentEntitlement_LatestCancelledSubscriptionDeniesPremiumContent()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-content-pro",
            Code = "content-pro",
            Name = "Content Pro",
            EntitlementsJson = System.Text.Json.JsonSerializer.Serialize(new { content = new { tier = "premium" } }),
            IncludedSubtestsJson = "[]"
        });
        db.Subscriptions.AddRange(
            new Subscription
            {
                Id = "sub-old-active-content",
                UserId = "learner-content-cancelled",
                PlanId = "content-pro",
                Status = SubscriptionStatus.Active,
                StartedAt = now.AddMonths(-2),
                ChangedAt = now.AddMonths(-2)
            },
            new Subscription
            {
                Id = "sub-latest-cancelled-content",
                UserId = "learner-content-cancelled",
                PlanId = "content-pro",
                Status = SubscriptionStatus.Cancelled,
                StartedAt = now.AddMonths(-1),
                ChangedAt = now
            });
        await db.SaveChangesAsync();

        var service = new ContentEntitlementService(db, new EffectiveEntitlementResolver(db));
        var result = await service.AllowAccessAsync("learner-content-cancelled", PremiumPaper("paper-content-cancelled", "reading"), default);

        Assert.False(result.Allowed);
        Assert.Equal("no_active_subscription", result.Reason);
    }

    [Fact]
    public async Task ContentEntitlement_UsesPlanVersionGrantWhenLivePlanMutates()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-versioned-content",
            Code = "versioned-content",
            Name = "Versioned Content",
            EntitlementsJson = System.Text.Json.JsonSerializer.Serialize(new { content = new { tier = "free", subtests = new[] { "writing" } } }),
            IncludedSubtestsJson = "[]"
        });
        db.BillingPlanVersions.Add(new BillingPlanVersion
        {
            Id = "plan-versioned-content-v1",
            PlanId = "plan-versioned-content",
            VersionNumber = 1,
            Code = "versioned-content",
            Name = "Versioned Content v1",
            EntitlementsJson = System.Text.Json.JsonSerializer.Serialize(new { content = new { tier = "free", subtests = new[] { "reading" } } }),
            IncludedSubtestsJson = "[]",
            CreatedAt = now.AddDays(-7)
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-versioned-content",
            UserId = "learner-versioned-content",
            PlanId = "versioned-content",
            PlanVersionId = "plan-versioned-content-v1",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-6),
            ChangedAt = now.AddDays(-6)
        });
        await db.SaveChangesAsync();

        var service = new ContentEntitlementService(db, new EffectiveEntitlementResolver(db));
        var result = await service.AllowAccessAsync("learner-versioned-content", PremiumPaper("paper-versioned-reading", "reading"), default);

        Assert.True(result.Allowed);
        Assert.Equal("plan_grants_subtest", result.Reason);
    }

    [Fact]
    public async Task ContentEntitlement_MissingPlanVersionAnchorFailsClosed()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-live-premium-content",
            Code = "live-premium-content",
            Name = "Live Premium Content",
            EntitlementsJson = System.Text.Json.JsonSerializer.Serialize(new { content = new { tier = "premium" } }),
            IncludedSubtestsJson = "[]"
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-broken-anchor-content",
            UserId = "learner-broken-anchor-content",
            PlanId = "live-premium-content",
            PlanVersionId = "missing-version-anchor",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now
        });
        await db.SaveChangesAsync();

        var service = new ContentEntitlementService(db, new EffectiveEntitlementResolver(db));
        var result = await service.AllowAccessAsync("learner-broken-anchor-content", PremiumPaper("paper-broken-anchor", "reading"), default);

        Assert.False(result.Allowed);
        // Slice E fail-low: a dangling PlanVersionId now demotes the
        // subscription to FREE upstream, so the downstream content gate
        // reports no_active_subscription rather than plan_does_not_grant.
        // Either reason is a closed-failed denial.
        Assert.Contains(result.Reason, new[] { "plan_does_not_grant", "no_active_subscription" });
    }

    [Fact]
    public async Task ContentEntitlement_MissingLivePlanWithoutSnapshotFailsClosed()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-missing-live-plan-content",
            UserId = "learner-missing-live-plan-content",
            PlanId = "missing-live-plan",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now
        });
        await db.SaveChangesAsync();

        var service = new ContentEntitlementService(db, new EffectiveEntitlementResolver(db));
        var result = await service.AllowAccessAsync("learner-missing-live-plan-content", PremiumPaper("paper-missing-live-plan", "reading"), default);

        Assert.False(result.Allowed);
        // Slice E fail-low: a missing live plan now demotes the
        // subscription to FREE upstream, so the gate reports
        // no_active_subscription rather than plan_does_not_grant.
        Assert.Contains(result.Reason, new[] { "plan_does_not_grant", "no_active_subscription" });
    }

    [Fact]
    public void ContentBundle_ExplicitContentIgnoresLegacyIncludedSubtests()
    {
        var bundle = ContentEntitlementService.ParseBundle(
            System.Text.Json.JsonSerializer.Serialize(new { content = new { tier = "free", subtests = new[] { "writing" } } }),
            System.Text.Json.JsonSerializer.Serialize(new[] { "reading" }));

        Assert.Contains("writing", bundle.GrantedSubtests);
        Assert.DoesNotContain("reading", bundle.GrantedSubtests);
        Assert.Equal("free", bundle.Tier);
    }

    private static ContentPaper PremiumPaper(string id, string subtestCode) => new()
    {
        Id = id,
        SubtestCode = subtestCode,
        Title = $"{subtestCode} premium paper",
        Slug = id,
        Status = ContentStatus.Published,
        TagsCsv = "access:premium",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class StaticConversationOptionsProvider(ConversationOptions options) : IConversationOptionsProvider
    {
        public Task<ConversationOptions> GetAsync(CancellationToken ct = default) => Task.FromResult(options);

        public void Invalidate()
        {
        }
    }
}