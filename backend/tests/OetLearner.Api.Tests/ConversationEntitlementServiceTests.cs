using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Conversation;
using Xunit;

namespace OetLearner.Api.Tests;

public class ConversationEntitlementServiceTests
{
    private sealed class FakeOptions(ConversationOptions opts) : IConversationOptionsProvider
    {
        public Task<ConversationOptions> GetAsync(CancellationToken ct = default) => Task.FromResult(opts);
        public ConversationOptions Snapshot() => opts;
        public void Invalidate() { }
    }

    private static (LearnerDbContext db, ConversationEntitlementService svc) Build(
        ConversationOptions? opts = null)
    {
        var dbOpts = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(dbOpts);
        var svc = new ConversationEntitlementService(db, new FakeOptions(opts ?? new ConversationOptions()));
        return (db, svc);
    }

    private static ConversationSession Session(string userId, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        UserId = userId,
        ExamTypeCode = "OET",
        TaskTypeCode = "oet-roleplay",
        CreatedAt = createdAt,
    };

    private static Subscription Sub(string userId, SubscriptionStatus status) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        UserId = userId,
        PlanId = "pro",
        Status = status,
        StartedAt = DateTimeOffset.UtcNow.AddDays(-30),
        ChangedAt = DateTimeOffset.UtcNow.AddDays(-30),
        NextRenewalAt = DateTimeOffset.UtcNow.AddDays(30),
        PriceAmount = 19.99m,
    };

    [Fact]
    public async Task Disabled_returns_not_allowed_with_disabled_tier()
    {
        var (db, svc) = Build(new ConversationOptions { Enabled = false });
        var r = await svc.CheckAsync("u1", default);
        Assert.False(r.Allowed);
        Assert.Equal("disabled", r.Tier);
        Assert.Contains("disabled", r.Reason, StringComparison.OrdinalIgnoreCase);
        await db.DisposeAsync();
    }

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
        Assert.Equal(int.MaxValue, r.Remaining);
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
        var (db, svc) = Build(new ConversationOptions { FreeTierSessionsLimit = 3, FreeTierWindowDays = 7 });
        db.Subscriptions.Add(Sub("u1", status));
        await db.SaveChangesAsync();

        var r = await svc.CheckAsync("u1", default);
        Assert.True(r.Allowed);
        Assert.Equal("free", r.Tier);
        Assert.Equal(3, r.Remaining);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Free_tier_with_negative_limit_is_unlimited()
    {
        var (db, svc) = Build(new ConversationOptions { FreeTierSessionsLimit = -1 });
        var r = await svc.CheckAsync("u1", default);
        Assert.True(r.Allowed);
        Assert.Equal("free", r.Tier);
        Assert.Equal(int.MaxValue, r.Remaining);
        Assert.Contains("unlimited", r.Reason, StringComparison.OrdinalIgnoreCase);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Free_tier_counts_only_sessions_inside_window()
    {
        var (db, svc) = Build(new ConversationOptions { FreeTierSessionsLimit = 3, FreeTierWindowDays = 7 });
        var now = DateTimeOffset.UtcNow;
        db.ConversationSessions.AddRange(
            Session("u1", now.AddDays(-2)),
            Session("u1", now.AddDays(-30)),     // outside window — ignored
            Session("u2", now.AddHours(-1)));    // other user — ignored
        await db.SaveChangesAsync();

        var r = await svc.CheckAsync("u1", default);
        Assert.True(r.Allowed);
        Assert.Equal(2, r.Remaining);
        Assert.Equal(3, r.LimitPerWindow);
        Assert.NotNull(r.ResetAt);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Free_tier_blocks_when_limit_reached_and_returns_reset_at()
    {
        var (db, svc) = Build(new ConversationOptions { FreeTierSessionsLimit = 2, FreeTierWindowDays = 7 });
        var now = DateTimeOffset.UtcNow;
        var earliest = now.AddDays(-3);
        db.ConversationSessions.AddRange(
            Session("u1", earliest),
            Session("u1", now.AddDays(-1)));
        await db.SaveChangesAsync();

        var r = await svc.CheckAsync("u1", default);
        Assert.False(r.Allowed);
        Assert.Equal(0, r.Remaining);
        Assert.Equal(2, r.LimitPerWindow);
        Assert.NotNull(r.ResetAt);
        // ResetAt = earliest + windowDays
        var diff = (r.ResetAt!.Value - earliest).TotalDays;
        Assert.Equal(7, diff, 1);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task WindowDays_zero_or_negative_falls_back_to_seven()
    {
        var (db, svc) = Build(new ConversationOptions { FreeTierSessionsLimit = 3, FreeTierWindowDays = 0 });
        var r = await svc.CheckAsync("u1", default);
        Assert.Equal(7, r.WindowDays);
        await db.DisposeAsync();

        var (db2, svc2) = Build(new ConversationOptions { FreeTierSessionsLimit = 3, FreeTierWindowDays = -5 });
        var r2 = await svc2.CheckAsync("u1", default);
        Assert.Equal(7, r2.WindowDays);
        await db2.DisposeAsync();
    }
}
