using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.LiveClasses;

/// <summary>
/// Handles the recording pipeline for Live Classes:
///   Download from Zoom → store via IFileStorage → Transcribe → AI Summary
///
/// All methods are safe to call from the background job processor.
/// They handle their own exceptions gracefully — failures update
/// the recording status and log rather than rethrowing.
/// </summary>
public sealed class LiveClassRecordingService(
    LearnerDbContext db,
    ZoomMeetingService zoomMeetingService,
    IFileStorage fileStorage,
    NotificationService notifications,
    TimeProvider timeProvider,
    ILogger<LiveClassRecordingService> logger)
{
    // ─────────────────────────────────────────────────────────────────────────
    // Step 1: Download
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the Zoom recording to configured storage and queues a Transcribe job.
    /// </summary>
    public async Task ProcessDownloadAsync(string recordingId, CancellationToken ct)
    {
        LiveClassRecording? recording = null;
        try
        {
            recording = await db.LiveClassRecordings
                .Include(r => r.ClassSession)
                    .ThenInclude(s => s.LiveClass)
                .FirstOrDefaultAsync(r => r.Id == recordingId, ct);

            if (recording is null)
            {
                logger.LogWarning("ProcessDownloadAsync: recording {RecordingId} not found — skipping.", recordingId);
                return;
            }

            if (recording.Status is not (LiveClassRecordingStatus.Pending or LiveClassRecordingStatus.Downloading))
            {
                logger.LogInformation(
                    "ProcessDownloadAsync: recording {RecordingId} has status {Status} — skipping.",
                    recordingId, recording.Status);
                return;
            }

            // Mark as in-progress
            recording.Status = LiveClassRecordingStatus.Downloading;
            await db.SaveChangesAsync(ct);

            if (!recording.ClassSession.ZoomMeetingId.HasValue)
            {
                logger.LogWarning(
                    "ProcessDownloadAsync: no Zoom meeting id for recording {RecordingId} — marking Failed.",
                    recordingId);
                recording.Status = LiveClassRecordingStatus.Failed;
                recording.FailureReason = "Zoom meeting id is missing.";
                await db.SaveChangesAsync(ct);
                return;
            }

            var now = timeProvider.GetUtcNow();
            var session = recording.ClassSession;

            logger.LogInformation(
                "ProcessDownloadAsync: downloading recording {RecordingId} from Zoom, session {SessionId}.",
                recordingId, session.Id);

            // Build storage key: live-class-recordings/{year}/{month}/{sessionId}/video.mp4
            var s3Key = $"live-class-recordings/{now.Year}/{now.Month:D2}/{session.Id}/video.mp4";

            await using (var destination = await fileStorage.OpenWriteAsync(s3Key, ct))
            {
                await zoomMeetingService.CopyRecordingFileAsync(
                    session.ZoomMeetingId.Value,
                    recording.ZoomRecordingId,
                    destination,
                    ct);
            }

            // Update recording
            recording.S3VideoKey = s3Key;
            recording.Status = LiveClassRecordingStatus.Processing;

            // Queue Transcribe job
            QueueJob(JobType.LiveClassRecordingTranscribe, recordingId, now);

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "ProcessDownloadAsync: recording {RecordingId} uploaded to S3 key {S3Key} — Transcribe job queued.",
                recordingId, s3Key);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "ProcessDownloadAsync: unhandled exception for recording {RecordingId}.",
                recordingId);

            if (recording is not null)
            {
                try
                {
                    recording.Status = LiveClassRecordingStatus.Failed;
                    recording.FailureReason = ex.Message.Length > 500
                        ? ex.Message[..500]
                        : ex.Message;
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception saveEx)
                {
                    logger.LogError(saveEx,
                        "ProcessDownloadAsync: failed to persist failure state for recording {RecordingId}.",
                        recordingId);
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 2: Transcribe
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Populates TranscriptText (or a placeholder) and queues a Summarize job.
    /// </summary>
    public async Task ProcessTranscribeAsync(string recordingId, CancellationToken ct)
    {
        LiveClassRecording? recording = null;
        try
        {
            recording = await db.LiveClassRecordings
                .FirstOrDefaultAsync(r => r.Id == recordingId, ct);

            if (recording is null)
            {
                logger.LogWarning("ProcessTranscribeAsync: recording {RecordingId} not found — skipping.", recordingId);
                return;
            }

            var now = timeProvider.GetUtcNow();

            if (string.IsNullOrWhiteSpace(recording.S3TranscriptKey) &&
                string.IsNullOrWhiteSpace(recording.TranscriptText))
            {
                // v1 sandbox behaviour: real Zoom AI Companion VTT parsing deferred to v2.
                recording.TranscriptText =
                    "[Transcript processing — Zoom AI Companion transcript will appear here when available]";
                logger.LogInformation(
                    "ProcessTranscribeAsync: recording {RecordingId} has no transcript source — using placeholder.",
                    recordingId);
            }
            else if (!string.IsNullOrWhiteSpace(recording.S3TranscriptKey))
            {
                // Production path (deferred): download VTT from S3TranscriptKey and parse.
                // For v1, just acknowledge the key exists and keep whatever TranscriptText is set.
                logger.LogInformation(
                    "ProcessTranscribeAsync: recording {RecordingId} has S3TranscriptKey={Key} — VTT parsing deferred to v2.",
                    recordingId, recording.S3TranscriptKey);
            }

            // Queue Summarize job
            QueueJob(JobType.LiveClassRecordingSummarize, recordingId, now);

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "ProcessTranscribeAsync: recording {RecordingId} transcript step complete — Summarize job queued.",
                recordingId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "ProcessTranscribeAsync: unhandled exception for recording {RecordingId}.",
                recordingId);

            if (recording is not null)
            {
                try
                {
                    recording.Status = LiveClassRecordingStatus.Failed;
                    recording.FailureReason = ex.Message.Length > 500
                        ? ex.Message[..500]
                        : ex.Message;
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception saveEx)
                {
                    logger.LogError(saveEx,
                        "ProcessTranscribeAsync: failed to persist failure state for recording {RecordingId}.",
                        recordingId);
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 3: Summarize
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates an AI summary (or placeholder), marks the recording Ready,
    /// and notifies enrolled learners.
    /// </summary>
    public async Task ProcessSummarizeAsync(string recordingId, CancellationToken ct)
    {
        LiveClassRecording? recording = null;
        try
        {
            recording = await db.LiveClassRecordings
                .Include(r => r.ClassSession)
                    .ThenInclude(s => s.Enrollments)
                .Include(r => r.ClassSession)
                    .ThenInclude(s => s.LiveClass)
                .FirstOrDefaultAsync(r => r.Id == recordingId, ct);

            if (recording is null)
            {
                logger.LogWarning("ProcessSummarizeAsync: recording {RecordingId} not found — skipping.", recordingId);
                return;
            }

            var now = timeProvider.GetUtcNow();
            var isPlaceholder = string.IsNullOrWhiteSpace(recording.TranscriptText) ||
                                 recording.TranscriptText.StartsWith("[Transcript processing", StringComparison.Ordinal);

            if (isPlaceholder)
            {
                // No real transcript yet — mark Ready without summary.
                logger.LogInformation(
                    "ProcessSummarizeAsync: recording {RecordingId} has placeholder/empty transcript — marking Ready without summary.",
                    recordingId);
                recording.AiSummary = null;
            }
            else
            {
                // v1 sandbox: real AI summary generation deferred.
                // In v2 this will call AiGatewayService with the transcript text.
                logger.LogInformation(
                    "ProcessSummarizeAsync: recording {RecordingId} has transcript ({Length} chars) — AI summary deferred to v2, using placeholder.",
                    recordingId, recording.TranscriptText!.Length);
                recording.AiSummary =
                    "[AI summary generation pending — transcript available for processing]";
            }

            recording.Status = LiveClassRecordingStatus.Ready;
            recording.ProcessedAt = now;

            // Notify enrolled learners (active enrollments only)
            var session = recording.ClassSession;
            var activeEnrollments = session.Enrollments
                .Where(e => e.Status == LiveClassEnrollmentStatus.Active ||
                            e.Status == LiveClassEnrollmentStatus.Attended)
                .ToList();

            foreach (var enrollment in activeEnrollments)
            {
                await notifications.CreateForLearnerAsync(
                    NotificationEventKey.LearnerLiveClassRecordingReady,
                    enrollment.UserId,
                    "live_class_recording",
                    recording.Id,
                    recording.ProcessedAt?.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)
                        ?? now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
                    new Dictionary<string, object?>
                    {
                        ["classTitle"] = session.LiveClass.Title,
                        ["classId"] = session.LiveClassId,
                        ["sessionId"] = session.Id,
                        ["recordingId"] = recording.Id,
                    },
                    ct);
            }

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "ProcessSummarizeAsync: recording {RecordingId} marked Ready. {Count} learner(s) would be notified.",
                recordingId, activeEnrollments.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "ProcessSummarizeAsync: unhandled exception for recording {RecordingId}.",
                recordingId);

            if (recording is not null)
            {
                try
                {
                    recording.Status = LiveClassRecordingStatus.Failed;
                    recording.FailureReason = ex.Message.Length > 500
                        ? ex.Message[..500]
                        : ex.Message;
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception saveEx)
                {
                    logger.LogError(saveEx,
                        "ProcessSummarizeAsync: failed to persist failure state for recording {RecordingId}.",
                        recordingId);
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 4: Reminder dispatch (stub)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dispatches session reminders to enrolled learners.
    /// v1: stub — full notification integration pending NotificationEventKey.LearnerLiveClassReminder template.
    /// </summary>
    public async Task ProcessReminderDispatchAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var session = await db.LiveClassSessions
                .Include(s => s.LiveClass)
                .Include(s => s.Enrollments)
                .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

            if (session is null)
            {
                logger.LogWarning("ProcessReminderDispatchAsync: session {SessionId} not found — skipping.", sessionId);
                return;
            }

            var activeEnrollments = session.Enrollments
                .Where(e => e.Status == LiveClassEnrollmentStatus.Active)
                .ToList();

            foreach (var enrollment in activeEnrollments)
            {
                await notifications.CreateForLearnerAsync(
                    NotificationEventKey.LearnerLiveClassReminder,
                    enrollment.UserId,
                    "live_class_session",
                    session.Id,
                    session.ScheduledStartAt.ToString("yyyyMMddHH", System.Globalization.CultureInfo.InvariantCulture),
                    new Dictionary<string, object?>
                    {
                        ["classTitle"] = session.LiveClass.Title,
                        ["sessionTime"] = session.ScheduledStartAt.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture),
                        ["classId"] = session.LiveClassId,
                        ["sessionId"] = session.Id,
                    },
                    ct);
            }

            logger.LogInformation(
                "ProcessReminderDispatchAsync: sent live class reminders for session {SessionId} to {Count} active learner(s).",
                sessionId, activeEnrollments.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "ProcessReminderDispatchAsync: unhandled exception for session {SessionId}.",
                sessionId);
            // Reminders are fire-and-forget — no status to update on failure.
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a background job to the EF change tracker (caller must SaveChanges).
    /// </summary>
    private void QueueJob(JobType type, string resourceId, DateTimeOffset now)
    {
        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id          = $"bgj-{Guid.NewGuid():N}",
            Type        = type,
            ResourceId  = resourceId,
            State       = AsyncState.Queued,
            AvailableAt = now,
            CreatedAt   = now,
        });
    }
}
