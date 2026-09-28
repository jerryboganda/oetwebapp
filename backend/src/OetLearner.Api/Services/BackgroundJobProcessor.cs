using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public partial class BackgroundJobProcessor(IServiceScopeFactory scopeFactory, ILogger<BackgroundJobProcessor> logger) : BackgroundService
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
    /// <summary>Jobs claimed by the most recent pass; 0 = idle tick.</summary>
    private int _lastClaimedJobCount;
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
    /// <summary>
    /// Root-cause fix (Writing Rev8 release, 11-12 Sep 2026): the per-job
    /// try/catch below has no timeout of its own, and <see cref="StuckJobRecoveryAsync"/>
    /// runs only AFTER this foreach loop finishes - it exists to reap jobs
    /// orphaned by a container restart, not to interrupt one that is actively
    /// hanging in THIS pass. A single slow/hung external call (observed: a
    /// high-thinking-effort AI provider call during Writing Model Answer
    /// generation) therefore wedges the ENTIRE single-threaded worker for
    /// every job type - evaluations, notifications, SLA alerts, everything -
    /// for as long as the hang lasts, with no self-healing possible. Bound
    /// every job to this wall-clock ceiling so one bad call can never again
    /// block the whole pipeline; a job that legitimately needs longer keeps
    /// working past this window on the retry queued at the bottom of the
    /// loop's catch block, one pass call at a time.
    /// </summary>
    private static readonly TimeSpan MaxJobExecutionTime = TimeSpan.FromMinutes(20);
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
                // Idle backoff: when the previous pass claimed no jobs, poll
                // less frequently. Cuts steady-state DB chatter ~3x while a
                // queued job still starts within 2 seconds of appearing.
                var idle = _lastClaimedJobCount == 0;
                await Task.Delay(idle ? TimeSpan.FromSeconds(6) : TimeSpan.FromSeconds(2), stoppingToken);
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

        // Recover orphaned Processing jobs BEFORE claiming new work. Running this
        // after the dispatch pass meant a tick that spent its whole budget on a
        // hung job (up to MaxJobExecutionTime) delayed recovery — and the stuck-job
        // alert — by that same window. Doing it first bounds that delay to one tick.
        if (now - _lastStuckJobRecoveryAt >= StuckJobRecoveryInterval)
        {
            _lastStuckJobRecoveryAt = now;
            await RecoverStuckJobsAsync(scope.ServiceProvider, db, cancellationToken);
        }

        var jobs = await ClaimQueuedJobsAsync(db, now, cancellationToken);
        _lastClaimedJobCount = jobs.Count;

        foreach (var job in jobs)
        {
            try
            {
                using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                jobCts.CancelAfter(MaxJobExecutionTime);
                try
                {
                    await ExecuteJobAsync(scope.ServiceProvider, db, job, jobCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // jobCts's own timer fired (MaxJobExecutionTime elapsed), not a
                    // real host shutdown - surface as an ordinary job failure so the
                    // existing retry/backoff logic below handles it and the loop
                    // moves on to the next job instead of hanging forever.
                    throw new TimeoutException(
                        $"Job {job.Id} ({job.Type}) exceeded the {MaxJobExecutionTime} execution ceiling.");
                }
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

        // Stuck-job recovery runs before the claim loop (see the top of this
        // method) so it is never starved by a hung job; it is deliberately not
        // repeated here.

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
                    .GetRequiredService<OetLearner.Api.Services.Expert.IExpertAutoAssignmentService>();
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
                    .GetRequiredService<OetLearner.Api.Services.Expert.IExpertAutoAssignmentService>();
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

    private static async Task ExecuteJobAsync(IServiceProvider services, LearnerDbContext db, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        var notifications = services.GetRequiredService<NotificationService>();
        switch (job.Type)
        {
            case JobType.WritingEvaluation:
                await services.GetRequiredService<OetLearner.Api.Services.Writing.IWritingEvaluationPipeline>()
                    .CompleteEvaluationAsync(job, cancellationToken);
                await CompleteWritingEvaluationSideEffectsAsync(services, db, notifications, job, cancellationToken);
                break;
            case JobType.WritingModelAnswerGeneration:
                await CompleteWritingModelAnswerGenerationAsync(services, job, cancellationToken);
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
                await services.GetRequiredService<OetLearner.Api.Services.Mocks.Results.IMockReportAggregationService>()
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
            case JobType.MockBookingZoomCreate:
            {
                var provisioner = services.GetRequiredService<Mocks.MockBookingZoomProvisioner>();
                await provisioner.CreateZoomMeetingForMockBookingAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.MockBookingConfirmation:
            {
                var notificationService = services.GetRequiredService<Mocks.MockBookingNotificationService>();
                await notificationService.SendConfirmationAsync(job.ResourceId!, cancellationToken);
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
                await services.GetRequiredService<OetLearner.Api.Services.Expert.IExpertAutoAssignmentService>()
                    .ProcessPendingAssignmentsAsync(cancellationToken);
                break;
            case JobType.ExpertReviewSlaEscalation:
                await services.GetRequiredService<OetLearner.Api.Services.Expert.IExpertAutoAssignmentService>()
                    .ProcessSlaEscalationsAsync(cancellationToken);
                break;

            // ── Live Classes ──
            case JobType.LiveClassRecordingDownload:
            {
                var svc = services.GetRequiredService<OetLearner.Api.Services.LiveClasses.LiveClassRecordingService>();
                await svc.ProcessDownloadAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.LiveClassRecordingTranscribe:
            {
                // Wave A2 — when AI processing is enabled, the processor service
                // calls Whisper (or reuses Zoom AI Companion's transcript) and
                // queues the Summarize stage. Flag-off ⇒ recording stays Pending.
                var processor = services.GetRequiredService<OetLearner.Api.Services.LiveClasses.LiveClassRecordingProcessingService>();
                await processor.ProcessTranscribeAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.LiveClassRecordingSummarize:
            {
                // Wave A2 — Sonnet-4.6 cached-prompt summarise → JSON
                // {summary, chapters, actionItems, keyTopics} and queue Translate.
                var processor = services.GetRequiredService<OetLearner.Api.Services.LiveClasses.LiveClassRecordingProcessingService>();
                await processor.ProcessSummarizeAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.LiveClassRecordingTranslate:
            {
                // Wave A2 — Sonnet-4.6 EN→AR translation, mark Ready, fan-out
                // learner "recording ready" notifications, queue Embed.
                var processor = services.GetRequiredService<OetLearner.Api.Services.LiveClasses.LiveClassRecordingProcessingService>();
                await processor.ProcessTranslateAsync(job.ResourceId!, cancellationToken);
                await NotifyLiveClassRecordingReadyAsync(services, db, job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.LiveClassRecordingEmbed:
            {
                // Wave A2 — chunk transcript + persist 1536-d vectors for the
                // "Ask AI about this class" RAG surface. Best-effort.
                var processor = services.GetRequiredService<OetLearner.Api.Services.LiveClasses.LiveClassRecordingProcessingService>();
                await processor.ProcessEmbedAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.LiveClassSessionReminderDispatch:
            {
                var svc = services.GetRequiredService<OetLearner.Api.Services.LiveClasses.LiveClassRecordingService>();
                await svc.ProcessReminderDispatchAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.LiveClassNoShowPingDispatch:
            {
                var svc = services.GetRequiredService<OetLearner.Api.Services.LiveClasses.LiveClassRecordingService>();
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

        // Aggregate by job TYPE within an hourly bucket, not by the individual
        // job id. The notification dedupe key embeds the entity id, so keying a
        // stuck-job alert on job.Id emits one email per affected job — that is
        // the alert flood reported on 11-12 Sep 2026, when a batch of Writing
        // model-answer jobs wedged behind one hung AI call. Keying on
        // (event, job type, hour) collapses that batch into a single admin alert
        // per type per hour, while a different job type or a later hour still
        // raises its own alert, so genuine warnings are preserved.
        var incidentBucket = NotificationScheduling.BuildIncidentBucket(DateTimeOffset.UtcNow);

        await notifications.CreateForAdminsAsync(
            adminAlertKey,
            "background_job_type",
            job.Type.ToString(),
            incidentBucket,
            new Dictionary<string, object?>
            {
                ["message"] = $"Background job type {job.Type} failed after {job.RetryCount} attempts (latest job {job.Id}): {ex.Message}"
            },
            cancellationToken);
    }

    private static async Task MarkResourceFailedAfterFinalRetryAsync(LearnerDbContext db, BackgroundJobItem job, Exception ex, CancellationToken cancellationToken)
    {
        if (job.Type == JobType.SpeakingEvaluation)
        {
            // Without this the Evaluation stayed Queued forever once the job
            // exhausted its retries (or was orphaned by a restart): the
            // learner polled a dead "processing" state with no retry button.
            // Retryable=true unlocks POST /v1/speaking/attempts/{id}/retry-evaluation.
            var evaluation = await SpeakingEvaluationPipeline.FindEvaluationForJobAsync(db, job, cancellationToken);
            if (evaluation is not null && evaluation.State != AsyncState.Completed)
            {
                evaluation.State = AsyncState.Failed;
                evaluation.Retryable = true;
                evaluation.RetryAfterMs = 60_000;
                evaluation.StatusReasonCode = "speaking_evaluation_failed";
                evaluation.StatusMessage = "We couldn't finish grading your recording. Please try grading again.";
                evaluation.LastTransitionAt = DateTimeOffset.UtcNow;
            }

            if (!string.IsNullOrWhiteSpace(job.AttemptId))
            {
                var attempt = await db.Attempts.FirstOrDefaultAsync(item => item.Id == job.AttemptId, cancellationToken);
                if (attempt is not null && attempt.State == AttemptState.Evaluating)
                {
                    attempt.State = AttemptState.Submitted;
                }
            }
        }

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

        var classNotifications = services.GetService<OetLearner.Api.Services.Classes.IClassNotificationService>();
        if (classNotifications is not null)
        {
            await classNotifications.SendTutorRecordingReadyAsync(recording, session, cancellationToken);
        }
    }
}
