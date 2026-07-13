using System.Data.Common;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests;

public sealed class WorkerQueryPerformanceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SqlCaptureInterceptor _sql = new();
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_sql)
            .Options;

        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task TextExtractionWorker_SelectsAtMostTwentyOldestPapersInSql()
    {
        await using (var db = new LearnerDbContext(_options))
        {
            var now = DateTimeOffset.UtcNow;
            db.ContentPapers.AddRange(Enumerable.Range(0, 25).Select(index =>
                CreatePaper($"paper-{index:D2}", now.AddMinutes(index))));
            db.ContentPapers.Add(CreatePaper(
                "paper-archived",
                now.AddYears(-1),
                ContentStatus.Archived));
            await db.SaveChangesAsync();
        }

        var extraction = new RecordingExtractionService();
        using var provider = BuildWorkerProvider(services =>
            services.AddSingleton<IContentTextExtractionService>(extraction));
        _sql.Clear();

        var processed = await new ContentTextExtractionWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ContentTextExtractionWorker>.Instance)
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(20, processed);
        Assert.Equal(
            Enumerable.Range(0, 20).Select(index => $"paper-{index:D2}"),
            extraction.PaperIds);
        var command = Assert.Single(_sql.ReaderCommands.Where(command =>
            command.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("ORDER BY", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadCleanup_FiltersExpirationInSql()
    {
        var now = DateTimeOffset.UtcNow;
        await using (var db = new LearnerDbContext(_options))
        {
            db.AdminUploadSessions.AddRange(
                CreateUploadSession("expired", now.AddMinutes(-1)),
                CreateUploadSession("current", now.AddHours(1)));
            await db.SaveChangesAsync();
        }

        var storage = new RecordingFileStorage();
        using var provider = BuildWorkerProvider(services =>
        {
            services.AddSingleton<IFileStorage>(storage);
            services.AddSingleton<IOptions<StorageOptions>>(Options.Create(new StorageOptions()));
        });
        _sql.Clear();

        var count = await new AdminUploadCleanupWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<AdminUploadCleanupWorker>.Instance)
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Single(storage.DeletedPrefixes);
        var command = Assert.Single(_sql.ReaderCommands.Where(command =>
            command.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("ExpiresAt", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<=", command, StringComparison.Ordinal);

        await using var verificationDb = new LearnerDbContext(_options);
        Assert.Equal(
            AdminUploadState.Expired,
            await verificationDb.AdminUploadSessions
                .Where(session => session.Id == "expired")
                .Select(session => session.State)
                .SingleAsync());
        Assert.Equal(
            AdminUploadState.Started,
            await verificationDb.AdminUploadSessions
                .Where(session => session.Id == "current")
                .Select(session => session.State)
                .SingleAsync());
    }

    [Fact]
    public async Task MediaAudit_UsesOneAggregateCommandAndPreservesCounts()
    {
        await using (var db = new LearnerDbContext(_options))
        {
            db.MediaAssets.AddRange(
                CreateMedia("ready-video", "video/mp4", MediaAssetStatus.Ready),
                CreateMedia("ready-audio", "audio/mpeg", MediaAssetStatus.Ready, transcriptPath: ""),
                CreateMedia("ready-image", "image/png", MediaAssetStatus.Ready),
                CreateMedia("processing-video", "video/mp4", MediaAssetStatus.Processing),
                CreateMedia("failed-audio", "audio/mpeg", MediaAssetStatus.Failed));
            await db.SaveChangesAsync();
        }

        await using var auditDb = new LearnerDbContext(_options);
        _sql.Clear();

        var result = await new MediaNormalizationService(auditDb)
            .AuditMediaAssetsAsync(CancellationToken.None);

        Assert.Equal(5, result.TotalAssets);
        Assert.Equal(3, result.Ready);
        Assert.Equal(1, result.Processing);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.MissingThumbnails);
        Assert.Equal(2, result.MissingTranscripts);
        var command = Assert.Single(_sql.ReaderCommands);
        Assert.Contains("GROUP BY", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT", command, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    public async Task ContentStaleness_UsesThreeQueriesRegardlessOfPublishedItemCount(int itemCount)
    {
        var now = DateTimeOffset.UtcNow;
        await using (var db = new LearnerDbContext(_options))
        {
            db.Criteria.AddRange(Enumerable.Range(0, 4).Select(index => new CriterionReference
            {
                Id = $"criterion-{index}",
                SubtestCode = "reading",
                Code = $"criterion-{index}",
                Label = $"Criterion {index}",
                Description = $"Criterion {index}"
            }));

            for (var index = 0; index < itemCount; index++)
            {
                var contentId = $"content-{index:D2}";
                db.ContentItems.Add(CreateContentItem(contentId, now.AddDays(-200)));
                db.Attempts.Add(CreateAttempt(
                    $"attempt-{index:D2}",
                    contentId,
                    now.AddDays(-200)));
            }

            await db.SaveChangesAsync();
        }

        await using var stalenessDb = new LearnerDbContext(_options);
        _sql.Clear();

        var results = await new ContentStalenessService(
                stalenessDb,
                NullLogger<ContentStalenessService>.Instance)
            .ComputeAllAsync(CancellationToken.None);

        Assert.Equal(itemCount, results.Count);
        Assert.Equal(3, _sql.ReaderCommands.Count);
        Assert.All(results, result =>
        {
            Assert.Equal(200, result.DaysSinceLastEdit);
            Assert.Equal(200, result.DaysSinceLastUsage);
            Assert.Equal(0, result.UsageCountLast90Days);
            Assert.Equal(25, result.RubricCoveragePercent);
            Assert.Equal(
                ["criterion-1", "criterion-2", "criterion-3"],
                result.MissingRubricCriteria);
            Assert.True(result.IsStale);
            Assert.Equal(
                "Last edited 200 days ago; Last used 200 days ago; "
                + "Only 0 uses in last 90 days; Rubric coverage 25% is below threshold",
                result.StalenessReason);
            Assert.Equal("major_revision", result.RecommendedAction);
        });
    }

    [Fact]
    public async Task ListeningV2Backfill_WhenCurrent_ReturnsZeroFromOneAntiJoinQuery()
    {
        var now = DateTimeOffset.UtcNow;
        await using (var db = new LearnerDbContext(_options))
        {
            db.ContentPapers.Add(CreatePaper("listening-paper", now));
            db.ListeningAttempts.Add(new ListeningAttempt
            {
                Id = "listening-attempt",
                UserId = "current-user",
                PaperId = "listening-paper",
                StartedAt = now,
                LastActivityAt = now,
                PolicySnapshotJson = "{}"
            });
            db.ListeningPathwayProgress.Add(new ListeningPathwayProgress
            {
                Id = "pathway-progress",
                UserId = "current-user",
                StageCode = "diagnostic",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        using var provider = BuildWorkerProvider();
        var service = new ListeningV2BackfillService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ListeningV2BackfillService>.Instance);
        _sql.Clear();

        var pendingCount = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, pendingCount);
        var command = Assert.Single(_sql.ReaderCommands);
        Assert.Contains("NOT EXISTS", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadinessRollover_WithNoStaleUsers_StillPrunesWithOneDelete()
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-26 * 7));
        await using (var db = new LearnerDbContext(_options))
        {
            db.ReadinessHistories.AddRange(
                CreateReadinessHistory("old-1", cutoff.AddDays(-14)),
                CreateReadinessHistory("old-2", cutoff.AddDays(-7)),
                CreateReadinessHistory("current", cutoff));
            await db.SaveChangesAsync();
        }

        await using var pruneDb = new LearnerDbContext(_options);
        using var provider = new ServiceCollection().BuildServiceProvider();
        _sql.Clear();

        await BackgroundJobProcessor.RunReadinessRolloverAsync(
            provider,
            pruneDb,
            CancellationToken.None);

        var command = Assert.Single(_sql.NonQueryCommands);
        Assert.Contains("DELETE", command, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await pruneDb.ReadinessHistories.CountAsync());
    }

    [Fact]
    public void BackgroundJobClaim_PostgresSql_UsesAtomicSkipLockedUpdateReturning()
    {
        var sql = GetBackgroundJobClaimSql();

        Assert.Contains("FOR UPDATE SKIP LOCKED", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UPDATE \"BackgroundJobs\" AS job", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RETURNING", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT @batchSize", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"AvailableAt\" <= @now", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY \"AvailableAt\", \"CreatedAt\"", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BackgroundJobClaim_SqliteFallbackClaimsEligibleJobsInOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var seeded = new Dictionary<string, BackgroundJobSnapshot>();
        var batchSize = GetBackgroundJobClaimBatchSize();

        await using (var db = new LearnerDbContext(_options))
        {
            var jobs = new List<BackgroundJobItem>();
            for (var index = 0; index < 52; index++)
            {
                var createdAt = now.AddMinutes(-index - 1);
                var availableAt = now.AddMinutes(-(index % 7) - 1);
                var job = CreateBackgroundJob(
                    $"queued-{index:D2}",
                    createdAt,
                    availableAt,
                    retryable: index % 3 != 0,
                    retryCount: index % 3,
                    retryAfterMs: index % 2 == 0 ? 500 * (index + 1) : null);
                jobs.Add(job);
                seeded[job.Id] = BackgroundJobSnapshot.From(job);
            }

            var unavailable = CreateBackgroundJob(
                "queued-future",
                now.AddMinutes(-100),
                now.AddMinutes(5),
                retryable: false,
                retryCount: 2,
                retryAfterMs: 7000);
            jobs.Add(unavailable);
            seeded[unavailable.Id] = BackgroundJobSnapshot.From(unavailable);

            jobs.Add(new BackgroundJobItem
            {
                Id = "already-processing",
                Type = JobType.NotificationFanout,
                State = AsyncState.Processing,
                PayloadJson = "{}",
                CreatedAt = now.AddMinutes(-200),
                AvailableAt = now.AddMinutes(-200),
                LastTransitionAt = now.AddMinutes(-10),
                StatusReasonCode = "processing",
                StatusMessage = "Job is processing.",
                Retryable = true,
                RetryCount = 1,
                RetryAfterMs = null
            });

            db.BackgroundJobs.AddRange(jobs);
            await db.SaveChangesAsync();
        }

        var expectedClaimOrder = seeded.Values
            .Where(job => job.AvailableAt <= now)
            .OrderBy(job => job.AvailableAt)
            .ThenBy(job => job.CreatedAt)
            .Take(batchSize)
            .ToList();

        _sql.Clear();

        await using var claimDb = new LearnerDbContext(_options);
        var claimed = await InvokeClaimQueuedJobsAsync(claimDb, now, CancellationToken.None);

        Assert.Equal(batchSize, claimed.Count);
        Assert.Equal(expectedClaimOrder.Select(job => job.Id), claimed.Select(job => job.Id));
        Assert.Contains(_sql.ReaderCommands, command =>
            command.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
            && command.Contains("\"BackgroundJobs\"", StringComparison.Ordinal));
        Assert.Contains(
            _sql.ReaderCommands.Concat(_sql.NonQueryCommands),
            command => command.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.Contains("\"BackgroundJobs\"", StringComparison.Ordinal));
        Assert.DoesNotContain(
            _sql.ReaderCommands.Concat(_sql.NonQueryCommands),
            command => command.Contains("SKIP LOCKED", StringComparison.OrdinalIgnoreCase));

        foreach (var claimedJob in claimed)
        {
            var original = seeded[claimedJob.Id];
            Assert.Equal(AsyncState.Processing, claimedJob.State);
            Assert.Equal("processing", claimedJob.StatusReasonCode);
            Assert.Equal("Job is processing.", claimedJob.StatusMessage);
            Assert.Equal(original.CreatedAt, claimedJob.CreatedAt);
            Assert.Equal(original.AvailableAt, claimedJob.AvailableAt);
            Assert.Equal(original.Retryable, claimedJob.Retryable);
            Assert.Equal(original.RetryCount, claimedJob.RetryCount);
            Assert.Equal(original.RetryAfterMs, claimedJob.RetryAfterMs);
            Assert.Equal(now, claimedJob.LastTransitionAt);
        }

        await using var verificationDb = new LearnerDbContext(_options);
        var claimedIds = claimed.Select(job => job.Id).ToHashSet(StringComparer.Ordinal);
        var persisted = await verificationDb.BackgroundJobs
            .Where(job => seeded.Keys.Contains(job.Id))
            .ToListAsync();

        foreach (var row in persisted.Where(job => claimedIds.Contains(job.Id)))
        {
            var original = seeded[row.Id];
            Assert.Equal(AsyncState.Processing, row.State);
            Assert.Equal("processing", row.StatusReasonCode);
            Assert.Equal("Job is processing.", row.StatusMessage);
            Assert.Equal(original.CreatedAt, row.CreatedAt);
            Assert.Equal(original.AvailableAt, row.AvailableAt);
            Assert.Equal(original.Retryable, row.Retryable);
            Assert.Equal(original.RetryCount, row.RetryCount);
            Assert.Equal(original.RetryAfterMs, row.RetryAfterMs);
            Assert.Equal(now, row.LastTransitionAt);
        }

        Assert.Equal(
            2,
            await verificationDb.BackgroundJobs.CountAsync(job =>
                job.State == AsyncState.Queued && job.AvailableAt <= now));

        var futureRow = await verificationDb.BackgroundJobs.SingleAsync(job => job.Id == "queued-future");
        Assert.Equal(AsyncState.Queued, futureRow.State);
        Assert.Equal(seeded[futureRow.Id].RetryCount, futureRow.RetryCount);
        Assert.Equal(seeded[futureRow.Id].RetryAfterMs, futureRow.RetryAfterMs);
    }

    private static string GetBackgroundJobClaimSql()
        => (string)(typeof(BackgroundJobProcessor)
            .GetProperty(
                "PostgresClaimQueuedJobsSql",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .GetValue(null)
            ?? throw new InvalidOperationException("Claim SQL was not available."));

    private static int GetBackgroundJobClaimBatchSize()
        => (int)(typeof(BackgroundJobProcessor)
            .GetField(
                "JobClaimBatchSize",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .GetValue(null)
            ?? throw new InvalidOperationException("Claim batch size was not available."));

    private static async Task<List<BackgroundJobItem>> InvokeClaimQueuedJobsAsync(
        LearnerDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var method = typeof(BackgroundJobProcessor).GetMethod(
            "ClaimQueuedJobsAsync",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException("Claim method was not available.");

        var task = (Task<List<BackgroundJobItem>>)method.Invoke(null, [db, now, cancellationToken])!;
        return await task;
    }

    private ServiceProvider BuildWorkerProvider(
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new LearnerDbContext(_options));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static ContentPaper CreatePaper(
        string id,
        DateTimeOffset updatedAt,
        ContentStatus status = ContentStatus.Published)
        => new()
        {
            Id = id,
            SubtestCode = "listening",
            Title = id,
            Slug = id,
            Status = status,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };

    private static AdminUploadSession CreateUploadSession(
        string id,
        DateTimeOffset expiresAt)
        => new()
        {
            Id = id,
            AdminUserId = "admin",
            OriginalFilename = $"{id}.pdf",
            Extension = "pdf",
            DeclaredMimeType = "application/pdf",
            State = AdminUploadState.Started,
            CreatedAt = expiresAt.AddHours(-1),
            ExpiresAt = expiresAt
        };

    private static MediaAsset CreateMedia(
        string id,
        string mimeType,
        MediaAssetStatus status,
        string? thumbnailPath = null,
        string? transcriptPath = null)
        => new()
        {
            Id = id,
            OriginalFilename = id,
            MimeType = mimeType,
            Format = mimeType[(mimeType.IndexOf('/') + 1)..],
            StoragePath = id,
            ThumbnailPath = thumbnailPath,
            TranscriptPath = transcriptPath,
            Status = status,
            UploadedAt = DateTimeOffset.UtcNow
        };

    private static ContentItem CreateContentItem(string id, DateTimeOffset timestamp)
        => new()
        {
            Id = id,
            ContentType = "practice",
            SubtestCode = "reading",
            Title = id,
            Difficulty = "standard",
            CriteriaFocusJson = """["criterion-0"]""",
            PublishedRevisionId = $"revision-{id}",
            Status = ContentStatus.Published,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

    private static Attempt CreateAttempt(
        string id,
        string contentId,
        DateTimeOffset timestamp)
        => new()
        {
            Id = id,
            UserId = "learner",
            ContentId = contentId,
            SubtestCode = "reading",
            Context = "practice",
            Mode = "paper",
            StartedAt = timestamp,
            CreatedAt = timestamp
        };

    private static ReadinessHistory CreateReadinessHistory(
        string id,
        DateOnly weekStartDate)
        => new()
        {
            Id = id,
            UserId = "learner",
            WeekStartDate = weekStartDate,
            RecordedAt = DateTimeOffset.UtcNow,
            Risk = "Low"
        };

    private static BackgroundJobItem CreateBackgroundJob(
        string id,
        DateTimeOffset createdAt,
        DateTimeOffset availableAt,
        bool retryable,
        int retryCount,
        int? retryAfterMs)
        => new()
        {
            Id = id,
            Type = JobType.NotificationFanout,
            State = AsyncState.Queued,
            PayloadJson = "{}",
            CreatedAt = createdAt,
            AvailableAt = availableAt,
            LastTransitionAt = createdAt,
            StatusReasonCode = "queued",
            StatusMessage = "Queued",
            Retryable = retryable,
            RetryCount = retryCount,
            RetryAfterMs = retryAfterMs
        };

    private sealed record BackgroundJobSnapshot(
        string Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset AvailableAt,
        bool Retryable,
        int RetryCount,
        int? RetryAfterMs)
    {
        public static BackgroundJobSnapshot From(BackgroundJobItem job)
            => new(
                job.Id,
                job.CreatedAt,
                job.AvailableAt,
                job.Retryable,
                job.RetryCount,
                job.RetryAfterMs);
    }

    private sealed class RecordingExtractionService : IContentTextExtractionService
    {
        public List<string> PaperIds { get; } = [];

        public Task<int> ExtractForPaperAsync(string paperId, CancellationToken ct)
        {
            PaperIds.Add(paperId);
            return Task.FromResult(1);
        }
    }

    private sealed class RecordingFileStorage : IFileStorage
    {
        public List<string> DeletedPrefixes { get; } = [];

        public Task<long> WriteAsync(string key, Stream source, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }

        public Task<bool> DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }

        public Task<long> LengthAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(0L);
        }

        public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            DeletedPrefixes.Add(prefix);
            return Task.FromResult(1);
        }

        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => null;
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> ReaderCommands { get; } = [];
        public List<string> NonQueryCommands { get; } = [];

        public void Clear()
        {
            ReaderCommands.Clear();
            NonQueryCommands.Clear();
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ReaderCommands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCommands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            NonQueryCommands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            NonQueryCommands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
