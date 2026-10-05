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
/// that starts late in a long batch is re-stamped so another process's stuck-job sweep
/// cannot re-queue it, an unhandled job type fails once (no silent "Completed", no retry
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

    // ── execution-start stamp ───────────────────────────────────────────

    [Fact]
    public async Task ExecutionStartStamp_RefreshesAnAgedClaimStamp_AndLeavesAFreshOneAlone()
    {
        var dbName = $"job-stamp-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var agedClaim = now.AddMinutes(-12);
        var freshClaim = now.AddMinutes(-1);
        await using (var seed = NewDb(dbName))
        {
            seed.BackgroundJobs.Add(ProcessingJob("job-aged", agedClaim));
            seed.BackgroundJobs.Add(ProcessingJob("job-fresh", freshClaim));
            await seed.SaveChangesAsync();
        }

        // scopeFactory is not used by the stamp.
        var processor = new BackgroundJobProcessor(null!, NullLogger<BackgroundJobProcessor>.Instance);
        await using (var db = NewDb(dbName))
        {
            var aged = await db.BackgroundJobs.SingleAsync(j => j.Id == "job-aged");
            var fresh = await db.BackgroundJobs.SingleAsync(j => j.Id == "job-fresh");
            await processor.StampExecutionStartAsync(db, aged, CancellationToken.None);
            await processor.StampExecutionStartAsync(db, fresh, CancellationToken.None);
        }

        await using var verify = NewDb(dbName);
        var persisted = await verify.BackgroundJobs.AsNoTracking().ToDictionaryAsync(j => j.Id);
        Assert.True(persisted["job-aged"].LastTransitionAt > now.AddSeconds(-30), "the aged stamp is refreshed to the start time");
        Assert.Equal(AsyncState.Processing, persisted["job-aged"].State);
        Assert.Equal(freshClaim, persisted["job-fresh"].LastTransitionAt);
    }

    [Fact]
    public void ARunningJob_CanNeverLookOrphaned_ToAnotherProcessesStuckJobSweep()
    {
        var maxExecution = (TimeSpan)Field("MaxJobExecutionTime").GetValue(null)!;
        var stuckThreshold = (TimeSpan)Field("StuckJobStaleThreshold").GetValue(null)!;

        // The stamp can be up to JobStartStampAfter old when the job begins, and the job may
        // then run for MaxJobExecutionTime: that total must stay under the sweep's threshold.
        Assert.True(BackgroundJobProcessor.JobStartStampAfter + maxExecution < stuckThreshold);
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
