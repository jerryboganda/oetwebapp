using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services;

public class BackgroundJobProcessor(IServiceScopeFactory scopeFactory, ILogger<BackgroundJobProcessor> logger) : BackgroundService
{
    internal const int JobClaimBatchSize = 50;
    internal const int SqliteQueuedJobScanLimit = 200;
    internal static string PostgresClaimQueuedJobsSql => """
        WITH candidate AS (
            SELECT "Id"
            FROM "BackgroundJobs"
            WHERE "State" = @queuedState
              AND "AvailableAt" <= @now
            ORDER BY "AvailableAt", "CreatedAt"
            LIMIT @batchSize
            FOR UPDATE SKIP LOCKED
        ),
        claimed AS (
            UPDATE "BackgroundJobs" AS job
            SET "State" = @processingState,
                "StatusReasonCode" = @statusReasonCode,
                "StatusMessage" = @statusMessage,
                "LastTransitionAt" = @now
            FROM candidate
            WHERE job."Id" = candidate."Id"
            RETURNING
                job."Id",
                job."Type",
                job."State",
                job."AttemptId",
                job."ResourceId",
                job."PayloadJson",
                job."CreatedAt",
                job."AvailableAt",
                job."LastTransitionAt",
                job."StatusReasonCode",
                job."StatusMessage",
                job."Retryable",
                job."RetryCount",
                job."RetryAfterMs"
        )
        SELECT *
        FROM claimed
        ORDER BY "AvailableAt", "CreatedAt";
        """;

    private DateTimeOffset _lastReconciliationAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAutoAssignAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSlaCheckAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastReadinessRolloverAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSpeakingTranscriptionPollAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastBillingAbandonedCartSweepAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastBillingDunningRetryDispatchAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPrivateSpeakingNoShowSweepAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPrivateSpeakingReminderAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPrivateSpeakingReservationExpiryAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastStuckJobRecoveryAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromHours(1);
    /// <summary>How often to scan for jobs orphaned in Processing (e.g. by a
    /// container restart mid-job). Runs on the first tick after startup too,
    /// because a restart is exactly when orphans appear.</summary>
    private static readonly TimeSpan StuckJobRecoveryInterval = TimeSpan.FromMinutes(5);
    /// <summary>A job legitimately stays in Processing only for the duration of
    /// one handler execution (seconds to a few minutes). Past this it is
    /// presumed dead. Kept well above the 10-minute /health/ready warning so
    /// blue/green overlap (the old container may still be finishing a job)
    /// never races the recovery sweep.</summary>
    private static readonly TimeSpan StuckJobStaleThreshold = TimeSpan.FromMinutes(30);
    /// <summary>Stuck jobs older than this are failed without spending retries
    /// or notifying learners: re-running a days-old evaluation or reminder is
    /// more confusing than helpful, and bulk-recovering an old backlog must
    /// not blast stale notifications at real users.</summary>
    private static readonly TimeSpan StuckJobRetryMaxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan ExpertAutoAssignInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExpertSlaCheckInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReadinessRolloverInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan SpeakingTranscriptionPollInterval = TimeSpan.FromSeconds(10);
    /// <summary>How often to poll <c>DunningAttempts</c> for rows ready to retry.</summary>
    private static readonly TimeSpan BillingDunningRetryDispatchInterval = TimeSpan.FromMinutes(5);
    /// <summary>How often to enqueue the Private Speaking no-show sweep (T5).</summary>
    private static readonly TimeSpan PrivateSpeakingNoShowSweepInterval = TimeSpan.FromMinutes(5);
    /// <summary>How often to enqueue the Private Speaking reminder sweep. Must be
    /// well below the smallest 15-minute reminder offset so the 15-min reminder
    /// actually fires before the session starts.</summary>
    private static readonly TimeSpan PrivateSpeakingReminderInterval = TimeSpan.FromMinutes(2);
    /// <summary>How often to enqueue the Private Speaking unpaid-reservation expiry sweep.</summary>
    private static readonly TimeSpan PrivateSpeakingReservationExpiryInterval = TimeSpan.FromMinutes(2);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background job processing failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Exposed as <c>internal</c> so the test harness can drive a single
    /// pass of the job pipeline deterministically (the hosted-service loop
    /// is stripped by <c>TestWebApplicationFactory.IsLongRunningHostedWorker</c>
    /// to avoid 2-second tick races in tests).
    /// </summary>
    internal async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var jobs = await ClaimQueuedJobsAsync(db, now, cancellationToken);

        foreach (var job in jobs)
        {
            try
            {
                await ExecuteJobAsync(scope.ServiceProvider, db, job, cancellationToken);
                job.State = AsyncState.Completed;
                job.StatusReasonCode = "completed";
                job.StatusMessage = "Job completed successfully.";
                job.LastTransitionAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {JobId} of type {JobType} failed (attempt {Attempt})", job.Id, job.Type, job.RetryCount + 1);
                await DiscardPendingJobSideEffectsAsync(db, job.Id, cancellationToken);
                job.RetryCount += 1;
                job.LastTransitionAt = DateTimeOffset.UtcNow;

                const int maxRetries = 3;
                if (job.RetryCount < maxRetries)
                {
                    // Re-queue with exponential backoff
                    var delayMs = (int)Math.Pow(2, job.RetryCount) * 5000;
                    job.State = AsyncState.Queued;
                    job.AvailableAt = DateTimeOffset.UtcNow.AddMilliseconds(delayMs);
                    job.StatusReasonCode = "retrying";
                    job.StatusMessage = $"Retry {job.RetryCount}/{maxRetries} after failure: {ex.Message}";
                    job.RetryAfterMs = delayMs;
                }
                else
                {
                    job.State = AsyncState.Failed;
                    job.StatusReasonCode = "processing_failed";
                    job.StatusMessage = $"Failed after {maxRetries} attempts: {ex.Message}";
                    job.RetryAfterMs = 0;
                    await MarkResourceFailedAfterFinalRetryAsync(db, job, ex, cancellationToken);
                    await EmitFailureNotificationsAsync(scope.ServiceProvider, db, job, ex, cancellationToken);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        if (now - _lastStuckJobRecoveryAt >= StuckJobRecoveryInterval)
        {
            _lastStuckJobRecoveryAt = now;
            await RecoverStuckJobsAsync(scope.ServiceProvider, db, cancellationToken);
        }

        await ReconcileFreezeLifecycleAsync(scope.ServiceProvider, db, cancellationToken);

        if (now - _lastReconciliationAt >= ReconciliationInterval)
        {
            _lastReconciliationAt = now;
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
            await RunSubscriptionLifecycleCheckAsync(db, notifications, cancellationToken);
            await RunSlaAlertCheckAsync(db, notifications, cancellationToken);
            await RunDripCampaignDispatchAsync(db, notifications, cancellationToken);
        }

        // Expert auto-assign — runs on its own cadence so reviews flow to
        // experts within ~30s of submission without waiting for the hourly
        // reconciliation tick.
        if (now - _lastAutoAssignAt >= ExpertAutoAssignInterval)
        {
            _lastAutoAssignAt = now;
            try
            {
                var assigner = scope.ServiceProvider
                    .GetRequiredService<OetWithDrHesham.Api.Services.Expert.IExpertAutoAssignmentService>();
                await assigner.ProcessPendingAssignmentsAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ExpertAutoAssignment poll failed");
            }
        }

        if (now - _lastSlaCheckAt >= ExpertSlaCheckInterval)
        {
            _lastSlaCheckAt = now;
            try
            {
                var assigner = scope.ServiceProvider
                    .GetRequiredService<OetWithDrHesham.Api.Services.Expert.IExpertAutoAssignmentService>();
                await assigner.ProcessSlaEscalationsAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ExpertSlaEscalation poll failed");
            }
        }

        if (now - _lastReadinessRolloverAt >= ReadinessRolloverInterval)
        {
            _lastReadinessRolloverAt = now;
            try
            {
                await RunReadinessRolloverAsync(scope.ServiceProvider, db, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Readiness rollover poll failed");
            }
        }

        if (now - _lastSpeakingTranscriptionPollAt >= SpeakingTranscriptionPollInterval)
        {
            _lastSpeakingTranscriptionPollAt = now;
            try
            {
                await RunSpeakingTranscriptionQueueAsync(scope.ServiceProvider, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Speaking transcription queue poll failed");
            }
        }

        // ── Wave A5 — Billing recurring jobs ─────────────────────────
        // Dunning ladder dispatcher (every 5 min): claims due DunningAttempt
        // rows and enqueues per-attempt BillingDunningRetry jobs so the
        // standard job pipeline owns retries + backoff.
        if (now - _lastBillingDunningRetryDispatchAt >= BillingDunningRetryDispatchInterval)
        {
            _lastBillingDunningRetryDispatchAt = now;
            try
            {
                await EnqueueDueBillingDunningRetriesAsync(db, now, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Billing dunning retry dispatch failed");
            }
        }

        // Abandoned-cart sweep (daily 03:00 UTC). Enqueueing a single job at
        // a time keeps the pipeline idempotent even across multiple API replicas
        // — the BackgroundJobs queue is the source of truth.
        if (ShouldEnqueueDailyAbandonedCartSweep(now, _lastBillingAbandonedCartSweepAt))
        {
            _lastBillingAbandonedCartSweepAt = now;
            try
            {
                await EnqueueAbandonedCartSweepJobAsync(db, now, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Billing abandoned-cart sweep enqueue failed");
            }
        }

        // ── Private Speaking — automatic no-show sweep (T5, PDF §3.3.6/§13) ──
        // Enqueue a singleton sweep job every 5 minutes, mirroring the interval-
        // gated enqueue used by the Billing sweeps above. The job is processed by
        // the standard pipeline (JobType.PrivateSpeakingNoShowSweep dispatch).
        if (now - _lastPrivateSpeakingNoShowSweepAt >= PrivateSpeakingNoShowSweepInterval)
        {
            _lastPrivateSpeakingNoShowSweepAt = now;
            try
            {
                await EnqueuePrivateSpeakingNoShowSweepJobAsync(db, now, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Private Speaking no-show sweep enqueue failed");
            }
        }

        // ── Private Speaking — reminder sweep (PDF §10) ──────────────────────
        // Enqueue a singleton reminder job every 2 minutes (well below the 15-min
        // reminder offset), mirroring the no-show sweep enqueue. The job is
        // processed by JobType.PrivateSpeakingReminder → ProcessRemindersAsync.
        if (now - _lastPrivateSpeakingReminderAt >= PrivateSpeakingReminderInterval)
        {
            _lastPrivateSpeakingReminderAt = now;
            try
            {
                await EnqueuePrivateSpeakingReminderJobAsync(db, now, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Private Speaking reminder enqueue failed");
            }
        }

        // ── Private Speaking — unpaid-reservation expiry sweep ───────────────
        // Enqueue a singleton expiry job every 2 minutes, mirroring the no-show
        // sweep enqueue. The job is processed by
        // JobType.PrivateSpeakingReservationExpiry → ExpireStaleReservationsAsync.
        if (now - _lastPrivateSpeakingReservationExpiryAt >= PrivateSpeakingReservationExpiryInterval)
        {
            _lastPrivateSpeakingReservationExpiryAt = now;
            try
            {
                await EnqueuePrivateSpeakingReservationExpiryJobAsync(db, now, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Private Speaking reservation-expiry enqueue failed");
            }
        }
    }

    internal static async Task<List<BackgroundJobItem>> ClaimQueuedJobsAsync(
        LearnerDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsNpgsql())
        {
            return await ClaimQueuedJobsPostgresAsync(db, now, cancellationToken);
        }

        var queuedJobQuery = db.BackgroundJobs
            .Where(x => x.State == AsyncState.Queued);

        var queuedJobs = db.Database.IsSqlite()
            ? await queuedJobQuery
                .Take(SqliteQueuedJobScanLimit)
                .ToListAsync(cancellationToken)
            : await queuedJobQuery
                .OrderBy(x => x.CreatedAt)
                .Take(JobClaimBatchSize)
                .ToListAsync(cancellationToken);

        var jobs = queuedJobs
            .Where(x => x.AvailableAt <= now)
            .OrderBy(x => x.AvailableAt)
            .ThenBy(x => x.CreatedAt)
            .Take(JobClaimBatchSize)
            .ToList();

        foreach (var job in jobs)
        {
            job.State = AsyncState.Processing;
            job.StatusReasonCode = "processing";
            job.StatusMessage = "Job is processing.";
            job.LastTransitionAt = now;
        }

        if (jobs.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return jobs;
    }

    private static async Task<List<BackgroundJobItem>> ClaimQueuedJobsPostgresAsync(
        LearnerDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedConnection = connection.State != ConnectionState.Open;
        if (openedConnection)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = PostgresClaimQueuedJobsSql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(CreateDbParameter(command, "@queuedState", (int)AsyncState.Queued));
            command.Parameters.Add(CreateDbParameter(command, "@processingState", (int)AsyncState.Processing));
            command.Parameters.Add(CreateDbParameter(command, "@statusReasonCode", "processing"));
            command.Parameters.Add(CreateDbParameter(command, "@statusMessage", "Job is processing."));
            command.Parameters.Add(CreateDbParameter(command, "@now", now));
            command.Parameters.Add(CreateDbParameter(command, "@batchSize", JobClaimBatchSize));

            var jobs = new List<BackgroundJobItem>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                jobs.Add(new BackgroundJobItem
                {
                    Id = reader.GetString(0),
                    Type = (JobType)reader.GetInt32(1),
                    State = (AsyncState)reader.GetInt32(2),
                    AttemptId = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ResourceId = reader.IsDBNull(4) ? null : reader.GetString(4),
                    PayloadJson = reader.GetString(5),
                    CreatedAt = reader.GetFieldValue<DateTimeOffset>(6),
                    AvailableAt = reader.GetFieldValue<DateTimeOffset>(7),
                    LastTransitionAt = reader.GetFieldValue<DateTimeOffset>(8),
                    StatusReasonCode = reader.GetString(9),
                    StatusMessage = reader.GetString(10),
                    Retryable = reader.GetBoolean(11),
                    RetryCount = reader.GetInt32(12),
                    RetryAfterMs = reader.IsDBNull(13) ? null : reader.GetInt32(13)
                });
            }

            if (jobs.Count > 0)
            {
                db.AttachRange(jobs);
            }

            return jobs;
        }
        finally
        {
            if (openedConnection && db.Database.CurrentTransaction is null)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static DbParameter CreateDbParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        return parameter;
    }

    /// <summary>
    /// T5 — enqueue a single <see cref="JobType.PrivateSpeakingNoShowSweep"/> job,
    /// skipping if one is already queued/processing. The BackgroundJobs queue is
    /// the source of truth, so this stays idempotent across multiple API replicas.
    /// </summary>
    private static async Task EnqueuePrivateSpeakingNoShowSweepJobAsync(
        LearnerDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var alreadyQueued = await db.BackgroundJobs
            .AsNoTracking()
            .AnyAsync(j => j.Type == JobType.PrivateSpeakingNoShowSweep
                && (j.State == AsyncState.Queued || j.State == AsyncState.Processing),
                cancellationToken);
        if (alreadyQueued) return;

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"jb-ps-noshow-sweep-{Guid.NewGuid():N}",
            Type = JobType.PrivateSpeakingNoShowSweep,
            State = AsyncState.Queued,
            StatusReasonCode = "queued",
            StatusMessage = "Private Speaking no-show sweep queued.",
            CreatedAt = now,
            AvailableAt = now,
            LastTransitionAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Enqueue a single <see cref="JobType.PrivateSpeakingReminder"/> job, skipping
    /// if one is already queued/processing. Idempotent across multiple API replicas,
    /// mirroring <see cref="EnqueuePrivateSpeakingNoShowSweepJobAsync"/>.
    /// </summary>
    private static async Task EnqueuePrivateSpeakingReminderJobAsync(
        LearnerDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var alreadyQueued = await db.BackgroundJobs
            .AsNoTracking()
            .AnyAsync(j => j.Type == JobType.PrivateSpeakingReminder
                && (j.State == AsyncState.Queued || j.State == AsyncState.Processing),
                cancellationToken);
        if (alreadyQueued) return;

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"jb-ps-reminder-{Guid.NewGuid():N}",
            Type = JobType.PrivateSpeakingReminder,
            State = AsyncState.Queued,
            StatusReasonCode = "queued",
            StatusMessage = "Private Speaking reminder sweep queued.",
            CreatedAt = now,
            AvailableAt = now,
            LastTransitionAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Enqueue a single <see cref="JobType.PrivateSpeakingReservationExpiry"/> job,
    /// skipping if one is already queued/processing. Idempotent across multiple API
    /// replicas, mirroring <see cref="EnqueuePrivateSpeakingNoShowSweepJobAsync"/>.
    /// </summary>
    private static async Task EnqueuePrivateSpeakingReservationExpiryJobAsync(
        LearnerDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var alreadyQueued = await db.BackgroundJobs
            .AsNoTracking()
            .AnyAsync(j => j.Type == JobType.PrivateSpeakingReservationExpiry
                && (j.State == AsyncState.Queued || j.State == AsyncState.Processing),
                cancellationToken);
        if (alreadyQueued) return;

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"jb-ps-reservation-expiry-{Guid.NewGuid():N}",
            Type = JobType.PrivateSpeakingReservationExpiry,
            State = AsyncState.Queued,
            StatusReasonCode = "queued",
            StatusMessage = "Private Speaking reservation-expiry sweep queued.",
            CreatedAt = now,
            AvailableAt = now,
            LastTransitionAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task RunSpeakingTranscriptionQueueAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var pipeline = services.GetRequiredService<OetWithDrHesham.Api.Services.Speaking.SpeakingTranscriptionPipeline>();
        for (var processed = 0; processed < 5; processed += 1)
        {
            if (!await pipeline.ProcessNextAsync(cancellationToken))
            {
                break;
            }
        }
    }

    /// <summary>
    /// Wave A5 — claim pending dunning attempts whose <c>ScheduledAt</c> is in
    /// the past and enqueue one <see cref="JobType.BillingDunningRetry"/> per
    /// row. Uses a dedup guard on (ResourceId == DunningAttempt.Id) to avoid
    /// double-enqueueing if multiple API replicas tick at the same moment.
    /// </summary>
    private static async Task EnqueueDueBillingDunningRetriesAsync(
        LearnerDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await db.DunningAttempts
            .Where(a => a.Outcome == OetWithDrHesham.Api.Domain.DunningAttemptOutcome.Pending
                     && a.ScheduledAt <= now
                     && a.ExecutedAt == null)
            .OrderBy(a => a.ScheduledAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (due.Count == 0) return;

        var pendingAttemptIds = due.Select(a => a.Id).ToList();
        var alreadyQueued = await db.BackgroundJobs
            .Where(j => j.Type == JobType.BillingDunningRetry
                     && pendingAttemptIds.Contains(j.ResourceId!)
                     && (j.State == AsyncState.Queued || j.State == AsyncState.Processing))
            .Select(j => j.ResourceId)
            .ToListAsync(cancellationToken);
        var alreadyQueuedSet = new HashSet<string?>(alreadyQueued);

        foreach (var attempt in due)
        {
            if (alreadyQueuedSet.Contains(attempt.Id)) continue;
            db.BackgroundJobs.Add(new BackgroundJobItem
            {
                Id = $"jb-dunning-retry-{Guid.NewGuid():N}",
                Type = JobType.BillingDunningRetry,
                ResourceId = attempt.Id,
                State = AsyncState.Queued,
                StatusReasonCode = "queued",
                StatusMessage = "Billing dunning retry queued.",
                CreatedAt = now,
                AvailableAt = now,
                LastTransitionAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Wave A5 — schedule a single <see cref="JobType.BillingAbandonedCartEmail"/>
    /// sweep at 03:00 UTC each day. The "last enqueued" timestamp guards
    /// against duplicate sweeps within the same UTC date when the worker
    /// reschedules between ticks.
    /// </summary>
    private static async Task EnqueueAbandonedCartSweepJobAsync(
        LearnerDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = now.UtcDateTime.ToString("yyyy-MM-dd");
        var jobId = $"jb-cart-sweep-{today}";
        var existing = await db.BackgroundJobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => j.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null) return;

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = jobId,
            Type = JobType.BillingAbandonedCartEmail,
            State = AsyncState.Queued,
            StatusReasonCode = "queued",
            StatusMessage = "Billing abandoned-cart sweep queued.",
            CreatedAt = now,
            AvailableAt = now,
            LastTransitionAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// True when "now" is at or past 03:00 UTC on a UTC date we have not yet
    /// emitted a sweep job for. Encapsulated so unit tests can target it
    /// without booting the full processor.
    /// </summary>
    internal static bool ShouldEnqueueDailyAbandonedCartSweep(DateTimeOffset now, DateTimeOffset lastEnqueuedAt)
    {
        if (now.UtcDateTime.Hour < 3) return false;
        if (lastEnqueuedAt == DateTimeOffset.MinValue) return true;
        return lastEnqueuedAt.UtcDateTime.Date < now.UtcDateTime.Date;
    }

    internal static async Task RunReadinessRolloverAsync(IServiceProvider services, LearnerDbContext db, CancellationToken cancellationToken)
    {
        var staleCutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var recentActivityCutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var staleUserIds = await db.ReadinessSnapshots
            .Where(s => s.ComputedAt < staleCutoff)
            .Select(s => s.UserId)
            .Distinct()
            .Take(100)
            .ToListAsync(cancellationToken);

        if (staleUserIds.Count > 0)
        {
            var activeUserIds = await db.Attempts
                .Where(a => staleUserIds.Contains(a.UserId) && a.CompletedAt >= recentActivityCutoff)
                .Select(a => a.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var computation = services.GetRequiredService<OetWithDrHesham.Api.Services.Readiness.ReadinessComputationService>();
            foreach (var userId in activeUserIds)
            {
                try
                {
                    await computation.ComputeAsync(userId, cancellationToken);
                }
                catch (Exception)
                {
                    // swallow per-user failures so rollover continues
                }
            }
        }

        // Prune history beyond 26 weeks
        var pruneCutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-26 * 7));
        await PruneReadinessHistoryAsync(db, pruneCutoff, cancellationToken);
    }

    internal static async Task<int> PruneReadinessHistoryAsync(
        LearnerDbContext db,
        DateOnly pruneCutoff,
        CancellationToken cancellationToken)
    {
        var query = db.ReadinessHistories
            .Where(history => history.WeekStartDate < pruneCutoff);

        if (db.Database.IsRelational())
        {
            return await query.ExecuteDeleteAsync(cancellationToken);
        }

        // EF's in-memory provider does not support ExecuteDeleteAsync.
        var oldRows = await query.ToListAsync(cancellationToken);
        if (oldRows.Count == 0) return 0;

        db.ReadinessHistories.RemoveRange(oldRows);
        await db.SaveChangesAsync(cancellationToken);
        return oldRows.Count;
    }

    private static async Task ExecuteJobAsync(IServiceProvider services, LearnerDbContext db, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        var notifications = services.GetRequiredService<NotificationService>();
        switch (job.Type)
        {
            case JobType.WritingEvaluation:
                await services.GetRequiredService<OetWithDrHesham.Api.Services.Writing.IWritingEvaluationPipeline>()
                    .CompleteEvaluationAsync(job, cancellationToken);
                await CompleteWritingEvaluationSideEffectsAsync(services, db, notifications, job, cancellationToken);
                break;
            case JobType.SpeakingTranscription:
                await services.GetRequiredService<ISpeakingEvaluationPipeline>()
                    .CompleteTranscriptionAsync(job, cancellationToken);
                break;
            case JobType.SpeakingEvaluation:
                await services.GetRequiredService<ISpeakingEvaluationPipeline>()
                    .CompleteEvaluationAsync(job, cancellationToken);
                await CompleteSpeakingEvaluationSideEffectsAsync(services, db, notifications, job, cancellationToken);
                break;
            case JobType.StudyPlanRegeneration:
                await CompleteStudyPlanRegenerationAsync(services, db, notifications, job, cancellationToken);
                break;
            case JobType.MockReportGeneration:
                await services.GetRequiredService<OetWithDrHesham.Api.Services.Mocks.Results.IMockReportAggregationService>()
                    .GenerateAsync(job, cancellationToken);
                await CompleteMockReportSideEffectsAsync(services, db, notifications, job, cancellationToken);
                break;
            case JobType.ReviewCompletion:
                await CompleteReviewRequestAsync(services, db, notifications, job, cancellationToken);
                break;
            case JobType.FreezeStart:
                await CompleteFreezeStartAsync(db, notifications, job, cancellationToken);
                break;
            case JobType.FreezeEnd:
                await CompleteFreezeEndAsync(db, notifications, job, cancellationToken);
                break;
            case JobType.NotificationFanout:
                await notifications.ProcessFanoutAsync(job, cancellationToken);
                break;
            case JobType.NotificationDigestDispatch:
                await notifications.ProcessDigestDispatchAsync(job, cancellationToken);
                break;
            case JobType.ContentGeneration:
                await CompleteContentGenerationAsync(db, job, cancellationToken);
                break;
            case JobType.ConversationEvaluation:
                await CompleteConversationEvaluationAsync(services, db, job, cancellationToken);
                break;
            case JobType.PronunciationAnalysis:
                await CompletePronunciationAnalysisAsync(db, job, cancellationToken);
                break;
            case JobType.PrivateSpeakingZoomCreate:
            {
                var psSvc = services.GetRequiredService<PrivateSpeakingService>();
                await psSvc.CreateZoomMeetingForBookingAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.PrivateSpeakingBookingConfirmation:
            {
                var psSvc = services.GetRequiredService<PrivateSpeakingService>();
                await psSvc.SendBookingConfirmationNotificationsAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.PrivateSpeakingCalendarSync:
            {
                var calendarSvc = services.GetRequiredService<PrivateSpeakingCalendarService>();
                await calendarSvc.SyncBookingAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.PrivateSpeakingReminder:
            {
                var psSvc = services.GetRequiredService<PrivateSpeakingService>();
                await psSvc.ProcessRemindersAsync(cancellationToken);
                break;
            }
            case JobType.PrivateSpeakingReservationExpiry:
            {
                var psSvc = services.GetRequiredService<PrivateSpeakingService>();
                await psSvc.ExpireStaleReservationsAsync(cancellationToken);
                break;
            }
            case JobType.PrivateSpeakingNoShowSweep:
            {
                var psSvc = services.GetRequiredService<PrivateSpeakingService>();
                await psSvc.ProcessNoShowSweepAsync(cancellationToken);
                break;
            }
            case JobType.SubscriptionLifecycleCheck:
                await RunSubscriptionLifecycleCheckAsync(db, notifications, cancellationToken);
                break;
            case JobType.SlaAlertCheck:
                await RunSlaAlertCheckAsync(db, notifications, cancellationToken);
                break;
            case JobType.DripCampaignDispatch:
                await RunDripCampaignDispatchAsync(db, notifications, cancellationToken);
                break;
            case JobType.ExpertReviewAutoAssign:
                await services.GetRequiredService<OetWithDrHesham.Api.Services.Expert.IExpertAutoAssignmentService>()
                    .ProcessPendingAssignmentsAsync(cancellationToken);
                break;
            case JobType.ExpertReviewSlaEscalation:
                await services.GetRequiredService<OetWithDrHesham.Api.Services.Expert.IExpertAutoAssignmentService>()
                    .ProcessSlaEscalationsAsync(cancellationToken);
                break;

            // ── Live Classes ──
            case JobType.LiveClassRecordingDownload:
            {
                var svc = services.GetRequiredService<OetWithDrHesham.Api.Services.LiveClasses.LiveClassRecordingService>();
                await svc.ProcessDownloadAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.LiveClassRecordingTranscribe:
            {
                // Wave A2 — when AI processing is enabled, the processor service
                // calls Whisper (or reuses Zoom AI Companion's transcript) and
                // queues the Summarize stage. Flag-off ⇒ recording stays Pending.
                var processor = services.GetRequiredService<OetWithDrHesham.Api.Services.LiveClasses.LiveClassRecordingProcessingService>();
                await processor.ProcessTranscribeAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.LiveClassRecordingSummarize:
            {
                // Wave A2 — Sonnet-4.6 cached-prompt summarise → JSON
                // {summary, chapters, actionItems, keyTopics} and queue Translate.
                var processor = services.GetRequiredService<OetWithDrHesham.Api.Services.LiveClasses.LiveClassRecordingProcessingService>();
                await processor.ProcessSummarizeAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.LiveClassRecordingTranslate:
            {
                // Wave A2 — Sonnet-4.6 EN→AR translation, mark Ready, fan-out
                // learner "recording ready" notifications, queue Embed.
                var processor = services.GetRequiredService<OetWithDrHesham.Api.Services.LiveClasses.LiveClassRecordingProcessingService>();
                await processor.ProcessTranslateAsync(job.ResourceId!, cancellationToken);
                await NotifyLiveClassRecordingReadyAsync(services, db, job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.LiveClassRecordingEmbed:
            {
                // Wave A2 — chunk transcript + persist 1536-d vectors for the
                // "Ask AI about this class" RAG surface. Best-effort.
                var processor = services.GetRequiredService<OetWithDrHesham.Api.Services.LiveClasses.LiveClassRecordingProcessingService>();
                await processor.ProcessEmbedAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.LiveClassSessionReminderDispatch:
            {
                var svc = services.GetRequiredService<OetWithDrHesham.Api.Services.LiveClasses.LiveClassRecordingService>();
                await svc.ProcessReminderDispatchAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.LiveClassNoShowPingDispatch:
            {
                var svc = services.GetRequiredService<OetWithDrHesham.Api.Services.LiveClasses.LiveClassRecordingService>();
                await svc.ProcessNoShowPingAsync(job.ResourceId!, cancellationToken);
                break;
            }

            case JobType.LiveClassWaitlistPromotion:
                services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger<BackgroundJobProcessor>()
                    .LogInformation(
                        "Live class waitlist promotion job {JobId} — handled inline on cancellation.",
                        job.Id);
                break;

            // ── Wave A5 — Billing background jobs ──
            case JobType.BillingDunningRetry:
                await ExecuteBillingDunningRetryAsync(services, job, cancellationToken);
                break;
            case JobType.BillingAbandonedCartEmail:
                await ExecuteBillingAbandonedCartEmailAsync(services, cancellationToken);
                break;
            case JobType.BillingRenewalReminder:
                await ExecuteBillingRenewalReminderAsync(services, job, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Wave A5 — execute a single pending dunning attempt. The job payload
    /// carries the <c>DunningAttempt.Id</c> (also stored in
    /// <see cref="BackgroundJobItem.ResourceId"/> by the enqueuer).
    /// </summary>
    private static async Task ExecuteBillingDunningRetryAsync(
        IServiceProvider services, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;
        var dunning = services.GetRequiredService<OetWithDrHesham.Api.Services.Billing.IDunningService>();
        await dunning.ExecutePendingRetryAsync(job.ResourceId, cancellationToken);
    }

    /// <summary>
    /// Wave A5 — daily 03:00 UTC sweep that emails carts idle &gt; 24h.
    /// </summary>
    private static async Task ExecuteBillingAbandonedCartEmailAsync(
        IServiceProvider services, CancellationToken cancellationToken)
    {
        var svc = services.GetRequiredService<OetWithDrHesham.Api.Services.Billing.IAbandonedCartRecoveryService>();
        await svc.SweepAsync(cancellationToken);
    }

    /// <summary>
    /// Wave A5 — 3-day renewal reminder. Wave A4's <c>invoice.upcoming</c>
    /// webhook enqueues the job with payload
    /// <c>{ userId, subscriptionId, renewsAt, amount, currency }</c>.
    /// </summary>
    private static async Task ExecuteBillingRenewalReminderAsync(
        IServiceProvider services, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.PayloadJson)) return;
        BillingRenewalReminderPayload? payload;
        try
        {
            payload = System.Text.Json.JsonSerializer.Deserialize<BillingRenewalReminderPayload>(job.PayloadJson);
        }
        catch
        {
            return;
        }
        if (payload is null || string.IsNullOrWhiteSpace(payload.UserId) || string.IsNullOrWhiteSpace(payload.SubscriptionId))
            return;

        var dispatcher = services.GetRequiredService<OetWithDrHesham.Api.Services.Billing.IBillingNotificationDispatcher>();
        await OetWithDrHesham.Api.Services.Billing.BillingDunningNotifications.SendRenewalReminderAsync(
            dispatcher,
            userId: payload.UserId,
            stripeSubscriptionId: payload.SubscriptionId,
            renewsAt: payload.RenewsAt,
            amount: payload.Amount ?? string.Empty,
            currency: payload.Currency ?? string.Empty,
            cancellationToken);
    }

    private sealed class BillingRenewalReminderPayload
    {
        public string? UserId { get; set; }
        public string? SubscriptionId { get; set; }
        public DateTimeOffset RenewsAt { get; set; }
        public string? Amount { get; set; }
        public string? Currency { get; set; }
    }

    private static async Task CompleteFreezeStartAsync(LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId))
        {
            return;
        }

        var record = await db.AccountFreezeRecords.FirstOrDefaultAsync(x => x.Id == job.ResourceId, cancellationToken);
        if (record is null)
        {
            return;
        }

        if (record.Status is FreezeStatus.Active or FreezeStatus.Completed or FreezeStatus.ForceEnded or FreezeStatus.Cancelled or FreezeStatus.Rejected)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (record.ScheduledStartAt is not null && record.ScheduledStartAt > now)
        {
            return;
        }

        record.Status = FreezeStatus.Active;
        record.IsCurrent = true;
        record.StartedAt ??= record.ScheduledStartAt ?? now;
        record.UpdatedAt = now;
        await ConsumeFreezeEntitlementOnStartAsync(db, record, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerFreezeStarted,
            record.UserId,
            nameof(AccountFreezeRecord),
            record.Id,
            record.PolicyVersionSnapshot.ToString(),
            new Dictionary<string, object?>
            {
                ["freezeId"] = record.Id,
                ["message"] = "Your freeze is now active."
            },
            cancellationToken);
    }

    private static async Task CompleteFreezeEndAsync(LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId))
        {
            return;
        }

        var record = await db.AccountFreezeRecords.FirstOrDefaultAsync(x => x.Id == job.ResourceId, cancellationToken);
        if (record is null)
        {
            return;
        }

        if (record.Status is FreezeStatus.Completed or FreezeStatus.ForceEnded or FreezeStatus.Cancelled or FreezeStatus.Rejected)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (record.EndedAt is not null && record.EndedAt > now)
        {
            return;
        }

        record.Status = FreezeStatus.Completed;
        record.IsCurrent = false;
        record.StartedAt ??= record.ScheduledStartAt ?? now;
        record.EndedAt ??= now;
        record.EndReason ??= "Freeze period ended";
        record.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerFreezeEnded,
            record.UserId,
            nameof(AccountFreezeRecord),
            record.Id,
            record.PolicyVersionSnapshot.ToString(),
            new Dictionary<string, object?>
            {
                ["freezeId"] = record.Id,
                ["message"] = "Your freeze period has ended."
            },
            cancellationToken);
    }

    private static async Task ReconcileFreezeLifecycleAsync(IServiceProvider services, LearnerDbContext db, CancellationToken cancellationToken)
    {
        var notifications = services.GetRequiredService<NotificationService>();
        var now = DateTimeOffset.UtcNow;
        var recordsQuery = db.AccountFreezeRecords
            .Where(x => x.Status == FreezeStatus.Scheduled || x.Status == FreezeStatus.Active);

        var records = db.Database.IsSqlite()
            ? (await recordsQuery.ToListAsync(cancellationToken))
                .OrderBy(x => x.ScheduledStartAt)
                .ToList()
            : await recordsQuery
                .OrderBy(x => x.ScheduledStartAt)
                .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var record in records)
        {
            if (record.Status == FreezeStatus.Scheduled && record.ScheduledStartAt is not null && record.ScheduledStartAt <= now)
            {
                record.Status = FreezeStatus.Active;
                record.StartedAt ??= record.ScheduledStartAt ?? now;
                record.UpdatedAt = now;
                changed = true;
                await ConsumeFreezeEntitlementOnStartAsync(db, record, now, cancellationToken);

                await notifications.CreateForLearnerAsync(
                    NotificationEventKey.LearnerFreezeStarted,
                    record.UserId,
                    nameof(AccountFreezeRecord),
                    record.Id,
                    record.PolicyVersionSnapshot.ToString(),
                    new Dictionary<string, object?>
                    {
                        ["freezeId"] = record.Id,
                        ["message"] = "Your freeze is now active."
                    },
                    cancellationToken);
            }

            if (record.Status == FreezeStatus.Active && record.EndedAt is not null && record.EndedAt <= now)
            {
                record.Status = FreezeStatus.Completed;
                record.IsCurrent = false;
                record.StartedAt ??= record.ScheduledStartAt ?? now;
                record.EndedAt ??= now;
                record.EndReason ??= "Freeze period ended";
                record.UpdatedAt = now;
                changed = true;

                await notifications.CreateForLearnerAsync(
                    NotificationEventKey.LearnerFreezeEnded,
                    record.UserId,
                    nameof(AccountFreezeRecord),
                    record.Id,
                    record.PolicyVersionSnapshot.ToString(),
                    new Dictionary<string, object?>
                    {
                        ["freezeId"] = record.Id,
                        ["message"] = "Your freeze period has ended."
                    },
                    cancellationToken);
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task ConsumeFreezeEntitlementOnStartAsync(LearnerDbContext db, AccountFreezeRecord record, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!record.IsSelfService || record.EntitlementConsumed)
        {
            return;
        }

        var entitlement = await db.AccountFreezeEntitlements.FirstOrDefaultAsync(x => x.UserId == record.UserId, cancellationToken);
        if (entitlement is null)
        {
            db.AccountFreezeEntitlements.Add(new AccountFreezeEntitlement
            {
                Id = $"FZE-{Guid.NewGuid():N}",
                UserId = record.UserId,
                FreezeRecordId = record.Id,
                ConsumedAt = now,
                ResetAt = null
            });
        }
        else
        {
            entitlement.FreezeRecordId = record.Id;
            entitlement.ConsumedAt = now;
            entitlement.ResetAt = null;
            entitlement.ResetByAdminId = null;
            entitlement.ResetByAdminName = null;
            entitlement.ResetReason = null;
        }

        record.EntitlementConsumed = true;
    }

    private static async Task CompleteWritingEvaluationSideEffectsAsync(IServiceProvider services, LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.AttemptId)) return;

        var attempt = await db.Attempts.FirstAsync(x => x.Id == job.AttemptId, cancellationToken);
        var evaluation = await db.Evaluations.FirstAsync(x => x.AttemptId == attempt.Id, cancellationToken);

        // Only emit success-side analytics + notifications when the
        // pipeline actually completed grading. On Failed evaluations the
        // pipeline preserves rule-engine findings and surfaces the failure
        // through evaluation.StatusReasonCode; the evaluation_failed-style
        // notification path lives elsewhere (job retry / SLA worker).
        if (evaluation.State != AsyncState.Completed)
        {
            return;
        }

        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = attempt.UserId,
            EventName = "evaluation_completed",
            PayloadJson = JsonSupport.Serialize(new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest = "writing" }),
            OccurredAt = DateTimeOffset.UtcNow
        });

        var readiness = await RefreshReadinessAsync(services, db, attempt.UserId, cancellationToken);
        await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Completed, cancellationToken);
        await LearnerWorkflowCoordinator.QueueStudyPlanRegenerationAsync(db, attempt.UserId, cancellationToken);
        var evaluationVersion = (evaluation.GeneratedAt ?? DateTimeOffset.UtcNow).UtcDateTime.Ticks.ToString();
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerEvaluationCompleted,
            attempt.UserId,
            "attempt",
            attempt.Id,
            evaluationVersion,
            new Dictionary<string, object?>
            {
                ["attemptId"] = attempt.Id,
                ["subtest"] = "writing"
            },
            cancellationToken);
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerReadinessUpdated,
            attempt.UserId,
            "readiness_snapshot",
            readiness.Id,
            readiness.Version.ToString(),
            new Dictionary<string, object?>
            {
                ["message"] = "Your readiness snapshot was recalculated after the latest writing evaluation."
            },
            cancellationToken);
    }

    private static async Task CompleteSpeakingEvaluationSideEffectsAsync(IServiceProvider services, LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.AttemptId)) return;

        var attempt = await db.Attempts.FirstAsync(x => x.Id == job.AttemptId, cancellationToken);
        var evaluation = await db.Evaluations.FirstAsync(x => x.AttemptId == attempt.Id, cancellationToken);

        if (evaluation.State != AsyncState.Completed)
        {
            var failureVersion = evaluation.LastTransitionAt.UtcDateTime.Ticks.ToString();
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerEvaluationFailed,
                attempt.UserId,
                "attempt",
                attempt.Id,
                failureVersion,
                new Dictionary<string, object?>
                {
                    ["attemptId"] = attempt.Id,
                    ["subtest"] = "speaking",
                    ["message"] = evaluation.StatusMessage ?? "We could not finish your speaking evaluation automatically. Please try again shortly."
                },
                cancellationToken);
            return;
        }

        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = attempt.UserId,
            EventName = "evaluation_completed",
            PayloadJson = JsonSupport.Serialize(new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest = "speaking" }),
            OccurredAt = DateTimeOffset.UtcNow
        });

        var readiness = await RefreshReadinessAsync(services, db, attempt.UserId, cancellationToken);
        await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Completed, cancellationToken);
        await LearnerWorkflowCoordinator.QueueStudyPlanRegenerationAsync(db, attempt.UserId, cancellationToken);
        var evaluationVersion = (evaluation.GeneratedAt ?? DateTimeOffset.UtcNow).UtcDateTime.Ticks.ToString();
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerEvaluationCompleted,
            attempt.UserId,
            "attempt",
            attempt.Id,
            evaluationVersion,
            new Dictionary<string, object?>
            {
                ["attemptId"] = attempt.Id,
                ["subtest"] = "speaking"
            },
            cancellationToken);
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerReadinessUpdated,
            attempt.UserId,
            "readiness_snapshot",
            readiness.Id,
            readiness.Version.ToString(),
            new Dictionary<string, object?>
            {
                ["message"] = "Your readiness snapshot was recalculated after the latest speaking evaluation."
            },
            cancellationToken);
    }

    private static async Task DiscardPendingJobSideEffectsAsync(LearnerDbContext db, string currentJobId, CancellationToken cancellationToken)
    {
        foreach (var entry in db.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is BackgroundJobItem backgroundJob)
            {
                if (backgroundJob.Id == currentJobId)
                {
                    await entry.ReloadAsync(cancellationToken);
                }

                continue;
            }

            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
                continue;
            }

            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                try
                {
                    await entry.ReloadAsync(cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    entry.State = EntityState.Detached;
                }
            }
        }
    }

    private static async Task CompleteStudyPlanRegenerationAsync(
        IServiceProvider services,
        LearnerDbContext db,
        NotificationService notifications,
        BackgroundJobItem job,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;
        var priorPlan = await db.StudyPlans.FirstOrDefaultAsync(x => x.Id == job.ResourceId, cancellationToken);
        if (priorPlan is null) return;

        var generator = services.GetRequiredService<OetWithDrHesham.Api.Services.Planner.IStudyPlanGenerator>();
        var trigger = ResolveTrigger(job);
        var result = await generator.GenerateAsync(priorPlan.UserId, trigger, cancellationToken);
        job.ResourceId = result.PlanId;

        if (result.SkippedBecauseUnchanged)
        {
            // Mark prior plan back to Completed; nothing materially changed.
            priorPlan.State = AsyncState.Completed;
            return;
        }

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerStudyPlanRegenerated,
            priorPlan.UserId,
            "study_plan",
            result.PlanId,
            result.Version.ToString(),
            new Dictionary<string, object?>
            {
                ["message"] = $"Your study plan has been refreshed (v{result.Version}, {result.ItemsCreated} new tasks).",
                ["templateId"] = result.TemplateId,
                ["trigger"] = trigger.ToString()
            },
            cancellationToken);

        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = priorPlan.UserId,
            EventName = "study_plan_generated",
            PayloadJson = JsonSupport.Serialize(new
            {
                planId = result.PlanId,
                version = result.Version,
                itemsCreated = result.ItemsCreated,
                itemsPreserved = result.ItemsPreservedFromPrior,
                templateId = result.TemplateId,
                trigger = trigger.ToString()
            }),
            OccurredAt = DateTimeOffset.UtcNow
        });
    }

    private static OetWithDrHesham.Api.Services.Planner.StudyPlanGenerationTrigger ResolveTrigger(BackgroundJobItem job)
    {
        if (string.IsNullOrWhiteSpace(job.PayloadJson))
        {
            return OetWithDrHesham.Api.Services.Planner.StudyPlanGenerationTrigger.Manual;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(job.PayloadJson);
            if (doc.RootElement.TryGetProperty("trigger", out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                if (Enum.TryParse<OetWithDrHesham.Api.Services.Planner.StudyPlanGenerationTrigger>(prop.GetString(), ignoreCase: true, out var parsed))
                {
                    return parsed;
                }
            }
        }
        catch
        {
            // fall through
        }

        return OetWithDrHesham.Api.Services.Planner.StudyPlanGenerationTrigger.Manual;
    }

    private static async Task CompleteMockReportGenerationAsync(LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;
        var mockAttempt = await db.MockAttempts.FirstAsync(x => x.Id == job.ResourceId, cancellationToken);
        var report = await db.MockReports.FirstOrDefaultAsync(x => x.MockAttemptId == mockAttempt.Id, cancellationToken);
        if (report is null)
        {
            report = new MockReport
            {
                Id = $"mock-report-{Guid.NewGuid():N}",
                MockAttemptId = mockAttempt.Id
            };
            db.MockReports.Add(report);
        }

        var sections = await db.MockSectionAttempts.AsNoTracking()
            .Where(x => x.MockAttemptId == mockAttempt.Id)
            .Join(db.MockBundleSections.AsNoTracking().Include(x => x.ContentPaper),
                sectionAttempt => sectionAttempt.MockBundleSectionId,
                bundleSection => bundleSection.Id,
                (sectionAttempt, bundleSection) => new { sectionAttempt, bundleSection })
            .OrderBy(x => x.bundleSection.SectionOrder)
            .ToListAsync(cancellationToken);

        var reviewAttemptIds = sections
            .Select(x => x.sectionAttempt.ContentAttemptId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();
        List<ReviewRequest> reviewRequests = reviewAttemptIds.Count == 0
            ? []
            : await db.ReviewRequests.AsNoTracking()
                .Where(x => reviewAttemptIds.Contains(x.AttemptId))
                .ToListAsync(cancellationToken);

        var subTests = sections.Select(row =>
        {
            var section = row.sectionAttempt;
            var subtest = section.SubtestCode;
            var scaled = ResolveMockScaledScore(section);
            var review = reviewRequests.FirstOrDefault(x => x.AttemptId == section.ContentAttemptId);
            var state = section.State == AttemptState.Completed
                ? review is null ? "completed" : ReviewStateForReport(review.State)
                : "not_completed";
            return new
            {
                id = subtest.Trim().ToLowerInvariant(),
                name = ToDisplaySubtest(subtest),
                score = scaled?.ToString() ?? (state is "queued" or "in_review" ? "Pending review" : "Pending"),
                rawScore = FormatMockRawScore(section),
                scaledScore = scaled,
                grade = scaled is null ? null : OetScoring.OetGradeLetterFromScaled(scaled.Value),
                state,
                contentPaperTitle = row.bundleSection.ContentPaper?.Title,
                reviewRequestId = review?.Id,
                reviewState = review is null ? null : ReviewStateForReport(review.State)
            };
        }).ToList();

        var availableScores = subTests
            .Where(x => x.scaledScore.HasValue)
            .Select(x => x.scaledScore!.Value)
            .ToList();
        var overall = availableScores.Count == 0
            ? (int?)null
            : (int)Math.Round(availableScores.Average(), MidpointRounding.AwayFromZero);
        var weakest = subTests
            .Where(x => x.scaledScore.HasValue)
            .OrderBy(x => x.scaledScore)
            .FirstOrDefault();

        var previousReports = await db.MockReports.AsNoTracking()
            .Join(db.MockAttempts.AsNoTracking().Where(x => x.UserId == mockAttempt.UserId && x.Id != mockAttempt.Id),
                report => report.MockAttemptId,
                attempt => attempt.Id,
                (report, attempt) => report)
            .Where(x => x.State == AsyncState.Completed && x.GeneratedAt != null)
            .OrderByDescending(x => x.GeneratedAt)
            .Take(1)
            .ToListAsync(cancellationToken);
        var priorOverall = previousReports
            .Select(x => TryReadOverallScore(x.PayloadJson))
            .FirstOrDefault(x => x.HasValue);

        var proctoringEvents = await db.MockProctoringEvents.AsNoTracking()
            .Where(x => x.MockAttemptId == mockAttempt.Id)
            .ToListAsync(cancellationToken);
        var perModuleReadiness = subTests.Select(x =>
        {
            var advisory = x.scaledScore.HasValue ? OetScoring.AdvisoryTier(x.scaledScore.Value) : null;
            return new
            {
                subtest = x.name,
                scaledScore = x.scaledScore,
                grade = x.grade,
                rag = advisory?.Tier ?? "pending",
                message = advisory?.Message ?? "Awaiting scored evidence or teacher review.",
                passThreshold = advisory?.PassThreshold
            };
        }).ToArray();
        var timingAnalysis = sections.Select(x => new
        {
            sectionId = x.sectionAttempt.Id,
            subtest = x.sectionAttempt.SubtestCode,
            startedAt = x.sectionAttempt.StartedAt,
            submittedAt = x.sectionAttempt.SubmittedAt,
            completedAt = x.sectionAttempt.CompletedAt,
            deadlineAt = x.sectionAttempt.DeadlineAt,
            secondsUsed = x.sectionAttempt.StartedAt is not null && (x.sectionAttempt.CompletedAt ?? x.sectionAttempt.SubmittedAt) is not null
                ? (int?)Math.Max(0, (int)((x.sectionAttempt.CompletedAt ?? x.sectionAttempt.SubmittedAt)!.Value - x.sectionAttempt.StartedAt.Value).TotalSeconds)
                : null
        }).ToArray();
        var weakestCriterion = weakest is null
            ? new { subtest = "Pending", criterion = "Awaiting evidence", description = "Complete scored sections or wait for expert-reviewed productive sections." }
            : new { subtest = weakest.name, criterion = "Lowest scaled sub-test", description = $"Prioritise {weakest.name} next; current scaled score is {weakest.scaledScore}/500." };
        var config = JsonSupport.Deserialize<Dictionary<string, object?>>(mockAttempt.ConfigJson, new Dictionary<string, object?>());
        static string? ReadString(Dictionary<string, object?> values, string key)
            => values.TryGetValue(key, out var value) && value is not null && !string.IsNullOrWhiteSpace(value.ToString())
                ? value.ToString()
                : null;

        report.State = AsyncState.Completed;
        report.GeneratedAt = DateTimeOffset.UtcNow;
        report.PayloadJson = JsonSupport.Serialize(new
        {
            id = report.Id,
            reportId = report.Id,
            mockAttemptId = mockAttempt.Id,
            title = mockAttempt.MockType == "sub" ? $"{ToDisplaySubtest(mockAttempt.SubtestCode ?? "mock")} Mock Report" : "Generated OET Mock Report",
            date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            profession = ReadString(config, "profession") ?? mockAttempt.Profession,
            targetCountry = ReadString(config, "targetCountry"),
            deliveryMode = ReadString(config, "deliveryMode") ?? mockAttempt.DeliveryMode,
            strictness = ReadString(config, "strictness") ?? mockAttempt.Strictness,
            releasePolicy = ReadString(config, "releasePolicy"),
            overallScore = overall?.ToString() ?? "Pending",
            overallGrade = overall is null ? null : OetScoring.OetGradeLetterFromScaled(overall.Value),
            summary = BuildMockReportSummary(overall, subTests.Count(x => x.state is "queued" or "in_review")),
            subTests,
            weakestCriterion,
            reviewSummary = new
            {
                queued = reviewRequests.Count(x => x.State == ReviewRequestState.Queued),
                inReview = reviewRequests.Count(x => x.State == ReviewRequestState.InReview),
                completed = reviewRequests.Count(x => x.State == ReviewRequestState.Completed),
                pending = reviewRequests.Count(x => x.State is ReviewRequestState.Queued or ReviewRequestState.InReview or ReviewRequestState.AwaitingPayment)
            },
            perModuleReadiness,
            partScores = subTests.Select(x => new { subtest = x.name, x.rawScore, x.scaledScore, x.grade, x.state }).ToArray(),
            timingAnalysis,
            errorCategories = new[]
            {
                new
                {
                    category = weakestCriterion.criterion,
                    subtest = weakestCriterion.subtest,
                    severity = "priority",
                    description = weakestCriterion.description
                }
            },
            teacherReviewState = new
            {
                queued = reviewRequests.Count(x => x.State == ReviewRequestState.Queued),
                inReview = reviewRequests.Count(x => x.State == ReviewRequestState.InReview),
                completed = reviewRequests.Count(x => x.State == ReviewRequestState.Completed),
                pending = reviewRequests.Count(x => x.State is ReviewRequestState.Queued or ReviewRequestState.InReview or ReviewRequestState.AwaitingPayment)
            },
            bookingAdvice = BuildMockBookingAdvice(overall),
            retakeAdvice = new
            {
                recommendedWindowDays = 7,
                nextMockType = "sub",
                subtest = weakestCriterion.subtest,
                message = $"Retake a targeted {weakestCriterion.subtest} mock after completing the 7-day remediation plan."
            },
            proctoringSummary = new
            {
                totalEvents = proctoringEvents.Count,
                advisoryOnly = true,
                criticalEvents = proctoringEvents.Count(x => x.Severity == "critical"),
                warningEvents = proctoringEvents.Count(x => x.Severity == "warning"),
                byKind = proctoringEvents.GroupBy(x => x.Kind).Select(g => new { kind = g.Key, count = g.Count() }).ToArray(),
                message = proctoringEvents.Count == 0
                    ? "No integrity events were recorded. Proctoring is advisory and never blocks submission automatically."
                    : "Integrity events were recorded for teacher/admin review. They are advisory and did not block submission."
            },
            remediationPlan = BuildMockRemediationPlan(report.Id, weakestCriterion.subtest, weakestCriterion.criterion, weakestCriterion.description),
            priorComparison = priorOverall.HasValue && overall.HasValue
                ? new
                {
                    exists = true,
                    priorMockName = "Previous mock",
                    overallTrend = overall.Value > priorOverall.Value ? "up" : overall.Value < priorOverall.Value ? "down" : "flat",
                    details = $"Overall score changed by {overall.Value - priorOverall.Value:+#;-#;0} points since the previous mock."
                }
                : new { exists = false, priorMockName = string.Empty, overallTrend = "flat", details = "No earlier generated mock report is available for comparison." }
        });

        mockAttempt.ReportId = report.Id;
        mockAttempt.State = AttemptState.Completed;
        mockAttempt.CompletedAt = DateTimeOffset.UtcNow;
        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = mockAttempt.UserId,
            EventName = "mock_completed",
            PayloadJson = JsonSupport.Serialize(new { mockAttemptId = mockAttempt.Id, reportId = report.Id, overallScore = overall }),
            OccurredAt = DateTimeOffset.UtcNow
        });

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerMockReportReady,
            mockAttempt.UserId,
            "mock_attempt",
            mockAttempt.Id,
            report.Id,
            new Dictionary<string, object?>
            {
                ["mockAttemptId"] = mockAttempt.Id
            },
            cancellationToken);
    }

    private static int? ResolveMockScaledScore(MockSectionAttempt section)
    {
        if (section.ScaledScore.HasValue) return section.ScaledScore.Value;
        if (section.SubtestCode is "reading" or "listening" && section.RawScore.HasValue)
        {
            return OetScoring.OetRawToScaled(section.RawScore.Value);
        }

        return null;
    }

    private static string FormatMockRawScore(MockSectionAttempt section)
    {
        if (section.RawScore.HasValue && section.RawScoreMax.HasValue)
        {
            return $"{section.RawScore}/{section.RawScoreMax}";
        }

        if (section.RawScore.HasValue && (section.SubtestCode is "reading" or "listening"))
        {
            return $"{section.RawScore}/42";
        }

        return "N/A";
    }

    private static string ReviewStateForReport(ReviewRequestState state) => state switch
    {
        ReviewRequestState.Queued => "queued",
        ReviewRequestState.InReview => "in_review",
        ReviewRequestState.Completed => "completed",
        ReviewRequestState.Failed => "failed",
        ReviewRequestState.Cancelled => "cancelled",
        ReviewRequestState.AwaitingPayment => "awaiting_payment",
        _ => state.ToString().ToLowerInvariant()
    };

    private static int? TryReadOverallScore(string payloadJson)
    {
        var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(payloadJson, new Dictionary<string, object?>());
        if (!payload.TryGetValue("overallScore", out var value) || value is null)
        {
            return null;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static string BuildMockReportSummary(int? overall, int pendingReviews)
    {
        if (pendingReviews > 0)
        {
            return $"The report is generated from available section evidence with {pendingReviews} expert-reviewed section(s) still pending.";
        }

        return overall.HasValue
            ? $"The advisory overall mock score is {overall}/500, calculated as the rounded mean of available sub-test scaled scores."
            : "The report is waiting for scored section evidence before calculating an advisory overall score.";
    }

    private static object BuildMockBookingAdvice(int? overall)
    {
        if (!overall.HasValue)
        {
            return new { status = "pending", message = "Wait for scored sections and teacher review before booking the official OET.", route = "/mocks/setup" };
        }

        var advisory = OetScoring.AdvisoryTier(overall.Value);
        return new
        {
            status = advisory.Tier,
            score = overall.Value,
            message = advisory.Tier is "green" or "dark-green"
                ? "Use at least two consistent green mocks before booking the official OET."
                : "Complete remediation and retake a strict mock before booking.",
            route = "/exam-booking"
        };
    }

    private static object[] BuildMockRemediationPlan(string reportId, string subtest, string criterion, string description)
    {
        var normalized = string.IsNullOrWhiteSpace(subtest) ? "reading" : subtest.ToLowerInvariant();
        var route = normalized switch
        {
            var s when s.Contains("listening", StringComparison.OrdinalIgnoreCase) => "/listening",
            var s when s.Contains("reading", StringComparison.OrdinalIgnoreCase) => "/reading/practice",
            var s when s.Contains("writing", StringComparison.OrdinalIgnoreCase) => "/writing/practice/library",
            var s when s.Contains("speaking", StringComparison.OrdinalIgnoreCase) => "/speaking/selection",
            _ => "/practice"
        };

        return
        [
            new { day = "Day 1", title = "Review every lost mark", description = "Compare answer review, timing notes, and teacher comments before attempting new work.", route = $"/mocks/report/{reportId}" },
            new { day = "Day 2", title = $"Repair {criterion}", description, route },
            new { day = "Day 3", title = "Complete a targeted micro-drill", description = $"Focus on {subtest} without full-exam pressure first.", route },
            new { day = "Day 4", title = "Attempt a sectional mock", description = "Check whether the repair transfers under timed conditions.", route = $"/mocks/setup?type=sub&subtest={Uri.EscapeDataString(normalized)}" },
            new { day = "Day 5-7", title = "Book tutor review or retake", description = "If Writing or Speaking is involved, request tutor feedback before another readiness mock.", route = "/mocks/setup" }
        ];
    }

    private static string ToDisplaySubtest(string subtest)
        => string.IsNullOrWhiteSpace(subtest) ? "Mock" : char.ToUpperInvariant(subtest[0]) + subtest[1..];

    private static async Task CompleteReviewRequestAsync(IServiceProvider services, LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;
        var request = await db.ReviewRequests.FirstAsync(x => x.Id == job.ResourceId, cancellationToken);
        if (request.State != ReviewRequestState.Completed || request.CompletedAt is null)
        {
            job.StatusReasonCode = "review_completion_not_ready";
            job.StatusMessage = "Review completion fan-out skipped because the review has not been completed by an expert yet.";
            return;
        }

        var attempt = await db.Attempts.FirstOrDefaultAsync(x => x.Id == request.AttemptId, cancellationToken);
        if (attempt is not null)
        {
            db.AnalyticsEvents.Add(new AnalyticsEventRecord
            {
                Id = $"evt-{Guid.NewGuid():N}",
                UserId = attempt.UserId,
                EventName = "review_completed",
                PayloadJson = JsonSupport.Serialize(new { reviewRequestId = request.Id, attemptId = request.AttemptId, subtest = request.SubtestCode }),
                OccurredAt = DateTimeOffset.UtcNow
            });

            // Trigger study plan regeneration after tutor review completes
            var readiness = await RefreshReadinessAsync(services, db, attempt.UserId, cancellationToken);
            await LearnerWorkflowCoordinator.QueueStudyPlanRegenerationAsync(db, attempt.UserId, cancellationToken);
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerReadinessUpdated,
                attempt.UserId,
                "readiness_snapshot",
                readiness.Id,
                readiness.Version.ToString(),
                new Dictionary<string, object?>
                {
                    ["message"] = "Your readiness snapshot was updated after tutor review feedback was applied."
                },
                cancellationToken);
        }
    }

    private static async Task<ReadinessSnapshot> RefreshReadinessAsync(IServiceProvider services, LearnerDbContext db, string userId, CancellationToken cancellationToken)
    {
        var computation = services.GetRequiredService<OetWithDrHesham.Api.Services.Readiness.ReadinessComputationService>();
        return await computation.ComputeAsync(userId, cancellationToken);
    }

    private static async Task CompleteMockReportSideEffectsAsync(IServiceProvider services, LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;
        var mockAttempt = await db.MockAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == job.ResourceId, cancellationToken);
        if (mockAttempt is null) return;

        var readiness = await RefreshReadinessAsync(services, db, mockAttempt.UserId, cancellationToken);
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerReadinessUpdated,
            mockAttempt.UserId,
            "readiness_snapshot",
            readiness.Id,
            readiness.Version.ToString(),
            new Dictionary<string, object?>
            {
                ["message"] = "Your readiness snapshot was recalculated after your mock report finished generating."
            },
            cancellationToken);
    }

    /// <summary>
    /// Recovers jobs orphaned in <see cref="AsyncState.Processing"/> — typically
    /// by a container restart (blue/green deploy) that killed the worker mid-job.
    /// Without this they hang forever: learners keep polling a result that will
    /// never arrive and /health/ready reports a growing stuck_jobs warning.
    /// Policy mirrors an in-process failure: recent orphans spend a retry and
    /// re-queue with backoff; orphans older than <see cref="StuckJobRetryMaxAge"/>
    /// fail terminally without learner notifications (a days-late evaluation
    /// result or reminder is more confusing than helpful).
    /// </summary>
    internal async Task RecoverStuckJobsAsync(IServiceProvider services, LearnerDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var staleBefore = now - StuckJobStaleThreshold;

        List<BackgroundJobItem> stuckJobs;
        try
        {
            stuckJobs = await db.BackgroundJobs
                .Where(x => x.State == AsyncState.Processing && x.LastTransitionAt < staleBefore)
                .OrderBy(x => x.LastTransitionAt)
                .Take(200)
                .ToListAsync(cancellationToken);
        }
        catch (InvalidOperationException) when (db.Database.IsSqlite())
        {
            // SQLite cannot translate the DateTimeOffset comparison; narrow on
            // the state predicate and filter in memory (same workaround as the
            // /health/ready stuck-jobs check and SubscriptionExpiryWorker).
            var candidates = await db.BackgroundJobs
                .Where(x => x.State == AsyncState.Processing)
                .ToListAsync(cancellationToken);
            stuckJobs = candidates
                .Where(x => x.LastTransitionAt < staleBefore)
                .OrderBy(x => x.LastTransitionAt)
                .Take(200)
                .ToList();
        }

        if (stuckJobs.Count == 0)
        {
            return;
        }

        const int maxRetries = 3;
        var requeued = 0;
        var failed = 0;
        foreach (var job in stuckJobs)
        {
            var stuckFor = now - job.LastTransitionAt;
            logger.LogWarning(
                "Job {JobId} of type {JobType} was orphaned in Processing for {StuckMinutes:F0} minutes; recovering (attempt {Attempt})",
                job.Id, job.Type, stuckFor.TotalMinutes, job.RetryCount + 1);

            await DiscardPendingJobSideEffectsAsync(db, job.Id, cancellationToken);
            var tooOldToRetry = stuckFor > StuckJobRetryMaxAge;
            job.LastTransitionAt = now;

            if (!tooOldToRetry && job.Retryable && job.RetryCount + 1 < maxRetries)
            {
                job.RetryCount += 1;
                var delayMs = (int)Math.Pow(2, job.RetryCount) * 5000;
                job.State = AsyncState.Queued;
                job.AvailableAt = now.AddMilliseconds(delayMs);
                job.StatusReasonCode = "stale_processing_requeued";
                job.StatusMessage = $"Processing was interrupted (app restart); retry {job.RetryCount}/{maxRetries}.";
                job.RetryAfterMs = delayMs;
                requeued += 1;
                continue;
            }

            job.State = AsyncState.Failed;
            job.RetryAfterMs = 0;
            var failure = new InvalidOperationException("Background job processing was interrupted and never completed.");
            if (tooOldToRetry)
            {
                job.StatusReasonCode = "stale_processing_expired";
                job.StatusMessage = $"Processing was interrupted and the job sat unrecovered for {stuckFor.TotalHours:F0} hours; failed without retry.";
                await MarkResourceFailedAfterFinalRetryAsync(db, job, failure, cancellationToken);
            }
            else
            {
                job.RetryCount += 1;
                job.StatusReasonCode = "stale_processing";
                job.StatusMessage = $"Processing was interrupted (app restart) and retries are exhausted ({job.RetryCount}/{maxRetries}).";
                await MarkResourceFailedAfterFinalRetryAsync(db, job, failure, cancellationToken);
                await EmitFailureNotificationsAsync(services, db, job, failure, cancellationToken);
            }
            failed += 1;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Stuck-job recovery: re-queued {Requeued} and failed {Failed} of {Total} orphaned Processing jobs.",
            requeued, failed, stuckJobs.Count);
    }

    private static async Task EmitFailureNotificationsAsync(IServiceProvider services, LearnerDbContext db, BackgroundJobItem job, Exception ex, CancellationToken cancellationToken)
    {
        var notifications = services.GetRequiredService<NotificationService>();
        var failureVersion = $"{job.Id}:{job.RetryCount}";

        if (job.Type is JobType.WritingEvaluation or JobType.SpeakingEvaluation && !string.IsNullOrWhiteSpace(job.AttemptId))
        {
            var attempt = await db.Attempts
                .AsNoTracking()
                .FirstOrDefaultAsync(existingAttempt => existingAttempt.Id == job.AttemptId, cancellationToken);

            if (attempt is not null)
            {
                await notifications.CreateForLearnerAsync(
                    NotificationEventKey.LearnerEvaluationFailed,
                    attempt.UserId,
                    "attempt",
                    attempt.Id,
                    failureVersion,
                    new Dictionary<string, object?>
                    {
                        ["attemptId"] = attempt.Id,
                        ["subtest"] = attempt.SubtestCode,
                        ["message"] = $"We could not finish your {attempt.SubtestCode} evaluation automatically. Please try again shortly."
                    },
                    cancellationToken);
            }
        }

        var adminAlertKey = job.Type is JobType.NotificationFanout or JobType.NotificationDigestDispatch
            ? NotificationEventKey.AdminNotificationDeliveryFailureAlert
            : NotificationEventKey.AdminStuckJobAlert;

        await notifications.CreateForAdminsAsync(
            adminAlertKey,
            "background_job",
            job.Id,
            failureVersion,
            new Dictionary<string, object?>
            {
                ["message"] = $"Background job {job.Id} ({job.Type}) failed after {job.RetryCount} attempts: {ex.Message}"
            },
            cancellationToken);
    }

    private static async Task MarkResourceFailedAfterFinalRetryAsync(LearnerDbContext db, BackgroundJobItem job, Exception ex, CancellationToken cancellationToken)
    {
        if (IsLiveClassRecordingPipelineJob(job.Type) && !string.IsNullOrWhiteSpace(job.ResourceId))
        {
            var recording = await db.LiveClassRecordings.FirstOrDefaultAsync(item => item.Id == job.ResourceId, cancellationToken);
            if (recording is not null && recording.Status != LiveClassRecordingStatus.Ready)
            {
                recording.Status = LiveClassRecordingStatus.Failed;
                recording.FailureReason = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            }
        }
    }

    private static bool IsLiveClassRecordingPipelineJob(JobType jobType)
        => jobType is JobType.LiveClassRecordingDownload
            or JobType.LiveClassRecordingTranscribe
            or JobType.LiveClassRecordingSummarize
            or JobType.LiveClassRecordingTranslate
            or JobType.LiveClassRecordingEmbed;

    private static async Task NotifyLiveClassRecordingReadyAsync(IServiceProvider services, LearnerDbContext db, string recordingId, CancellationToken cancellationToken)
    {
        var recording = await db.LiveClassRecordings
            .Include(item => item.ClassSession)
                .ThenInclude(session => session.LiveClass)
            .Include(item => item.ClassSession)
                .ThenInclude(session => session.Enrollments)
            .FirstOrDefaultAsync(item => item.Id == recordingId, cancellationToken);

        if (recording is null || recording.Status != LiveClassRecordingStatus.Ready)
        {
            return;
        }

        var notifications = services.GetRequiredService<NotificationService>();
        var session = recording.ClassSession;
        var version = (recording.ProcessedAt ?? DateTimeOffset.UtcNow).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var activeEnrollments = session.Enrollments
            .Where(enrollment => enrollment.Status is LiveClassEnrollmentStatus.Active or LiveClassEnrollmentStatus.Attended)
            .ToList();

        foreach (var enrollment in activeEnrollments)
        {
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerLiveClassRecordingReady,
                enrollment.UserId,
                "live_class_recording",
                recording.Id,
                version,
                new Dictionary<string, object?>
                {
                    ["classTitle"] = session.LiveClass.Title,
                    ["classId"] = session.LiveClassId,
                    ["sessionId"] = session.Id,
                    ["recordingId"] = recording.Id,
                },
                cancellationToken);
        }

        var classNotifications = services.GetService<OetWithDrHesham.Api.Services.Classes.IClassNotificationService>();
        if (classNotifications is not null)
        {
            await classNotifications.SendTutorRecordingReadyAsync(recording, session, cancellationToken);
        }
    }

    private static async Task CompleteContentGenerationAsync(LearnerDbContext db, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;

        var genJob = await db.ContentGenerationJobs.FirstOrDefaultAsync(j => j.Id == job.ResourceId, cancellationToken);
        if (genJob == null) return;

        genJob.State = "generating";
        await db.SaveChangesAsync(cancellationToken);

        var generatedIds = new List<string>();
        for (var i = 0; i < genJob.RequestedCount; i++)
        {
            var contentId = $"ci-{Guid.NewGuid():N}";
            db.ContentItems.Add(new ContentItem
            {
                Id = contentId,
                ExamFamilyCode = genJob.ExamTypeCode,
                SubtestCode = genJob.SubtestCode,
                ContentType = "practice_task",
                ProfessionId = genJob.ProfessionId,
                Title = $"[AI Generated] {genJob.SubtestCode} Task — {genJob.Difficulty}",
                Difficulty = genJob.Difficulty ?? "medium",
                DetailJson = JsonSupport.Serialize(new
                {
                    generatedBy = "AI",
                    generationJobId = genJob.Id,
                    prompt = genJob.PromptConfigJson,
                    caseNotes = "This is an AI-generated practice task. Review and edit before publishing.",
                    scenarioType = genJob.SubtestCode == "writing" ? "referral_letter" : "roleplay"
                }),
                Status = ContentStatus.Draft,
                SourceType = "ai_generated",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            generatedIds.Add(contentId);
        }

        genJob.GeneratedCount = generatedIds.Count;
        genJob.GeneratedContentIdsJson = JsonSupport.Serialize(generatedIds);
        genJob.State = "completed";
        genJob.CompletedAt = DateTimeOffset.UtcNow;
    }

    private static async Task CompleteConversationEvaluationAsync(IServiceProvider services, LearnerDbContext db, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;

        var session = await db.ConversationSessions.FirstOrDefaultAsync(s => s.Id == job.ResourceId, cancellationToken);
        if (session == null) return;

        var existing = await db.ConversationEvaluations.FirstOrDefaultAsync(e => e.SessionId == session.Id, cancellationToken);
        if (existing is not null)
        {
            session.State = "evaluated";
            session.EvaluationId = existing.Id;
            return;
        }

        var orchestrator = services.GetRequiredService<Conversation.IConversationAiOrchestrator>();

        if (!Enum.TryParse<OetWithDrHesham.Api.Services.Rulebook.ExamProfession>(
                (session.Profession ?? "medicine").Replace("-", "").Replace("_", ""),
                ignoreCase: true, out var profession))
            profession = OetWithDrHesham.Api.Services.Rulebook.ExamProfession.Medicine;

        var elapsedSeconds = session.StartedAt.HasValue && session.CompletedAt.HasValue
            ? (int)(session.CompletedAt.Value - session.StartedAt.Value).TotalSeconds
            : session.DurationSeconds;

        var ctx = new Conversation.ConversationAiContext(
            session.Id, session.UserId, null, null, profession,
            session.TaskTypeCode, session.ScenarioJson, session.TranscriptJson,
            session.TurnCount, elapsedSeconds, 0, null);

        Conversation.ConversationAiEvaluation aiEval;
        try
        {
            aiEval = await orchestrator.EvaluateAsync(ctx, cancellationToken);
        }
        catch (OetWithDrHesham.Api.Services.Rulebook.PromptNotGroundedException)
        {
            session.State = "failed";
            session.LastErrorCode = "ungrounded";
            return;
        }
        catch (Exception ex)
        {
            var logger = services.GetService<ILogger<BackgroundJobProcessor>>();
            logger?.LogError(ex, "Conversation AI evaluation failed for {SessionId}", session.Id);
            aiEval = new Conversation.ConversationAiEvaluation(
                new[]
                {
                    new Conversation.ConversationAiCriterion("intelligibility", 0, "evaluation error", Array.Empty<string>()),
                    new Conversation.ConversationAiCriterion("fluency", 0, "evaluation error", Array.Empty<string>()),
                    new Conversation.ConversationAiCriterion("appropriateness", 0, "evaluation error", Array.Empty<string>()),
                    new Conversation.ConversationAiCriterion("grammar_expression", 0, "evaluation error", Array.Empty<string>()),
                },
                Array.Empty<Conversation.ConversationAiAnnotation>(),
                Array.Empty<string>(),
                new[] { "The AI evaluator could not complete. Try the session again." },
                Array.Empty<string>(), Array.Empty<string>(),
                "AI evaluation failed.", "");
        }

        var intelligibility = aiEval.Criteria.FirstOrDefault(c => c.Id.Equals("intelligibility", StringComparison.OrdinalIgnoreCase))?.Score06 ?? 0;
        var fluency = aiEval.Criteria.FirstOrDefault(c => c.Id.Equals("fluency", StringComparison.OrdinalIgnoreCase))?.Score06 ?? 0;
        var appropriateness = aiEval.Criteria.FirstOrDefault(c => c.Id.Equals("appropriateness", StringComparison.OrdinalIgnoreCase))?.Score06 ?? 0;
        var grammarExpression = aiEval.Criteria.FirstOrDefault(c => c.Id.Equals("grammar_expression", StringComparison.OrdinalIgnoreCase))?.Score06 ?? 0;

        var mean = (intelligibility + fluency + appropriateness + grammarExpression) / 4.0;
        var scaled = OetScoring.ConversationProjectedScaled(mean);
        var band = OetScoring.GradeSpeaking(scaled);

        var evaluationId = $"ce-{Guid.NewGuid():N}";
        var evaluation = new ConversationEvaluation
        {
            Id = evaluationId,
            SessionId = session.Id,
            UserId = session.UserId,
            OverallScaled = band.ScaledScore,
            OverallGrade = band.Grade,
            Passed = band.Passed,
            CountryVariant = null,
            CriteriaJson = JsonSupport.Serialize(aiEval.Criteria.Select(c => new
            {
                id = c.Id, score06 = c.Score06, maxScore = 6.0, evidence = c.Evidence, quotes = c.Quotes,
            })),
            StrengthsJson = JsonSupport.Serialize(aiEval.Strengths),
            ImprovementsJson = JsonSupport.Serialize(aiEval.Improvements),
            SuggestedPracticeJson = JsonSupport.Serialize(aiEval.SuggestedPractice),
            AppliedRuleIdsJson = JsonSupport.Serialize(aiEval.AppliedRuleIds),
            RulebookVersion = aiEval.RulebookVersion,
            Advisory = aiEval.Advisory,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ConversationEvaluations.Add(evaluation);

        var examTypeCode = OetWithDrHesham.Api.Services.Common.ExamCodes.NormalizeOrNull(session.ExamTypeCode) ?? OetWithDrHesham.Api.Services.Common.ExamCodes.DefaultCode;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var seededReviewKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in aiEval.TurnAnnotations)
        {
            db.ConversationTurnAnnotations.Add(new ConversationTurnAnnotation
            {
                Id = $"cta-{Guid.NewGuid():N}",
                SessionId = session.Id,
                EvaluationId = evaluationId,
                TurnNumber = a.TurnNumber,
                Type = a.Type,
                Category = a.Category,
                RuleId = a.RuleId,
                Evidence = a.Evidence,
                Suggestion = a.Suggestion,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            if (!string.Equals(a.Type, "error", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(a.Type, "improvement", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(a.RuleId)) continue;

            var sourceId = $"{session.Id}:{a.TurnNumber}:{a.RuleId}";
            if (!seededReviewKeys.Add(sourceId)) continue;

            var existingReview = await db.ReviewItems.AnyAsync(r =>
                r.UserId == session.UserId && r.SourceType == "conversation_issue" && r.SourceId == sourceId,
                cancellationToken);
            if (existingReview) continue;

            db.ReviewItems.Add(new ReviewItem
            {
                Id = $"rv-{Guid.NewGuid():N}",
                UserId = session.UserId,
                ExamTypeCode = examTypeCode,
                SubtestCode = "speaking",
                SourceType = "conversation_issue",
                SourceId = sourceId,
                CriterionCode = a.Category,
                QuestionJson = JsonSupport.Serialize(new
                {
                    prompt = $"Conversation turn {a.TurnNumber}: {a.Evidence}",
                    ruleId = a.RuleId,
                    sessionId = session.Id,
                }),
                AnswerJson = JsonSupport.Serialize(new
                {
                    suggestion = a.Suggestion ?? "Revisit the rule and re-attempt this scenario.",
                    ruleId = a.RuleId,
                }),
                EaseFactor = 2.5,
                IntervalDays = 1,
                ReviewCount = 0,
                ConsecutiveCorrect = 0,
                DueDate = today.AddDays(1),
                CreatedAt = DateTimeOffset.UtcNow,
                Status = "active",
            });
        }

        session.State = "evaluated";
        session.EvaluationId = evaluationId;
    }

    private static Task CompletePronunciationAnalysisAsync(LearnerDbContext db, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        // Pronunciation analysis is handled inline in PronunciationService.SubmitDrillAttemptAsync
        // This handler exists for future production integration with Azure Speech SDK async processing
        return Task.CompletedTask;
    }

    private static async Task RunSubscriptionLifecycleCheckAsync(LearnerDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var renewalWindow = now.AddDays(7);

        List<Subscription> renewingSoon;
        List<Subscription> expired;
        if (db.Database.IsSqlite())
        {
            var activeSubscriptions = await db.Subscriptions
                .AsNoTracking()
                .Where(subscription => subscription.Status == SubscriptionStatus.Active)
                .ToListAsync(cancellationToken);

            renewingSoon = activeSubscriptions
                .Where(subscription => subscription.NextRenewalAt > now && subscription.NextRenewalAt <= renewalWindow)
                .ToList();
            expired = activeSubscriptions
                .Where(subscription => subscription.NextRenewalAt <= now)
                .ToList();
        }
        else
        {
            // Renewal reminders: Active subscriptions renewing within 7 days
            renewingSoon = await db.Subscriptions
                .AsNoTracking()
                .Where(subscription => subscription.Status == SubscriptionStatus.Active
                    && subscription.NextRenewalAt > now
                    && subscription.NextRenewalAt <= renewalWindow)
                .ToListAsync(cancellationToken);

            expired = await db.Subscriptions
                .AsNoTracking()
                .Where(subscription => subscription.Status == SubscriptionStatus.Active
                    && subscription.NextRenewalAt <= now)
                .ToListAsync(cancellationToken);
        }

        foreach (var sub in renewingSoon)
        {
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerRenewalComing,
                sub.UserId,
                "Subscription",
                sub.Id,
                now.UtcDateTime.ToString("yyyy-MM-dd"),
                new Dictionary<string, object?>
                {
                    ["message"] = $"Your subscription renews on {sub.NextRenewalAt:yyyy-MM-dd}.",
                    ["planName"] = sub.PlanId,
                    ["renewalDate"] = sub.NextRenewalAt.ToString("O")
                },
                cancellationToken);
        }

        foreach (var sub in expired)
        {
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerSubscriptionChanged,
                sub.UserId,
                "Subscription",
                sub.Id,
                now.UtcDateTime.ToString("yyyy-MM-dd"),
                new Dictionary<string, object?>
                {
                    ["message"] = "Your subscription has expired. Renew to keep your study plan and premium features.",
                    ["planName"] = sub.PlanId,
                    ["status"] = "expired"
                },
                cancellationToken);
        }
    }

    private static async Task RunSlaAlertCheckAsync(LearnerDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        // Alert experts 24h before SLA deadline
        var alertThreshold = now.AddHours(24);

        // Map turnaround options to hours for SLA calculation
        static int TurnaroundHours(string option) => option?.ToLowerInvariant() switch
        {
            "24h" or "24hour" or "24-hour" or "1day" => 24,
            "48h" or "48hour" or "48-hour" or "2day" => 48,
            "72h" or "72hour" or "72-hour" or "3day" => 72,
            "7day" or "7-day" or "1week" => 168,
            _ => 72
        };

        var approachingDeadline = db.Database.IsSqlite()
            ? (await db.ReviewRequests
                    .AsNoTracking()
                    .Where(request => request.State == ReviewRequestState.InReview)
                    .ToListAsync(cancellationToken))
                .Join(
                    (await db.ExpertReviewAssignments
                            .AsNoTracking()
                            .Where(assignment => assignment.ClaimState == ExpertAssignmentState.Claimed
                                && assignment.AssignedReviewerId != null)
                            .ToListAsync(cancellationToken))
                        .Where(assignment => !string.IsNullOrWhiteSpace(assignment.AssignedReviewerId)),
                    request => request.Id,
                    assignment => assignment.ReviewRequestId,
                    (request, assignment) => new { Request = request, Assignment = assignment })
                .ToList()
            : await db.ReviewRequests
                .AsNoTracking()
                .Where(request => request.State == ReviewRequestState.InReview)
                .Join(
                    db.ExpertReviewAssignments.AsNoTracking().Where(assignment => assignment.ClaimState == ExpertAssignmentState.Claimed && assignment.AssignedReviewerId != null),
                    request => request.Id,
                    assignment => assignment.ReviewRequestId,
                    (request, assignment) => new { Request = request, Assignment = assignment })
                .ToListAsync(cancellationToken);

        foreach (var item in approachingDeadline)
        {
            var slaDeadline = item.Request.CreatedAt.AddHours(TurnaroundHours(item.Request.TurnaroundOption));
            if (slaDeadline > now && slaDeadline <= alertThreshold && !string.IsNullOrWhiteSpace(item.Assignment.AssignedReviewerId))
            {
                await notifications.CreateForExpertAsync(
                    NotificationEventKey.ExpertReviewOverdue,
                    item.Assignment.AssignedReviewerId!,
                    "ReviewRequest",
                    item.Request.Id,
                    now.UtcDateTime.ToString("yyyy-MM-dd"),
                    new Dictionary<string, object?>
                    {
                        ["reviewRequestId"] = item.Request.Id,
                        ["message"] = $"Review {item.Request.Id} is approaching its SLA deadline ({slaDeadline:yyyy-MM-dd HH:mm})."
                    },
                    cancellationToken);
            }
        }
    }

    private static async Task RunDripCampaignDispatchAsync(LearnerDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var today = now.UtcDateTime.ToString("yyyy-MM-dd");

        // Credit depletion nudge: learners with low credits
        var lowCreditThreshold = 3;
        var lowCreditLearners = await db.Wallets
            .AsNoTracking()
            .Where(w => w.CreditBalance < lowCreditThreshold)
            .ToListAsync(cancellationToken);

        foreach (var wallet in lowCreditLearners)
        {
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerCreditsLow,
                wallet.UserId,
                "Wallet",
                wallet.Id,
                today,
                new Dictionary<string, object?>
                {
                    ["message"] = $"You have {wallet.CreditBalance} review credits left. Top up to keep submitting for expert feedback."
                },
                cancellationToken);
        }

        // Inactive learner nudge: no activity in 7 days
        var inactiveThreshold = now.AddDays(-7);
        var inactiveLearners = db.Database.IsSqlite()
            ? (await db.Users
                .AsNoTracking()
                .Where(user => user.Role == ApplicationUserRoles.Learner)
                .ToListAsync(cancellationToken))
                .Where(user => user.LastActiveAt < inactiveThreshold)
                .ToList()
            : await db.Users
                .AsNoTracking()
                .Where(user => user.LastActiveAt < inactiveThreshold
                    && user.Role == ApplicationUserRoles.Learner)
                .ToListAsync(cancellationToken);

        foreach (var user in inactiveLearners)
        {
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerInactiveNudge,
                user.Id,
                "User",
                user.Id,
                today,
                new Dictionary<string, object?>
                {
                    ["message"] = "We miss you! Come back to continue your OET preparation."
                },
                cancellationToken);
        }
    }
}
