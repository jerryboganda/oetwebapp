using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// The Speaking transcription queue is the SpeakingTranscripts table itself, polled
/// by every app process (blue, green, ai-worker). The old read-then-write let two of
/// them take the same row and call the paid ASR provider twice, and a row orphaned in
/// <c>__processing__</c> by a dead process was never picked up again. These cover the
/// atomic claim and the stale-row requeue on the in-memory provider (logic) and, in
/// <see cref="SpeakingTranscriptionQueuePostgreSqlTests"/>, on real PostgreSQL (the CAS).
/// </summary>
public sealed class SpeakingTranscriptionQueueTests
{
    [Fact]
    public async Task Claim_TakesTheOldestQueuedRowFirst_EachRowOnce_ThenReportsAnEmptyQueue()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        Seed(db, "t-newest", SpeakingTranscriptionPipeline.StateQueued, now.AddMinutes(-1));
        Seed(db, "t-oldest", SpeakingTranscriptionPipeline.StateQueued, now.AddMinutes(-9));
        Seed(db, "t-done", "openai-whisper", now.AddMinutes(-30));
        Seed(db, "t-busy", SpeakingTranscriptionPipeline.StateProcessing, now.AddMinutes(-20));
        await db.SaveChangesAsync();
        var pipeline = Pipeline(db);

        Assert.Equal("t-oldest", await pipeline.ClaimNextQueuedAsync(default));
        Assert.Equal("t-newest", await pipeline.ClaimNextQueuedAsync(default));
        Assert.Null(await pipeline.ClaimNextQueuedAsync(default));

        var rows = await db.SpeakingTranscripts.AsNoTracking().ToDictionaryAsync(t => t.Id);
        Assert.Equal(SpeakingTranscriptionPipeline.StateProcessing, rows["t-oldest"].Provider);
        Assert.Equal(SpeakingTranscriptionPipeline.StateProcessing, rows["t-newest"].Provider);
        Assert.Equal("openai-whisper", rows["t-done"].Provider);
        // The claim starts the lease: GeneratedAt becomes the claim time.
        Assert.True(rows["t-oldest"].GeneratedAt > now.AddSeconds(-30));
    }

    [Fact]
    public async Task Claim_IsVisibleToAnotherContext_Immediately()
    {
        var dbName = $"speaking-claim-{Guid.NewGuid():N}";
        await using var db = NewDb(dbName);
        Seed(db, "t-1", SpeakingTranscriptionPipeline.StateQueued, DateTimeOffset.UtcNow.AddMinutes(-1));
        await db.SaveChangesAsync();

        Assert.Equal("t-1", await Pipeline(db).ClaimNextQueuedAsync(default));

        await using var other = NewDb(dbName);
        Assert.Equal(
            SpeakingTranscriptionPipeline.StateProcessing,
            (await other.SpeakingTranscripts.AsNoTracking().SingleAsync(t => t.Id == "t-1")).Provider);
    }

    [Fact]
    public async Task ProcessNext_WithAnEmptyQueue_ReturnsFalse_AndNeverCallsTheProvider()
    {
        await using var db = NewDb();
        Seed(db, "t-done", "openai-whisper", DateTimeOffset.UtcNow.AddMinutes(-30));
        await db.SaveChangesAsync();

        // provider is null!: any ASR call would throw NullReferenceException.
        Assert.False(await Pipeline(db).ProcessNextAsync(default));
    }

    [Fact]
    public async Task RequeueStale_PutsOnlyRowsPastTheLeaseBackInTheQueue_KeepingTheirPlaceInLine()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var stale = now - SpeakingTranscriptionPipeline.ProcessingLease - TimeSpan.FromMinutes(1);
        Seed(db, "t-dead", SpeakingTranscriptionPipeline.StateProcessing, stale);
        Seed(db, "t-live", SpeakingTranscriptionPipeline.StateProcessing, now.AddMinutes(-2));
        Seed(db, "t-queued", SpeakingTranscriptionPipeline.StateQueued, now.AddMinutes(-1));
        Seed(db, "t-failed", SpeakingTranscriptionPipeline.StateFailed, stale);
        await db.SaveChangesAsync();
        var pipeline = Pipeline(db);

        Assert.Equal(1, await pipeline.RequeueStaleProcessingAsync(now, default));

        var rows = await db.SpeakingTranscripts.AsNoTracking().ToDictionaryAsync(t => t.Id);
        Assert.Equal(SpeakingTranscriptionPipeline.StateQueued, rows["t-dead"].Provider);
        Assert.Equal(SpeakingTranscriptionPipeline.StateProcessing, rows["t-live"].Provider);
        Assert.Equal(SpeakingTranscriptionPipeline.StateFailed, rows["t-failed"].Provider);

        // The re-queued row keeps its old timestamp, so it is claimed before the newer queued one.
        Assert.Equal("t-dead", await pipeline.ClaimNextQueuedAsync(default));

        // Nothing left to recover: a second sweep is a no-op.
        Assert.Equal(0, await pipeline.RequeueStaleProcessingAsync(now, default));
    }

    [Fact]
    public void TheLeaseOutlastsAnAsrCall_AndTheInlineGradeCeilingStaysUnderTheOperationLease()
    {
        // The Whisper HTTP client times out at 100 s; a lease shorter than a call would
        // re-queue live work and double-bill it.
        Assert.True(SpeakingTranscriptionPipeline.ProcessingLease > TimeSpan.FromMinutes(5));
        // The ceiling must fire (and hand the grade back) before the 30-minute operation
        // lease could expire under a still-running inline grade.
        Assert.True(SpeakingTranscriptionPipeline.InlineAssessCeiling < OetLearner.Api.Services.Ai.AiOperationWorker.LeaseDuration);
        // A healthy Max-reasoning grade takes ~12 minutes; the ceiling only bounds a hang.
        Assert.True(SpeakingTranscriptionPipeline.InlineAssessCeiling > TimeSpan.FromMinutes(12));
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static LearnerDbContext NewDb(string? name = null)
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(name ?? $"speaking-queue-{Guid.NewGuid():N}")
            .Options);

    private static SpeakingTranscriptionPipeline Pipeline(LearnerDbContext db)
        => new(db, provider: null!, NullLogger<SpeakingTranscriptionPipeline>.Instance);

    private static void Seed(LearnerDbContext db, string id, string provider, DateTimeOffset generatedAt)
        => db.SpeakingTranscripts.Add(new SpeakingTranscript
        {
            Id = id,
            SpeakingSessionId = $"session-{id}",
            Provider = provider,
            Language = "en",
            SegmentsJson = "[]",
            IsLatest = false,
            GeneratedAt = generatedAt,
        });
}

/// <summary>
/// Real-PostgreSQL twin (skips without <c>OET_TEST_POSTGRES_CONNECTION</c>): the claim is a
/// compare-and-swap UPDATE, so two processes racing the same queue must never both win a row,
/// and the relational stale-requeue UPDATE must behave like the in-memory one.
/// </summary>
[Collection(PostgreSqlExclusiveCollection.Name)]
public sealed class SpeakingTranscriptionQueuePostgreSqlTests
{
    private const string TranscriptsDdl = """
        CREATE TABLE "SpeakingTranscripts" (
            "Id" varchar(64) PRIMARY KEY,
            "SpeakingSessionId" varchar(64) NOT NULL,
            "Provider" varchar(32) NOT NULL,
            "Language" varchar(8) NOT NULL DEFAULT 'en',
            "SegmentsJson" text NOT NULL DEFAULT '[]',
            "IsLatest" boolean NOT NULL DEFAULT false,
            "WordCount" integer NOT NULL DEFAULT 0,
            "MeanConfidence" double precision NOT NULL DEFAULT 0,
            "GeneratedAt" timestamptz NOT NULL
        );
        """;

    [PostgreSqlFact]
    public async Task TwoProcesses_RacingTheQueue_NeverClaimTheSameRow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(TranscriptsDdl);
        for (var index = 0; index < 8; index++)
        {
            await InsertAsync(database, $"t-{index}", "__queued__", DateTimeOffset.UtcNow.AddMinutes(-20 + index));
        }

        await using var db1 = CreateContext(database);
        await using var db2 = CreateContext(database);
        var first = new SpeakingTranscriptionPipeline(db1, provider: null!, NullLogger<SpeakingTranscriptionPipeline>.Instance);
        var second = new SpeakingTranscriptionPipeline(db2, provider: null!, NullLogger<SpeakingTranscriptionPipeline>.Instance);

        var results = await Task.WhenAll(
            Task.Run(() => DrainAsync(first)),
            Task.Run(() => DrainAsync(second)));

        var all = results.SelectMany(ids => ids).ToList();
        Assert.Equal(8, all.Count);
        Assert.Equal(8, all.Distinct().Count());
        await using var verify = CreateContext(database);
        Assert.Equal(8, await verify.SpeakingTranscripts.CountAsync(t => t.Provider == "__processing__"));
        Assert.Equal(0, await verify.SpeakingTranscripts.CountAsync(t => t.Provider == "__queued__"));
    }

    [PostgreSqlFact]
    public async Task StaleProcessingRows_AreRequeuedByTheRelationalUpdate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(TranscriptsDdl);
        var now = DateTimeOffset.UtcNow;
        await InsertAsync(database, "t-dead", "__processing__", now.AddHours(-1));
        await InsertAsync(database, "t-live", "__processing__", now.AddMinutes(-1));
        await InsertAsync(database, "t-done", "openai-whisper", now.AddHours(-2));

        await using var db = CreateContext(database);
        var pipeline = new SpeakingTranscriptionPipeline(db, provider: null!, NullLogger<SpeakingTranscriptionPipeline>.Instance);

        Assert.Equal(1, await pipeline.RequeueStaleProcessingAsync(now, CancellationToken.None));

        await using var verify = CreateContext(database);
        var rows = await verify.SpeakingTranscripts.AsNoTracking().ToDictionaryAsync(t => t.Id);
        Assert.Equal("__queued__", rows["t-dead"].Provider);
        Assert.Equal("__processing__", rows["t-live"].Provider);
        Assert.Equal("openai-whisper", rows["t-done"].Provider);
    }

    private static async Task<List<string>> DrainAsync(SpeakingTranscriptionPipeline pipeline)
    {
        var claimed = new List<string>();
        while (await pipeline.ClaimNextQueuedAsync(CancellationToken.None) is { } id)
        {
            claimed.Add(id);
        }

        return claimed;
    }

    private static LearnerDbContext CreateContext(PostgreSqlTestDatabase database)
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseNpgsql(database.SchemaConnectionString, npgsql => npgsql.UseVector())
            .Options);

    private static async Task InsertAsync(PostgreSqlTestDatabase database, string id, string provider, DateTimeOffset generatedAt)
        => await database.ExecuteAsync($"""
            INSERT INTO "SpeakingTranscripts" ("Id", "SpeakingSessionId", "Provider", "GeneratedAt")
            VALUES ('{id}', 'session-{id}', '{provider}', TIMESTAMPTZ '{generatedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss.ffffff}+00');
            """);
}
