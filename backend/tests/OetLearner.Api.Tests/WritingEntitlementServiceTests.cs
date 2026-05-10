using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Writing;
using Xunit;

namespace OetLearner.Api.Tests;

public class WritingEntitlementServiceTests
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
        var svc = new WritingEntitlementService(db, resolver, provider);
        return (svc, provider, db);
    }

    [Fact]
    public async Task PremiumSubscriber_AllowedUnlimited()
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

        Assert.True(result.Allowed);
        Assert.Equal("paid", result.Tier);
        Assert.Equal(int.MaxValue, result.Remaining);
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

public class WritingOptionsProviderTests
{
    private static DbContextOptions<LearnerDbContext> NewInMemoryOptions()
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    [Fact]
    public async Task Get_BootstrapsSingletonRow_OnFirstRead()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new WritingOptionsProvider(db, cache);

        var first = await provider.GetAsync(default);

        Assert.Equal("global", first.Id);
        Assert.True(first.AiGradingEnabled);
        Assert.True(first.AiCoachEnabled);
        Assert.False(first.FreeTierEnabled);
        Assert.Equal(0, first.FreeTierLimit);
        Assert.Equal(7, first.FreeTierWindowDays);
    }

    [Fact]
    public async Task UpdateThenGet_ReturnsUpdatedValues_AndInvalidatesCache()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new WritingOptionsProvider(db, cache);

        // Seed cache.
        var initial = await provider.GetAsync(default);
        Assert.True(initial.AiGradingEnabled);

        // Update flips kill switch + enables free tier.
        var saved = await provider.UpdateAsync(new WritingOptions
        {
            Id = "global",
            AiGradingEnabled = false,
            AiCoachEnabled = true,
            KillSwitchReason = "Maintenance window",
            FreeTierEnabled = true,
            FreeTierLimit = 5,
            FreeTierWindowDays = 14,
        }, "admin-99", default);

        Assert.False(saved.AiGradingEnabled);
        Assert.True(saved.FreeTierEnabled);

        // Re-read after invalidation must reflect the update, not cached old.
        var after = await provider.GetAsync(default);
        Assert.False(after.AiGradingEnabled);
        Assert.Equal("Maintenance window", after.KillSwitchReason);
        Assert.Equal(5, after.FreeTierLimit);
        Assert.Equal(14, after.FreeTierWindowDays);
        Assert.Equal("admin-99", after.UpdatedByAdminId);

        // Audit event written.
        var audits = await db.AuditEvents.Where(a => a.ResourceType == "WritingOptions").ToListAsync();
        Assert.Single(audits);
        Assert.Equal("WritingOptionsUpdated", audits[0].Action);
    }

    [Fact]
    public async Task Update_RejectsNegativeLimit()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new WritingOptionsProvider(db, cache);

        await Assert.ThrowsAsync<OetLearner.Api.Services.ApiException>(() =>
            provider.UpdateAsync(new WritingOptions
            {
                Id = "global",
                FreeTierLimit = -1,
                FreeTierWindowDays = 7,
            }, "admin-1", default));
    }

    [Fact]
    public async Task Update_RejectsOutOfRangeWindowDays()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new WritingOptionsProvider(db, cache);

        await Assert.ThrowsAsync<OetLearner.Api.Services.ApiException>(() =>
            provider.UpdateAsync(new WritingOptions
            {
                Id = "global",
                FreeTierLimit = 5,
                FreeTierWindowDays = 0,
            }, "admin-1", default));

        await Assert.ThrowsAsync<OetLearner.Api.Services.ApiException>(() =>
            provider.UpdateAsync(new WritingOptions
            {
                Id = "global",
                FreeTierLimit = 5,
                FreeTierWindowDays = 366,
            }, "admin-1", default));
    }
}
