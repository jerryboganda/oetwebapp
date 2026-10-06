using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

    // ── inline grade after a transcript lands ───────────────────────────
    //
    // The grade that follows a landed transcript runs under a ceiling (InlineAssessCeiling). Only
    // THAT ceiling firing means "handed back to the AI worker queue"; a cancellation it did not
    // cause (an HttpClient timeout surfaces as TaskCanceledException) is a failed auto-assessment,
    // and only a real shutdown propagates. (The ceiling itself is 20 minutes, so its branch is
    // pinned by the filter reading the ceiling's own token, not by waiting it out here.)

    [Fact]
    public async Task InlineGrade_CancelledByAnHttpTimeout_IsLoggedAsAFailure_NotAsTheCeilingHandBack()
    {
        await using var db = NewDb();
        await SeedLandableTranscriptAsync(db);
        var logger = new CapturingLogger();
        var pipeline = new SpeakingTranscriptionPipeline(
            db,
            new FakeAsrProvider(),
            logger,
            new StubCanonicalAssessment(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.")));

        Assert.True(await pipeline.ProcessNextAsync(default));

        // The transcript itself landed before the grade started.
        var row = await db.SpeakingTranscripts.AsNoTracking().SingleAsync(t => t.Id == "t-1");
        Assert.Equal(FakeAsrProvider.Code, row.Provider);
        Assert.True(row.IsLatest);
        Assert.Contains(logger.Messages, m => m.Contains("Auto-assessment after transcription failed", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("handed back to the AI worker queue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InlineGrade_ThatFailsOutright_IsLoggedAsAFailure_AndTheQueueMovesOn()
    {
        await using var db = NewDb();
        await SeedLandableTranscriptAsync(db);
        var logger = new CapturingLogger();
        var pipeline = new SpeakingTranscriptionPipeline(
            db,
            new FakeAsrProvider(),
            logger,
            new StubCanonicalAssessment(_ => throw new InvalidOperationException("grader exploded")));

        Assert.True(await pipeline.ProcessNextAsync(default));

        Assert.Contains(logger.Messages, m => m.Contains("Auto-assessment after transcription failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InlineGrade_InterruptedByAHostShutdown_StillPropagates()
    {
        await using var db = NewDb();
        await SeedLandableTranscriptAsync(db);
        using var shutdown = new CancellationTokenSource();
        var pipeline = new SpeakingTranscriptionPipeline(
            db,
            new FakeAsrProvider(),
            new CapturingLogger(),
            new StubCanonicalAssessment(token =>
            {
                shutdown.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.ProcessNextAsync(shutdown.Token));
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static LearnerDbContext NewDb(string? name = null)
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(name ?? $"speaking-queue-{Guid.NewGuid():N}")
            .Options);

    private static SpeakingTranscriptionPipeline Pipeline(LearnerDbContext db)
        => new(db, provider: null!, NullLogger<SpeakingTranscriptionPipeline>.Instance);

    /// <summary>A queued transcript whose session is submitted (so the inline grade is requested)
    /// and whose recording + media asset resolve, so <c>ProcessNextAsync</c> reaches the grade.</summary>
    private static async Task SeedLandableTranscriptAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        Seed(db, "t-1", SpeakingTranscriptionPipeline.StateQueued, now.AddMinutes(-1));
        const string sessionId = "session-t-1";
        db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = sessionId,
            UserId = "learner-1",
            RolePlayCardId = "card-1",
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.MediaAssets.Add(new MediaAsset
        {
            Id = "media-1",
            OriginalFilename = "role-play.webm",
            MimeType = "audio/webm",
            Format = "webm",
            StoragePath = "speaking/role-play.webm",
            UploadedAt = now,
        });
        db.SpeakingRecordings.Add(new SpeakingRecording
        {
            Id = SpeakingSessionRecordingService.RecordingIdFor(sessionId),
            SpeakingSessionId = sessionId,
            MediaAssetId = "media-1",
            Sha256 = "sha-1",
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private sealed class FakeAsrProvider : ISpeakingTranscriptionProvider
    {
        public const string Code = "fake-asr";

        public string ProviderCode => Code;

        public Task<SpeakingTranscriptionProviderResult> TranscribeAsync(string mediaAssetUrl, string language, CancellationToken ct)
            => Task.FromResult(new SpeakingTranscriptionProviderResult
            {
                Provider = Code,
                Language = "en",
                SegmentsJson = """[{"speaker":"learner","startMs":0,"endMs":900,"text":"Good morning","confidence":0.9}]""",
                WordCount = 2,
                MeanConfidence = 0.9,
            });
    }

    /// <summary>Only <c>AssessNowAsync</c> is reachable from the transcription pipeline.</summary>
    private sealed class StubCanonicalAssessment(Func<CancellationToken, Task> assessNow) : ISpeakingCanonicalAssessmentService
    {
        public Task AssessNowAsync(string sessionId, CancellationToken ct) => assessNow(ct);

        public string ComputeIdentityHash(string sessionId, string cardId, string transcriptHash, string rubricVersion, string promptVersion)
            => throw new NotSupportedException();

        public Task<SpeakingFinalizationTicket> EnqueueAsync(string sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task ExecuteQueuedAsync(string operationId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SpeakingFinalizationTicket> EnqueueExamCombinedAsync(string examId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task RetryExamCombinedAsync(string examId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<string> GetExamCombinedStateAsync(string examId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> UsesV11Async(string sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SpeakingAssessmentState> GetStateAsync(string sessionId, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class CapturingLogger : ILogger<SpeakingTranscriptionPipeline>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

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
