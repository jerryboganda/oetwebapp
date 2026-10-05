using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Platform;

/// <summary>
/// BackgroundJobProcessor hardening: the freeze sweep is due-only and throttled, a job
/// that starts late in a long batch is ownership-checked and keeps its stamp fresh (and is
/// skipped, never run twice, when another process's stuck-job sweep already took it back), an unhandled job type fails once (no silent "Completed", no retry
/// storm, no admin alert), AchievementCheck stays an explicit no-op, and the Speaking
/// transcription queue polls on its own loop so a long inline grade cannot stall the pass.
/// </summary>
public sealed class BackgroundJobProcessorHardeningTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BackgroundJobProcessorHardeningTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── freeze sweep ────────────────────────────────────────────────────

    [Fact]
    public void DueFreezeQuery_PutsBothDueConditionsInThePostgresWhereClause()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=translation_only;Username=none;Password=none",
                npgsql => npgsql.UseVector())
            .Options;
        using var db = new LearnerDbContext(options);

        var sql = BackgroundJobProcessor.DueFreezeRecords(db, DateTimeOffset.UtcNow).ToQueryString();

        Assert.Matches("\"ScheduledStartAt\"\\s*<=", sql);
        Assert.Matches("\"EndedAt\"\\s*<=", sql);
    }

    [Fact]
    public async Task FreezeSweep_ActivatesOnlyDueRecords_AndRunsAtMostOncePerInterval()
    {
        var processor = NewProcessor();
        var now = DateTimeOffset.UtcNow;
        var dueId = $"freeze-due-{Guid.NewGuid():N}";
        var futureId = $"freeze-future-{Guid.NewGuid():N}";
        var laterDueId = $"freeze-later-{Guid.NewGuid():N}";
        await SeedScheduledFreezeAsync(dueId, now.AddMinutes(-5));
        await SeedScheduledFreezeAsync(futureId, now.AddDays(3));

        Assert.True(await SweepAsync(processor, now));
        Assert.Equal(FreezeStatus.Active, (await LoadFreezeAsync(dueId)).Status);
        Assert.Equal(FreezeStatus.Scheduled, (await LoadFreezeAsync(futureId)).Status);

        // Inside the interval the sweep does nothing, even for a record that is already due.
        await SeedScheduledFreezeAsync(laterDueId, now.AddMinutes(-1));
        Assert.False(await SweepAsync(processor, now.AddSeconds(10)));
        Assert.Equal(FreezeStatus.Scheduled, (await LoadFreezeAsync(laterDueId)).Status);

        Assert.True(await SweepAsync(processor, now.AddSeconds(61)));
        Assert.Equal(FreezeStatus.Active, (await LoadFreezeAsync(laterDueId)).Status);
        Assert.Equal(FreezeStatus.Scheduled, (await LoadFreezeAsync(futureId)).Status);
    }

    // ── claimed-batch ownership (tail jobs) ─────────────────────────────
    //
    // A pass claims a batch up front and runs it serially, so a tail job can start long after
    // its claim stamp, while another process's RecoverStuckJobsAsync re-queues any Processing row
    // older than 30 minutes. These drive the real sweep against rows seeded with aged claim
    // stamps (no clock to fake: the age is in the seeded data).

    [Fact]
    public async Task AFastBatch_PaysNoExtraWrites_AndItsRowsAreLeftUntouched()
    {
        var dbName = $"job-fast-{Guid.NewGuid():N}";
        var claimedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await SeedProcessingJobsAsync(dbName, claimedAt, "job-a", "job-b");

        await using var db = NewDb(dbName);
        var batch = await LoadBatchAsync(db);
        Assert.True(await NewProcessorWithoutScopes().TryBeginClaimedJobAsync(db, batch, 0, CancellationToken.None));

        var persisted = await ReadJobsAsync(dbName);
        Assert.Equal(claimedAt, persisted["job-a"].LastTransitionAt);
        Assert.Equal(claimedAt, persisted["job-b"].LastTransitionAt);
    }

    [Fact]
    public async Task AnAgedClaim_RefreshesTheStampOfTheJobAndOfTheWholeUnstartedTail()
    {
        var dbName = $"job-aged-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        await SeedProcessingJobsAsync(dbName, now.AddMinutes(-12), "job-a", "job-b", "job-c");

        await using var db = NewDb(dbName);
        var batch = await LoadBatchAsync(db);
        Assert.True(await NewProcessorWithoutScopes().TryBeginClaimedJobAsync(db, batch, 0, CancellationToken.None));

        var persisted = await ReadJobsAsync(dbName);
        foreach (var id in new[] { "job-a", "job-b", "job-c" })
        {
            Assert.Equal(AsyncState.Processing, persisted[id].State);
            Assert.Equal(0, persisted[id].RetryCount);
            Assert.True(persisted[id].LastTransitionAt > now.AddSeconds(-30), $"{id} keeps a fresh claim stamp");
        }
    }

    [Fact]
    public async Task ATailJobThatWaitedPastTheSweepThreshold_IsKeptAliveByTheRefresh_NotRequeued()
    {
        var dbName = $"job-tail-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var stuckThreshold = (TimeSpan)Field("StuckJobStaleThreshold").GetValue(null)!;
        // Claimed 31 minutes ago: older than the sweep's threshold, but not yet swept.
        await SeedProcessingJobsAsync(dbName, now - stuckThreshold - TimeSpan.FromMinutes(1), "job-a", "job-b", "job-c");

        await using var dbA = NewDb(dbName);
        var batch = await LoadBatchAsync(dbA);
        Assert.True(await NewProcessorWithoutScopes().TryBeginClaimedJobAsync(dbA, batch, 0, CancellationToken.None));

        await RunStuckJobSweepAsync(dbName);

        var persisted = await ReadJobsAsync(dbName);
        foreach (var id in new[] { "job-a", "job-b", "job-c" })
        {
            Assert.Equal(AsyncState.Processing, persisted[id].State);
            Assert.Equal(0, persisted[id].RetryCount);
        }
    }

    [Fact]
    public async Task AClaimedJobTheSweepAlreadyRequeued_IsSkippedAndNeverWrittenBack()
    {
        var dbName = $"job-swept-{Guid.NewGuid():N}";
        var stuckThreshold = (TimeSpan)Field("StuckJobStaleThreshold").GetValue(null)!;
        await SeedProcessingJobsAsync(dbName, DateTimeOffset.UtcNow - stuckThreshold - TimeSpan.FromMinutes(1), "job-a", "job-b", "job-c");

        // Process A claimed the batch (holds it in memory); process B's sweep then takes it back.
        await using var dbA = NewDb(dbName);
        var batch = await LoadBatchAsync(dbA);
        await RunStuckJobSweepAsync(dbName);
        var afterSweep = await ReadJobsAsync(dbName);
        foreach (var id in new[] { "job-a", "job-b", "job-c" })
        {
            Assert.Equal(AsyncState.Queued, afterSweep[id].State);
            Assert.Equal(1, afterSweep[id].RetryCount);
        }

        var processor = NewProcessorWithoutScopes();
        for (var index = 0; index < batch.Count; index++)
        {
            Assert.False(await processor.TryBeginClaimedJobAsync(dbA, batch, index, CancellationToken.None));
        }

        // The stale in-memory copies must not be saved over the live rows.
        await dbA.SaveChangesAsync();
        var afterSkip = await ReadJobsAsync(dbName);
        foreach (var id in new[] { "job-a", "job-b", "job-c" })
        {
            Assert.Equal(AsyncState.Queued, afterSkip[id].State);
            Assert.Equal(1, afterSkip[id].RetryCount);
            Assert.Equal(afterSweep[id].LastTransitionAt, afterSkip[id].LastTransitionAt);
            Assert.Equal("stale_processing_requeued", afterSkip[id].StatusReasonCode);
        }
    }

    [Fact]
    public async Task ARowAnotherProcessReclaimedAfterTheSweep_IsNotOurs_EvenThoughItIsProcessingAgain()
    {
        var dbName = $"job-reclaimed-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        await SeedProcessingJobsAsync(dbName, now.AddMinutes(-12), "job-a", "job-b");

        await using var dbA = NewDb(dbName);
        var batch = await LoadBatchAsync(dbA);
        // Swept and re-claimed elsewhere: Processing again with a fresh stamp, one retry later.
        var reclaimedAt = now.AddSeconds(-5);
        await using (var other = NewDb(dbName))
        {
            var row = await other.BackgroundJobs.SingleAsync(j => j.Id == "job-a");
            row.RetryCount = 1;
            row.LastTransitionAt = reclaimedAt;
            await other.SaveChangesAsync();
        }

        var processor = NewProcessorWithoutScopes();
        Assert.False(await processor.TryBeginClaimedJobAsync(dbA, batch, 0, CancellationToken.None));
        // Only the row that is still ours (job-b) runs, and only it is refreshed.
        Assert.True(await processor.TryBeginClaimedJobAsync(dbA, batch, 1, CancellationToken.None));

        await dbA.SaveChangesAsync();
        var persisted = await ReadJobsAsync(dbName);
        Assert.Equal(1, persisted["job-a"].RetryCount);
        Assert.Equal(reclaimedAt, persisted["job-a"].LastTransitionAt);
        Assert.Equal(0, persisted["job-b"].RetryCount);
        Assert.True(persisted["job-b"].LastTransitionAt > now.AddSeconds(-30));
    }

    [Fact]
    public void TheStartRefreshWindowPlusTheExecutionCeiling_StaysUnderTheSweepThreshold()
    {
        var maxExecution = (TimeSpan)Field("MaxJobExecutionTime").GetValue(null)!;
        var stuckThreshold = (TimeSpan)Field("StuckJobStaleThreshold").GetValue(null)!;

        // The stamp can be up to JobStartStampAfter old when a job begins unrefreshed, and the
        // job may then run for MaxJobExecutionTime: that total must stay under the sweep's
        // threshold. (The ownership check, not this arithmetic, is what protects a tail job
        // behind a handler that ignores cancellation; this only pins the common-case budget.)
        Assert.True(BackgroundJobProcessor.JobStartStampAfter + maxExecution < stuckThreshold);
    }

    [Fact]
    public void TheJobPass_RunsTheOwnershipGate_BeforeExecutingAJob()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "backend", "src", "OetLearner.Api", "Services", "BackgroundJobProcessor.cs"));

        var gate = source.IndexOf("await TryBeginClaimedJobAsync(db, jobs, index, cancellationToken)", StringComparison.Ordinal);
        var execute = source.IndexOf("await ExecuteJobAsync(scope.ServiceProvider, db, job, jobCts.Token)", StringComparison.Ordinal);
        Assert.True(gate >= 0, "every claimed job must pass the ownership gate");
        Assert.True(execute > gate, "the gate must run before the job executes");
    }

    // ── job types ───────────────────────────────────────────────────────

    [Fact]
    public async Task AchievementCheck_IsAnExplicitNoOp_AndAnUnhandledTypeFailsOnceWithoutRetries()
    {
        var achievementId = $"job-achv-{Guid.NewGuid():N}";
        var unhandledId = $"job-unhandled-{Guid.NewGuid():N}";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.BackgroundJobs.Add(QueuedJob(achievementId, JobType.AchievementCheck));
            db.BackgroundJobs.Add(QueuedJob(unhandledId, JobType.SkillProfileUpdate));
            await db.SaveChangesAsync();
        }

        await _factory.DrainBackgroundJobsAsync(passes: 1);

        var achievement = await LoadJobAsync(achievementId);
        Assert.Equal(AsyncState.Completed, achievement.State);
        Assert.Equal(0, achievement.RetryCount);

        // The generic failure path would have re-queued it ("retrying", RetryCount 1) and, after
        // the retries, raised an admin stuck-job alert. This type has no handler: fail it once.
        var unhandled = await LoadJobAsync(unhandledId);
        Assert.Equal(AsyncState.Failed, unhandled.State);
        Assert.Equal("unhandled_job_type", unhandled.StatusReasonCode);
        Assert.Equal(0, unhandled.RetryCount);
        Assert.False(unhandled.Retryable);
        Assert.Contains(nameof(JobType.SkillProfileUpdate), unhandled.StatusMessage);
    }

    [Fact]
    public void TheExecutorSwitch_HasAnExplicitAchievementCheckCase_AheadOfItsThrowingDefault()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "backend", "src", "OetLearner.Api", "Services", "BackgroundJobProcessor.cs"));

        var achievementCase = source.IndexOf("case JobType.AchievementCheck:", StringComparison.Ordinal);
        var throwingDefault = source.IndexOf("throw new UnhandledJobTypeException(job.Type)", StringComparison.Ordinal);
        Assert.True(achievementCase >= 0, "AchievementCheck must stay an explicit case");
        Assert.True(throwingDefault > achievementCase, "the throwing default must come after the AchievementCheck case");
    }

    // ── Speaking transcription lane ─────────────────────────────────────

    [Fact]
    public void TheTranscriptionQueue_HasItsOwnLoop_AndIsNotPolledInsideTheJobPass()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "backend", "src", "OetLearner.Api", "Services", "BackgroundJobProcessor.cs"));

        Assert.Contains("RunSpeakingTranscriptionLoopAsync(stoppingToken)", source);
        Assert.DoesNotContain("await RunSpeakingTranscriptionQueueAsync(scope.ServiceProvider, cancellationToken)", source);
    }

    [Fact]
    public async Task TranscriptionPoll_RecoversStaleRowsAtMostOncePerInterval()
    {
        var dbName = $"transcription-lane-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddScoped(_ => NewDb(dbName));
        services.AddScoped(sp => new SpeakingTranscriptionPipeline(
            sp.GetRequiredService<LearnerDbContext>(), provider: null!, NullLogger<SpeakingTranscriptionPipeline>.Instance));
        using var provider = services.BuildServiceProvider();
        var processor = new BackgroundJobProcessor(null!, NullLogger<BackgroundJobProcessor>.Instance);
        var now = DateTimeOffset.UtcNow;
        var stale = now - SpeakingTranscriptionPipeline.ProcessingLease - TimeSpan.FromMinutes(1);

        // A stale row has no recording, so once re-queued and claimed it ends as a visible
        // no_recording failure: that outcome is only reachable through the recovery sweep.
        await SeedProcessingTranscriptAsync(dbName, "t-1", stale);
        await PollAsync(processor, provider, now);
        Assert.Equal("no_recording", await FailureReasonAsync(dbName, "t-1"));

        await SeedProcessingTranscriptAsync(dbName, "t-2", stale);
        await PollAsync(processor, provider, now.AddSeconds(10));
        Assert.Equal(SpeakingTranscriptionPipeline.StateProcessing, await ProviderOfAsync(dbName, "t-2"));

        await PollAsync(processor, provider, now.AddSeconds(61));
        Assert.Equal("no_recording", await FailureReasonAsync(dbName, "t-2"));
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private BackgroundJobProcessor NewProcessor()
        => new(_factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<BackgroundJobProcessor>.Instance);

    private async Task<bool> SweepAsync(BackgroundJobProcessor processor, DateTimeOffset now)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        return await processor.ReconcileFreezeLifecycleIfDueAsync(scope.ServiceProvider, db, now, CancellationToken.None);
    }

    private async Task SeedScheduledFreezeAsync(string id, DateTimeOffset scheduledStartAt)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        db.AccountFreezeRecords.Add(new AccountFreezeRecord
        {
            Id = id,
            UserId = id,
            RequestedByLearnerId = id,
            Status = FreezeStatus.Scheduled,
            IsCurrent = true,
            IsSelfService = false,
            RequestedAt = now,
            ScheduledStartAt = scheduledStartAt,
            EndedAt = scheduledStartAt.AddDays(7),
            DurationDays = 7,
            Reason = "sweep test",
            PolicySnapshotJson = "{}",
            EligibilitySnapshotJson = "{}",
            PolicyVersionSnapshot = 1,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private async Task<AccountFreezeRecord> LoadFreezeAsync(string id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        return await db.AccountFreezeRecords.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private async Task<BackgroundJobItem> LoadJobAsync(string id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        return await db.BackgroundJobs.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private static LearnerDbContext NewDb(string name)
        => new(new DbContextOptionsBuilder<LearnerDbContext>().UseInMemoryDatabase(name).Options);

    // scopeFactory is only used by ProcessOnceAsync and the transcription loop, not by the gate.
    private static BackgroundJobProcessor NewProcessorWithoutScopes()
        => new(null!, NullLogger<BackgroundJobProcessor>.Instance);

    private static async Task SeedProcessingJobsAsync(string dbName, DateTimeOffset claimedAt, params string[] ids)
    {
        await using var seed = NewDb(dbName);
        foreach (var id in ids)
        {
            seed.BackgroundJobs.Add(ProcessingJob(id, claimedAt));
        }

        await seed.SaveChangesAsync();
    }

    /// <summary>What process A holds after its claim: every row of the batch, tracked, in claim order.</summary>
    private static Task<List<BackgroundJobItem>> LoadBatchAsync(LearnerDbContext db)
        => db.BackgroundJobs.OrderBy(j => j.Id).ToListAsync();

    private static async Task<Dictionary<string, BackgroundJobItem>> ReadJobsAsync(string dbName)
    {
        await using var verify = NewDb(dbName);
        return await verify.BackgroundJobs.AsNoTracking().ToDictionaryAsync(j => j.Id);
    }

    /// <summary>Another process's stuck-job sweep (its own context, no learner side effects needed to re-queue).</summary>
    private static async Task RunStuckJobSweepAsync(string dbName)
    {
        await using var db = NewDb(dbName);
        using var services = new ServiceCollection().BuildServiceProvider();
        await NewProcessorWithoutScopes().RecoverStuckJobsAsync(services, db, CancellationToken.None);
    }

    private static BackgroundJobItem QueuedJob(string id, JobType type)
    {
        var now = DateTimeOffset.UtcNow;
        return new BackgroundJobItem
        {
            Id = id,
            Type = type,
            State = AsyncState.Queued,
            PayloadJson = "{}",
            CreatedAt = now.AddSeconds(-5),
            AvailableAt = now.AddSeconds(-5),
            LastTransitionAt = now.AddSeconds(-5),
            StatusReasonCode = "queued",
            StatusMessage = "Queued",
            Retryable = true,
        };
    }

    private static BackgroundJobItem ProcessingJob(string id, DateTimeOffset claimedAt)
        => new()
        {
            Id = id,
            Type = JobType.NotificationFanout,
            State = AsyncState.Processing,
            PayloadJson = "{}",
            CreatedAt = claimedAt,
            AvailableAt = claimedAt,
            LastTransitionAt = claimedAt,
            StatusReasonCode = "processing",
            StatusMessage = "Job is processing.",
            Retryable = true,
        };

    private static async Task PollAsync(BackgroundJobProcessor processor, IServiceProvider root, DateTimeOffset now)
    {
        await using var scope = root.CreateAsyncScope();
        await processor.RunSpeakingTranscriptionQueueAsync(scope.ServiceProvider, now, CancellationToken.None);
    }

    private static async Task SeedProcessingTranscriptAsync(string dbName, string id, DateTimeOffset generatedAt)
    {
        await using var db = NewDb(dbName);
        db.SpeakingTranscripts.Add(new SpeakingTranscript
        {
            Id = id,
            SpeakingSessionId = $"session-{id}",
            Provider = SpeakingTranscriptionPipeline.StateProcessing,
            Language = "en",
            SegmentsJson = "[]",
            IsLatest = false,
            GeneratedAt = generatedAt,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string> ProviderOfAsync(string dbName, string id)
    {
        await using var db = NewDb(dbName);
        return (await db.SpeakingTranscripts.AsNoTracking().SingleAsync(t => t.Id == id)).Provider;
    }

    private static async Task<string?> FailureReasonAsync(string dbName, string id)
    {
        await using var db = NewDb(dbName);
        var row = await db.SpeakingTranscripts.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Equal(SpeakingTranscriptionPipeline.StateFailed, row.Provider);
        return SpeakingTranscriptionPipeline.ReadFailureField(row.SegmentsJson, "reasonCode");
    }

    private static FieldInfo Field(string name)
        => typeof(BackgroundJobProcessor).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new InvalidOperationException($"BackgroundJobProcessor.{name} was not found.");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend", "src", "OetLearner.Api")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

/// <summary>
/// Real-PostgreSQL twin of the claimed-batch ownership tests (skips without
/// <c>OET_TEST_POSTGRES_CONNECTION</c>): production takes the compare-and-swap
/// <c>ExecuteUpdateAsync</c> path, which the in-memory provider cannot exercise. The stuck-job
/// sweep is the real <see cref="BackgroundJobProcessor.RecoverStuckJobsAsync"/>.
/// </summary>
[Collection(PostgreSqlExclusiveCollection.Name)]
public sealed class BackgroundJobClaimOwnershipPostgreSqlTests
{
    private const string JobsDdl = """
        CREATE TABLE "BackgroundJobs" (
            "Id" varchar(64) PRIMARY KEY,
            "Type" integer NOT NULL,
            "State" integer NOT NULL,
            "AttemptId" text NULL,
            "ResourceId" text NULL,
            "PayloadJson" text NOT NULL DEFAULT '{}',
            "CreatedAt" timestamptz NOT NULL,
            "AvailableAt" timestamptz NOT NULL,
            "LastTransitionAt" timestamptz NOT NULL,
            "StatusReasonCode" text NOT NULL DEFAULT 'processing',
            "StatusMessage" text NOT NULL DEFAULT 'Job is processing.',
            "Retryable" boolean NOT NULL DEFAULT true,
            "RetryCount" integer NOT NULL DEFAULT 0,
            "RetryAfterMs" integer NULL
        );
        """;

    [PostgreSqlFact]
    public async Task AnAgedTail_IsRefreshedInTheStore_SoTheSweepNeverRequeuesIt()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(JobsDdl);
        var now = DateTimeOffset.UtcNow;
        var claimedAt = now.AddMinutes(-31);
        foreach (var id in new[] { "job-a", "job-b", "job-c" })
        {
            await InsertProcessingAsync(database, id, claimedAt);
        }

        await using var dbA = CreateContext(database);
        var batch = await dbA.BackgroundJobs.OrderBy(j => j.Id).ToListAsync();
        var processor = new BackgroundJobProcessor(null!, NullLogger<BackgroundJobProcessor>.Instance);

        Assert.True(await processor.TryBeginClaimedJobAsync(dbA, batch, 0, CancellationToken.None));
        await RunStuckJobSweepAsync(database);

        await using var verify = CreateContext(database);
        var rows = await verify.BackgroundJobs.AsNoTracking().ToDictionaryAsync(j => j.Id);
        foreach (var id in new[] { "job-a", "job-b", "job-c" })
        {
            Assert.Equal(AsyncState.Processing, rows[id].State);
            Assert.Equal(0, rows[id].RetryCount);
            Assert.True(rows[id].LastTransitionAt > now.AddSeconds(-30), $"{id} stamp was refreshed in the store");
        }
    }

    [PostgreSqlFact]
    public async Task ASweptBatch_IsRefusedByTheCompareAndSwap_AndTheStaleCopiesAreNeverWrittenBack()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(JobsDdl);
        var claimedAt = DateTimeOffset.UtcNow.AddMinutes(-31);
        foreach (var id in new[] { "job-a", "job-b", "job-c" })
        {
            await InsertProcessingAsync(database, id, claimedAt);
        }

        await using var dbA = CreateContext(database);
        var batch = await dbA.BackgroundJobs.OrderBy(j => j.Id).ToListAsync();
        await RunStuckJobSweepAsync(database);
        var processor = new BackgroundJobProcessor(null!, NullLogger<BackgroundJobProcessor>.Instance);

        for (var index = 0; index < batch.Count; index++)
        {
            Assert.False(await processor.TryBeginClaimedJobAsync(dbA, batch, index, CancellationToken.None));
        }

        await dbA.SaveChangesAsync();
        await using var verify = CreateContext(database);
        var rows = await verify.BackgroundJobs.AsNoTracking().ToDictionaryAsync(j => j.Id);
        foreach (var id in new[] { "job-a", "job-b", "job-c" })
        {
            Assert.Equal(AsyncState.Queued, rows[id].State);
            Assert.Equal(1, rows[id].RetryCount);
            Assert.Equal("stale_processing_requeued", rows[id].StatusReasonCode);
        }
    }

    [PostgreSqlFact]
    public async Task ARowReclaimedAfterTheSweep_IsNotOurs_WhileTheRowThatIsStillOursRuns()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(JobsDdl);
        var claimedAt = DateTimeOffset.UtcNow.AddMinutes(-12);
        await InsertProcessingAsync(database, "job-a", claimedAt);
        await InsertProcessingAsync(database, "job-b", claimedAt);

        await using var dbA = CreateContext(database);
        var batch = await dbA.BackgroundJobs.OrderBy(j => j.Id).ToListAsync();
        // Swept and re-claimed by another process: Processing again, one retry later.
        await database.ExecuteAsync("""UPDATE "BackgroundJobs" SET "RetryCount" = 1, "LastTransitionAt" = now() WHERE "Id" = 'job-a';""");
        var processor = new BackgroundJobProcessor(null!, NullLogger<BackgroundJobProcessor>.Instance);

        Assert.False(await processor.TryBeginClaimedJobAsync(dbA, batch, 0, CancellationToken.None));
        Assert.True(await processor.TryBeginClaimedJobAsync(dbA, batch, 1, CancellationToken.None));

        await dbA.SaveChangesAsync();
        await using var verify = CreateContext(database);
        var rows = await verify.BackgroundJobs.AsNoTracking().ToDictionaryAsync(j => j.Id);
        Assert.Equal(1, rows["job-a"].RetryCount);
        Assert.Equal(0, rows["job-b"].RetryCount);
        Assert.True(rows["job-b"].LastTransitionAt > DateTimeOffset.UtcNow.AddMinutes(-1), "the owned row's stamp was refreshed");
    }

    private static async Task RunStuckJobSweepAsync(PostgreSqlTestDatabase database)
    {
        await using var db = CreateContext(database);
        using var services = new ServiceCollection().BuildServiceProvider();
        var processor = new BackgroundJobProcessor(null!, NullLogger<BackgroundJobProcessor>.Instance);
        await processor.RecoverStuckJobsAsync(services, db, CancellationToken.None);
    }

    private static LearnerDbContext CreateContext(PostgreSqlTestDatabase database)
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseNpgsql(database.SchemaConnectionString, npgsql => npgsql.UseVector())
            .Options);

    private static async Task InsertProcessingAsync(PostgreSqlTestDatabase database, string id, DateTimeOffset claimedAt)
        => await database.ExecuteAsync($"""
            INSERT INTO "BackgroundJobs" ("Id", "Type", "State", "CreatedAt", "AvailableAt", "LastTransitionAt")
            VALUES ('{id}', {(int)JobType.NotificationFanout}, {(int)AsyncState.Processing},
                    TIMESTAMPTZ '{claimedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss.ffffff}+00',
                    TIMESTAMPTZ '{claimedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss.ffffff}+00',
                    TIMESTAMPTZ '{claimedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss.ffffff}+00');
            """);
}
