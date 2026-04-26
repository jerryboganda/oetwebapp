using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Pronunciation;
using Xunit;

namespace OetLearner.Api.Tests;

public class PronunciationEntitlementServiceTests
{
    private static (LearnerDbContext db, PronunciationEntitlementService svc) Build(
        PronunciationOptions? opts = null)
    {
        var dbOpts = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(dbOpts);
        var svc = new PronunciationEntitlementService(
            db,
            Options.Create(opts ?? new PronunciationOptions()));
        return (db, svc);
    }

    private static Subscription Sub(string userId, SubscriptionStatus status) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        UserId = userId,
        PlanId = "p",
        Status = status,
        StartedAt = DateTimeOffset.UtcNow.AddDays(-30),
        ChangedAt = DateTimeOffset.UtcNow.AddDays(-30),
        NextRenewalAt = DateTimeOffset.UtcNow.AddDays(30),
        PriceAmount = 9.99m,
    };

    private static PronunciationAttempt Attempt(
        string userId, DateTimeOffset createdAt, string status = "queued") => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        UserId = userId,
        DrillId = "drill-1",
        Status = status,
        CreatedAt = createdAt,
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
        Assert.Equal(7, r.WindowDays);
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

    [Fact]
    public async Task Free_tier_with_negative_limit_is_unlimited()
    {
        var (db, svc) = Build(new PronunciationOptions { FreeTierWeeklyAttemptLimit = -1 });
        var r = await svc.CheckAsync("u1", default);
        Assert.True(r.Allowed);
        Assert.Equal("free", r.Tier);
        Assert.Equal(int.MaxValue, r.Remaining);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Free_tier_excludes_refused_attempts_from_quota()
    {
        var (db, svc) = Build(new PronunciationOptions { FreeTierWeeklyAttemptLimit = 5, FreeTierWindowDays = 7 });
        var now = DateTimeOffset.UtcNow;
        db.PronunciationAttempts.AddRange(
            Attempt("u1", now.AddDays(-1)),                           // counts
            Attempt("u1", now.AddDays(-2), status: "refused"),        // ignored
            Attempt("u1", now.AddDays(-30)),                          // outside window
            Attempt("u2", now.AddDays(-1)));                          // other user
        await db.SaveChangesAsync();

        var r = await svc.CheckAsync("u1", default);
        Assert.True(r.Allowed);
        Assert.Equal(4, r.Remaining);
        Assert.Equal(5, r.LimitPerWindow);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Free_tier_blocks_when_limit_reached_and_returns_reset_at()
    {
        var (db, svc) = Build(new PronunciationOptions { FreeTierWeeklyAttemptLimit = 2, FreeTierWindowDays = 7 });
        var now = DateTimeOffset.UtcNow;
        var earliest = now.AddDays(-3);
        db.PronunciationAttempts.AddRange(
            Attempt("u1", earliest),
            Attempt("u1", now.AddDays(-1)));
        await db.SaveChangesAsync();

        var r = await svc.CheckAsync("u1", default);
        Assert.False(r.Allowed);
        Assert.Equal(0, r.Remaining);
        Assert.NotNull(r.ResetAt);
        var diff = (r.ResetAt!.Value - earliest).TotalDays;
        Assert.Equal(7, diff, 1);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task WindowDays_zero_or_negative_falls_back_to_seven()
    {
        var (db, svc) = Build(new PronunciationOptions { FreeTierWindowDays = 0 });
        var r = await svc.CheckAsync("u1", default);
        Assert.Equal(7, r.WindowDays);
        await db.DisposeAsync();

        var (db2, svc2) = Build(new PronunciationOptions { FreeTierWindowDays = -3 });
        var r2 = await svc2.CheckAsync("u1", default);
        Assert.Equal(7, r2.WindowDays);
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task Free_tier_with_no_attempts_returns_full_quota()
    {
        var (db, svc) = Build(new PronunciationOptions { FreeTierWeeklyAttemptLimit = 20 });
        var r = await svc.CheckAsync("u1", default);
        Assert.True(r.Allowed);
        Assert.Equal(20, r.Remaining);
        Assert.Null(r.ResetAt);
        await db.DisposeAsync();
    }
}
