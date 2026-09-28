using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Writing;
using Xunit;

namespace OetLearner.Api.Tests.Writing;

public class WritingEntitlementServiceDbTests
{
    private static DbContextOptions<LearnerDbContext> NewInMemoryOptions()
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static (WritingEntitlementService svc, WritingOptionsProvider provider, LearnerDbContext db)
        BuildServices(LearnerDbContext db)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new WritingOptionsProvider(db, cache);
        var resolver = new EffectiveEntitlementResolver(db);
        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var svc = new WritingEntitlementService(db, resolver, provider, credits);
        return (svc, provider, db);
    }

    [Fact]
    public async Task PremiumSubscriber_WithoutWritingGrant_IsNotUnlimited()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        db.BillingPlans.Add(new BillingPlan { Id = "pro", Code = "pro", Name = "Pro" });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-1",
            UserId = "user-pro",
            PlanId = "pro",
            Status = SubscriptionStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddMonths(-1),
            ChangedAt = DateTimeOffset.UtcNow.AddMonths(-1),
        });
        await db.SaveChangesAsync();

        var (svc, _, _) = BuildServices(db);
        var result = await svc.CheckAsync("user-pro", default);

        Assert.False(result.Allowed);
        Assert.Equal(0, result.Remaining);
        Assert.NotEqual(int.MaxValue, result.Remaining);
        Assert.DoesNotContain("unlimited writing attempts", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FreeTierDisabled_ByDefault_ReturnsPremiumRequired()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var (svc, _, _) = BuildServices(db);

        // Default WritingOptions has FreeTierEnabled = false.
        var result = await svc.CheckAsync("user-free", default);

        Assert.False(result.Allowed);
        Assert.Equal("free", result.Tier);
        Assert.Equal("premium_required", result.Reason);
    }

    [Fact]
    public async Task AiPackageCreditsWithoutCourse_AllowsWriting()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var now = DateTimeOffset.UtcNow;
        db.AiPackageCreditAccounts.Add(new AiPackageCreditAccount
        {
            Id = "acct-ai-writing",
            UserId = "user-ai-pack",
            FlexibleCredits = 5,
            ListeningTestsRemaining = 0,
            ReadingTestsRemaining = 0,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddDays(30)
        });
        await db.SaveChangesAsync();

        var (svc, _, _) = BuildServices(db);
        var result = await svc.CheckAsync("user-ai-pack", default);

        Assert.True(result.Allowed);
        Assert.Equal("ai_package", result.Tier);
    }

    [Fact]
    public async Task FreeTierEnabled_UnderLimit_AllowsWithRemaining()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var (svc, provider, _) = BuildServices(db);

        // Enable free tier with limit=3.
        await provider.UpdateAsync(new WritingOptions
        {
            Id = "global",
            AiGradingEnabled = true,
            AiCoachEnabled = true,
            FreeTierEnabled = true,
            FreeTierLimit = 3,
            FreeTierWindowDays = 7,
        }, "admin-1", default);

        // Seed two completed Writing attempts in the window.
        for (var i = 0; i < 2; i++)
        {
            db.Attempts.Add(new Attempt
            {
                Id = $"wa-{i}",
                UserId = "user-free",
                ContentId = "c-1",
                SubtestCode = "writing",
                Context = "practice",
                Mode = "standard",
                State = AttemptState.Completed,
                StartedAt = DateTimeOffset.UtcNow.AddHours(-i - 2),
                CompletedAt = DateTimeOffset.UtcNow.AddHours(-i - 1),
            });
        }
        await db.SaveChangesAsync();

        var result = await svc.CheckAsync("user-free", default);

        Assert.True(result.Allowed);
        Assert.Equal("free", result.Tier);
        Assert.Equal(1, result.Remaining);
        Assert.Equal(3, result.LimitPerWindow);
    }

    [Fact]
    public async Task FreeTierEnabled_AtLimit_ReturnsQuotaExceeded()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var (svc, provider, _) = BuildServices(db);

        await provider.UpdateAsync(new WritingOptions
        {
            Id = "global",
            AiGradingEnabled = true,
            AiCoachEnabled = true,
            FreeTierEnabled = true,
            FreeTierLimit = 2,
            FreeTierWindowDays = 7,
        }, "admin-1", default);

        for (var i = 0; i < 2; i++)
        {
            db.Attempts.Add(new Attempt
            {
                Id = $"wa-{i}",
                UserId = "user-free",
                ContentId = "c-1",
                SubtestCode = "writing",
                Context = "practice",
                Mode = "standard",
                State = AttemptState.Completed,
                StartedAt = DateTimeOffset.UtcNow.AddHours(-i - 2),
                CompletedAt = DateTimeOffset.UtcNow.AddHours(-i - 1),
            });
        }
        await db.SaveChangesAsync();

        var result = await svc.CheckAsync("user-free", default);

        Assert.False(result.Allowed);
        Assert.Equal("free", result.Tier);
        Assert.Equal(0, result.Remaining);
        Assert.Equal("quota_exceeded", result.Reason);
        Assert.NotNull(result.ResetAt);
    }

    [Fact]
    public async Task AnonymousBlocked()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var (svc, _, _) = BuildServices(db);

        var result = await svc.CheckAsync(null, default);

        Assert.False(result.Allowed);
        Assert.Equal("anonymous", result.Tier);
    }
}
