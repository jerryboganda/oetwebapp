using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Grammar;
using Xunit;

namespace OetLearner.Api.Tests;

public class GrammarEntitlementServiceTests
{
    private static (LearnerDbContext db, GrammarEntitlementService svc) Build()
    {
        var opts = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(opts);
        return (db, new GrammarEntitlementService(db));
    }

    private static Subscription Sub(string userId, SubscriptionStatus status) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        UserId = userId,
        PlanId = "plan",
        Status = status,
        StartedAt = DateTimeOffset.UtcNow.AddDays(-30),
        ChangedAt = DateTimeOffset.UtcNow.AddDays(-30),
        NextRenewalAt = DateTimeOffset.UtcNow.AddDays(30),
        PriceAmount = 9.99m,
    };

    private static LearnerGrammarProgress Completed(string userId, DateTimeOffset completedAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        LessonId = "lesson-" + Guid.NewGuid().ToString("N")[..8],
        Status = "completed",
        CompletedAt = completedAt,
        StartedAt = completedAt.AddMinutes(-10),
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Anonymous_user_is_blocked(string? userId)
    {
        var (db, svc) = Build();
        var r = await svc.CheckAsync(userId, default);
        Assert.False(r.Allowed);
        Assert.Equal("anonymous", r.Tier);
        Assert.Equal(0, r.Remaining);
        Assert.Equal(0, r.LimitPerWindow);
        Assert.Equal(7, r.WindowDays);
        Assert.Null(r.ResetAt);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Active_subscription_grants_paid_unlimited()
    {
        var (db, svc) = Build();
        db.Subscriptions.Add(Sub("u1", SubscriptionStatus.Active));
        await db.SaveChangesAsync();

        var r = await svc.CheckAsync("u1", default);
        Assert.True(r.Allowed);
        Assert.Equal("paid", r.Tier);
        Assert.Equal(int.MaxValue, r.Remaining);
        Assert.Null(r.ResetAt);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Trial_subscription_grants_trial_unlimited()
    {
        var (db, svc) = Build();
        db.Subscriptions.Add(Sub("u1", SubscriptionStatus.Trial));
        await db.SaveChangesAsync();

        var r = await svc.CheckAsync("u1", default);
        Assert.True(r.Allowed);
        Assert.Equal("trial", r.Tier);
        await db.DisposeAsync();
    }

    [Theory]
    [InlineData(SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Expired)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Suspended)]
    [InlineData(SubscriptionStatus.Pending)]
    public async Task Inactive_subscription_falls_back_to_free_tier(SubscriptionStatus status)
    {
        var (db, svc) = Build();
        db.Subscriptions.Add(Sub("u1", status));
        await db.SaveChangesAsync();

        var r = await svc.CheckAsync("u1", default);
        Assert.True(r.Allowed);
        Assert.Equal("free", r.Tier);
        Assert.Equal(3, r.Remaining);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Free_tier_with_no_completions_returns_full_quota()
    {
        var (db, svc) = Build();
        var r = await svc.CheckAsync("u1", default);
        Assert.True(r.Allowed);
        Assert.Equal("free", r.Tier);
        Assert.Equal(3, r.Remaining);
        Assert.Equal(3, r.LimitPerWindow);
        Assert.Null(r.ResetAt);
        Assert.Contains("3", r.Reason);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Free_tier_counts_only_completed_records_inside_window()
    {
        var (db, svc) = Build();
        var now = DateTimeOffset.UtcNow;
        db.Set<LearnerGrammarProgress>().AddRange(
            Completed("u1", now.AddDays(-1)),
            Completed("u1", now.AddDays(-15)),                                    // outside window
            Completed("u2", now.AddDays(-1)),                                     // other user
            new LearnerGrammarProgress
            {
                Id = Guid.NewGuid(),
                UserId = "u1",
                LessonId = "x",
                Status = "in_progress",                                           // not completed
                StartedAt = now,
            });
        await db.SaveChangesAsync();

        var r = await svc.CheckAsync("u1", default);
        Assert.True(r.Allowed);
        Assert.Equal(2, r.Remaining);
        Assert.NotNull(r.ResetAt);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Free_tier_blocks_when_quota_exhausted_and_returns_reset_at()
    {
        var (db, svc) = Build();
        var now = DateTimeOffset.UtcNow;
        var earliest = now.AddDays(-3);
        db.Set<LearnerGrammarProgress>().AddRange(
            Completed("u1", earliest),
            Completed("u1", now.AddDays(-2)),
            Completed("u1", now.AddDays(-1)));
        await db.SaveChangesAsync();

        var r = await svc.CheckAsync("u1", default);
        Assert.False(r.Allowed);
        Assert.Equal("free", r.Tier);
        Assert.Equal(0, r.Remaining);
        Assert.Equal(3, r.LimitPerWindow);
        Assert.Equal(7, r.WindowDays);
        Assert.NotNull(r.ResetAt);
        var diff = (r.ResetAt!.Value - earliest).TotalDays;
        Assert.Equal(7, diff, 1);
        Assert.Contains("Upgrade", r.Reason, StringComparison.OrdinalIgnoreCase);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Free_tier_caps_remaining_at_zero_when_completions_exceed_limit()
    {
        var (db, svc) = Build();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            db.Set<LearnerGrammarProgress>().Add(Completed("u1", now.AddHours(-i)));
        }
        await db.SaveChangesAsync();

        var r = await svc.CheckAsync("u1", default);
        Assert.False(r.Allowed);
        Assert.Equal(0, r.Remaining);
        await db.DisposeAsync();
    }
}
