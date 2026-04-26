using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests;

public class AnalyticsIngestionServiceTests
{
    private static (LearnerDbContext db, AnalyticsIngestionService svc, FakeTimeProvider time) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        var svc = new AnalyticsIngestionService(db, time);
        return (db, svc, time);
    }

    [Fact]
    public async Task RecordAsync_persists_event_with_normalized_name_and_serialized_payload()
    {
        var (db, svc, time) = Build();
        var props = new Dictionary<string, object?>
        {
            ["section"] = "writing",
            ["score"] = 350,
        };
        await svc.RecordAsync("u1", new AnalyticsTrackRequest("  practice.completed  ", props), default);

        var saved = await db.AnalyticsEvents.SingleAsync();
        Assert.Equal("u1", saved.UserId);
        Assert.Equal("practice.completed", saved.EventName); // trimmed
        Assert.Equal(time.GetUtcNow(), saved.OccurredAt);
        Assert.Contains("\"section\"", saved.PayloadJson);
        Assert.Contains("writing", saved.PayloadJson);
        Assert.Contains("350", saved.PayloadJson);
        Assert.StartsWith("AN-", saved.Id);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RecordAsync_uses_empty_payload_when_properties_null()
    {
        var (db, svc, _) = Build();
        await svc.RecordAsync("u1", new AnalyticsTrackRequest("login", null), default);
        var saved = await db.AnalyticsEvents.SingleAsync();
        Assert.Equal("{}", saved.PayloadJson);
        await db.DisposeAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task RecordAsync_throws_validation_when_event_name_blank(string? name)
    {
        var (db, svc, _) = Build();
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.RecordAsync("u1", new AnalyticsTrackRequest(name!, null), default));
        Assert.Equal("invalid_analytics_event", ex.ErrorCode);
        Assert.Equal(0, await db.AnalyticsEvents.CountAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RecordAsync_throws_validation_when_event_name_exceeds_max_length()
    {
        var (db, svc, _) = Build();
        var longName = new string('x', 65); // > 64 chars
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.RecordAsync("u1", new AnalyticsTrackRequest(longName, null), default));
        Assert.Equal("invalid_analytics_event", ex.ErrorCode);
        Assert.Equal(0, await db.AnalyticsEvents.CountAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RecordAsync_accepts_event_name_at_exactly_max_length()
    {
        var (db, svc, _) = Build();
        var maxName = new string('y', 64);
        await svc.RecordAsync("u1", new AnalyticsTrackRequest(maxName, null), default);
        var saved = await db.AnalyticsEvents.SingleAsync();
        Assert.Equal(maxName, saved.EventName);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RecordAsync_generates_unique_ids_per_event()
    {
        var (db, svc, _) = Build();
        await svc.RecordAsync("u1", new AnalyticsTrackRequest("a", null), default);
        await svc.RecordAsync("u1", new AnalyticsTrackRequest("b", null), default);
        var ids = await db.AnalyticsEvents.Select(e => e.Id).ToListAsync();
        Assert.Equal(2, ids.Count);
        Assert.NotEqual(ids[0], ids[1]);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RecordAsync_uses_time_provider_for_OccurredAt()
    {
        var (db, svc, time) = Build();
        var t1 = time.GetUtcNow();
        await svc.RecordAsync("u1", new AnalyticsTrackRequest("first", null), default);

        time.Advance(TimeSpan.FromMinutes(5));
        await svc.RecordAsync("u1", new AnalyticsTrackRequest("second", null), default);

        var events = await db.AnalyticsEvents.OrderBy(e => e.EventName).ToListAsync();
        Assert.Equal(t1, events.Single(e => e.EventName == "first").OccurredAt);
        Assert.Equal(t1.AddMinutes(5), events.Single(e => e.EventName == "second").OccurredAt);
        await db.DisposeAsync();
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
