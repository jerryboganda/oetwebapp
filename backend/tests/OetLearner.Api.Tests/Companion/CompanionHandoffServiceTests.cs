using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Companion;
using Xunit;

namespace OetLearner.Api.Tests.Companion;

/// <summary>F-107/F-108/F-123: handoffs are built from real learner evidence,
/// namespaced to the learner, and give tutors a lifecycle.</summary>
public sealed class CompanionHandoffServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionHandoffServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private CompanionHandoffService NewService()
    {
        var db = new LearnerDbContext(_options);
        var memory = new CompanionMemoryService(db, new FixedClock());
        var errors = new ErrorDnaService(db, new FixedClock());
        return new CompanionHandoffService(db, memory, errors, new FixedClock());
    }

    [Fact]
    public async Task CreateAsync_BuildsSummaryFromRecordedEvidence()
    {
        var db = new LearnerDbContext(_options);
        var memory = new CompanionMemoryService(db, new FixedClock());
        var errors = new ErrorDnaService(db, new FixedClock());
        await memory.RecordAsync("u1", CompanionMemoryLayers.Learning, "score", "writing",
            "writing score 340 on 2026-09-30", null, "chat", "t1",
            DateTimeOffset.UtcNow, CancellationToken.None);
        await errors.RecordEvidenceAsync("u1", "grammar", "article before singular countable noun",
            "writing", "writing_grade", "r1", CancellationToken.None);

        var svc = new CompanionHandoffService(db, memory, errors, new FixedClock());
        var handoff = await svc.CreateAsync("u1", "thread-1", "tutor",
            "I keep failing writing despite practicing", CancellationToken.None);

        Assert.Equal("open", handoff.Status);
        Assert.Equal("tutor", handoff.Route);
        Assert.Contains("writing score 340", handoff.Summary);
        Assert.Contains("article before singular countable noun", handoff.Summary);
        Assert.Contains("thread-1", handoff.Summary);
    }

    [Fact]
    public async Task CreateAsync_NormalisesRoute()
    {
        var svc = NewService();
        var handoff = await svc.CreateAsync("u1", "t1", "SUPPORT", "cannot log in on my phone", CancellationToken.None);
        Assert.Equal("support", handoff.Route);
    }

    [Fact]
    public async Task Queue_OpenHandoffsOldestFirst_StatusLifecycle()
    {
        var svc = NewService();
        var first = await svc.CreateAsync("u1", "t1", "tutor", "issue one", CancellationToken.None);
        await svc.CreateAsync("u2", "t2", "tutor", "issue two", CancellationToken.None);

        var queue = await svc.ListOpenAsync("tutor", 10, CancellationToken.None);
        Assert.Equal(2, queue.Count);
        Assert.Equal(first.Id, queue[0].Id);

        var claimed = await svc.SetStatusAsync(first.Id, "claimed", "tutor-7", CancellationToken.None);
        Assert.Equal("claimed", claimed?.Status);
        Assert.Equal("tutor-7", claimed?.HandledBy);

        var resolved = await svc.SetStatusAsync(first.Id, "resolved", "tutor-7", CancellationToken.None);
        Assert.NotNull(resolved?.HandledAt);

        var remaining = await svc.ListOpenAsync("tutor", 10, CancellationToken.None);
        Assert.Single(remaining);
    }

    [Fact]
    public async Task Lists_AreNamespacedToTheLearner()
    {
        var svc = NewService();
        await svc.CreateAsync("u1", "t1", "tutor", "mine", CancellationToken.None);

        var other = await svc.ListForUserAsync("someone-else", 10, CancellationToken.None);
        Assert.Empty(other);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2027, 1, 13, 9, 0, 0, TimeSpan.Zero);
    }
}
