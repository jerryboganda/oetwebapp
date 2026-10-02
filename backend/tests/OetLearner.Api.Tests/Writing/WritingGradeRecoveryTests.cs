using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// WAI-03 auto-resume: the batch cron's due-row filter, the stale-claim reclaim and the shutdown
/// requeue — on SQLite (set-based updates) and on the InMemory test provider (load-and-save branch).
/// </summary>
public sealed class WritingGradeRecoveryTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private SqliteConnection? _connection;

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
    }

    private LearnerDbContext NewDb(bool inMemory)
    {
        if (inMemory)
        {
            return new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        }

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options);
        db.Database.EnsureCreated();
        return db;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sweep_PicksOnlyDueRows_OldestFirst_AndAtMostTheBatch(bool inMemory)
    {
        await using var db = NewDb(inMemory);
        var dueBackoff = Seed(db, "queued", submittedAgo: 40, nextRetryIn: -1);
        var notYetDue = Seed(db, "queued", submittedAgo: 50, nextRetryIn: 5);
        var freshExpress = Seed(db, "queued", submittedAgo: 1);
        var staleExpress = Seed(db, "queued", submittedAgo: 3);
        var batched = Seed(db, "queued", submittedAgo: 0, tier: "batched");
        Seed(db, "grading", submittedAgo: 60);
        Seed(db, "failed", submittedAgo: 60);
        await db.SaveChangesAsync();

        var due = await WritingGradeRecovery.DueQueuedIdsAsync(db, Now, take: 5, default);

        Assert.Equal(new[] { dueBackoff, staleExpress, batched }, due);
        Assert.DoesNotContain(notYetDue, due);
        Assert.DoesNotContain(freshExpress, due);
        Assert.Single(await WritingGradeRecovery.DueQueuedIdsAsync(db, Now, take: 1, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleReclaim_RequeuesOnlyClaimsPastTheLease(bool inMemory)
    {
        await using var db = NewDb(inMemory);
        var dead = Seed(db, "grading", submittedAgo: 60, claimedAgo: (int)WritingGradeTimings.StaleClaimLease.TotalMinutes + 1, owner: "dead-host:1:a");
        var live = Seed(db, "grading", submittedAgo: 10, claimedAgo: 5, owner: "live-host:1:b");
        await db.SaveChangesAsync();

        var reclaimed = await WritingGradeRecovery.ReclaimStaleGradingAsync(db, Now, default);

        Assert.Equal(1, reclaimed);
        var deadRow = await Fresh(db, dead);
        Assert.Equal("queued", deadRow.Status);
        Assert.Null(deadRow.ClaimOwner);
        Assert.Equal(Now, deadRow.NextAutoRetryAt);
        var liveRow = await Fresh(db, live);
        Assert.Equal("grading", liveRow.Status);
        Assert.Equal("live-host:1:b", liveRow.ClaimOwner);
        Assert.Contains(dead, await WritingGradeRecovery.DueQueuedIdsAsync(db, Now, 5, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownRequeue_TouchesOnlyThisProcessesClaims(bool inMemory)
    {
        await using var db = NewDb(inMemory);
        var mine = Seed(db, "grading", submittedAgo: 5, claimedAgo: 1, owner: WritingGradeRecovery.ProcessOwnerPrefix + "run1");
        var otherSlot = Seed(db, "grading", submittedAgo: 5, claimedAgo: 1, owner: "other-host:7:run2");
        var finished = Seed(db, "graded", submittedAgo: 5, claimedAgo: 1, owner: WritingGradeRecovery.ProcessOwnerPrefix + "run3");
        await db.SaveChangesAsync();

        var requeued = await WritingGradeRecovery.RequeueOwnClaimsAsync(db, WritingGradeRecovery.ProcessOwnerPrefix, Now, default);

        Assert.Equal(1, requeued);
        Assert.Equal("queued", (await Fresh(db, mine)).Status);
        Assert.Equal("grading", (await Fresh(db, otherSlot)).Status);
        Assert.Equal("graded", (await Fresh(db, finished)).Status);
    }

    private static Guid Seed(
        LearnerDbContext db,
        string status,
        int submittedAgo,
        int? nextRetryIn = null,
        int? claimedAgo = null,
        string? owner = null,
        string tier = "express")
    {
        var id = Guid.NewGuid();
        db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = id,
            UserId = "learner-1",
            ScenarioId = Guid.NewGuid(),
            LetterContent = "letter",
            LetterContentHash = id.ToString("N"),
            Status = status,
            GradingTier = tier,
            InputSource = "typed",
            StartedAt = Now.AddMinutes(-submittedAgo),
            SubmittedAt = Now.AddMinutes(-submittedAgo),
            CreatedAt = Now.AddMinutes(-submittedAgo),
            NextAutoRetryAt = nextRetryIn is { } inMinutes ? Now.AddMinutes(inMinutes) : null,
            ClaimedAt = claimedAgo is { } ago ? Now.AddMinutes(-ago) : null,
            ClaimOwner = owner,
        });
        return id;
    }

    private static async Task<WritingSubmission> Fresh(LearnerDbContext db, Guid id)
    {
        db.ChangeTracker.Clear();
        return await db.WritingSubmissions.AsNoTracking().SingleAsync(s => s.Id == id);
    }
}
