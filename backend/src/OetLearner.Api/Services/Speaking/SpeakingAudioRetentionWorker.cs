using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Contracts;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Wave 7 of docs/SPEAKING-MODULE-PLAN.md - background sweeper that
/// physically deletes learner speaking audio once it is older than
/// <see cref="SpeakingComplianceOptions.AudioRetentionDays"/>. Mirrors
/// <see cref="OetLearner.Api.Services.Conversation.ConversationAudioRetentionWorker"/>.
///
/// Sweeps in batches of 500 to keep memory bounded. The blob is removed
/// via <see cref="IFileStorage"/> and the <c>AudioObjectKey</c> column is
/// cleared. Once every recording in a session has expired, transcript and
/// report evidence is redacted while non-content score/version/audit metadata
/// remains available.
///
/// Phase 7 of the OET Speaking module plan (B.8) extended this worker
/// with a second sweep that walks <see cref="SpeakingRecording"/> rows
/// (the new typed schema introduced in Phase 1+) and physically deletes
/// blobs whose <c>RetentionExpiresAt</c> has elapsed. Each deletion
/// emits an <see cref="AuditEvent"/> row so the compliance audit trail
/// captures every reaper-initiated removal.
/// </summary>
public sealed class SpeakingAudioRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SpeakingAudioRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Speaking audio retention sweep failed");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Shutdown - exit loop quietly.
            }
        }
    }

    /// <summary>
    /// Internal entry point used by tests. Returns the number of
    /// attempts that had their audio cleared. Also drives the Phase 7
    /// <see cref="SpeakingRecording"/> sweep but doesn't surface that
    /// count separately to preserve the existing test contract — use
    /// <see cref="SweepSpeakingRecordingsOnceAsync"/> for the new path.
    /// </summary>
    public async Task<int> SweepOnceAsync(CancellationToken ct)
    {
        var clearedAttempts = await SweepLegacyAttemptsAsync(ct);

        // Phase 7 sweep — never throws on partial failure so the
        // legacy attempt sweep above stays the authoritative count.
        try
        {
            await SweepSpeakingRecordingsOnceAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SpeakingRecording retention sweep failed");
        }

        return clearedAttempts;
    }

    /// <summary>Legacy sweep over <see cref="Attempt.AudioObjectKey"/>.
    /// Preserved verbatim from the original worker so existing tests
    /// (<c>SpeakingAudioRetentionWorkerTests</c>) continue to pass.</summary>
    private async Task<int> SweepLegacyAttemptsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptions<SpeakingComplianceOptions>>().Value;

        if (options.AudioRetentionDays <= 0)
        {
            return 0;
        }

        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(options.AudioRetentionDays);

        // We retain audio while the attempt is still active. Sweep on
        // SubmittedAt when present, otherwise StartedAt. Only speaking.
        List<Attempt> due;
        try
        {
            due = await db.Attempts
                .Where(a => a.SubtestCode == "speaking"
                    && a.AudioObjectKey != null
                    && ((a.SubmittedAt != null && a.SubmittedAt < cutoff)
                        || (a.SubmittedAt == null && a.StartedAt < cutoff)))
                .OrderBy(a => a.SubmittedAt ?? a.StartedAt)
                .Take(BatchSize)
                .ToListAsync(ct);
        }
        catch (InvalidOperationException) when (db.Database.IsSqlite())
        {
            // The SQLite provider (bundled desktop backend) cannot translate
            // DateTimeOffset comparisons/ordering — same limitation handled by
            // the /health/ready stuck-jobs probe. Narrow on the translatable
            // predicates server-side, apply the cutoff and ordering in memory.
            var candidates = await db.Attempts
                .Where(a => a.SubtestCode == "speaking" && a.AudioObjectKey != null)
                .ToListAsync(ct);
            due = candidates
                .Where(a => (a.SubmittedAt ?? a.StartedAt) < cutoff)
                .OrderBy(a => a.SubmittedAt ?? a.StartedAt)
                .Take(BatchSize)
                .ToList();
        }

        if (due.Count == 0)
        {
            return 0;
        }

        var clearedAttempts = 0;
        var deletedBlobs = 0;
        foreach (var attempt in due)
        {
            var key = attempt.AudioObjectKey!;
            var canClearPointer = true;
            try
            {
                if (await storage.ExistsAsync(key, ct))
                {
                    var deleted = await storage.DeleteAsync(key, ct);
                    if (deleted)
                    {
                        deletedBlobs++;
                    }
                    else if (await storage.ExistsAsync(key, ct))
                    {
                        canClearPointer = false;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to delete speaking audio blob {Key} for attempt {AttemptId}",
                    key, attempt.Id);
                canClearPointer = false;
            }

            if (!canClearPointer)
            {
                continue;
            }

            attempt.AudioObjectKey = null;
            clearedAttempts++;
        }

        if (clearedAttempts == 0)
        {
            return 0;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Speaking audio retention sweep cleared {Count} attempts, deleted {Blobs} blobs.",
            clearedAttempts, deletedBlobs);

        return clearedAttempts;
    }

    /// <summary>
    /// Phase 7 sweep: walks <see cref="SpeakingRecording"/> rows whose
    /// <c>RetentionExpiresAt</c> has elapsed AND that are not already
    /// archived. Deletes the underlying blob (best-effort) and writes:
    ///   * <c>IsArchived = true</c> on the recording row.
    ///   * An <see cref="AuditEvent"/> row with action
    ///     <c>SpeakingRecordingExpiredByRetention</c> so the GDPR audit
    ///     trail captures every reaper-initiated deletion.
    ///   * A session-level audit row when all recordings are expired; the
    ///     session transcript/report evidence is then redacted.
    /// Returns the number of rows archived in this sweep.
    /// </summary>
    public async Task<int> SweepSpeakingRecordingsOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var now = DateTimeOffset.UtcNow;

        // Pull due rows. The retention worker only sweeps rows that
        // explicitly carry a RetentionExpiresAt — sessions that have
        // never been wired through the new lifecycle still rely on the
        // legacy Attempt.AudioObjectKey sweep above.
        List<SpeakingRecording> due;
        try
        {
            due = await db.SpeakingRecordings
                .Where(r => r.RetentionExpiresAt != null
                    && r.RetentionExpiresAt <= now
                    && !r.IsArchived)
                .OrderBy(r => r.RetentionExpiresAt)
                .Take(BatchSize)
                .ToListAsync(ct);
        }
        catch (InvalidOperationException) when (db.Database.IsSqlite())
        {
            // SQLite provider limitation — see SweepLegacyAttemptsAsync.
            var candidates = await db.SpeakingRecordings
                .Where(r => r.RetentionExpiresAt != null && !r.IsArchived)
                .ToListAsync(ct);
            due = candidates
                .Where(r => r.RetentionExpiresAt <= now)
                .OrderBy(r => r.RetentionExpiresAt)
                .Take(BatchSize)
                .ToList();
        }

        if (due.Count == 0)
        {
            return 0;
        }

        var archivedCount = 0;
        var auditCount = 0;
        foreach (var recording in due)
        {
            var blobDeleted = false;
            string? storageKey = null;
            var deletionConfirmed = true;

            try
            {
                var mediaAsset = await db.MediaAssets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == recording.MediaAssetId, ct);
                if (mediaAsset is not null && !string.IsNullOrWhiteSpace(mediaAsset.StoragePath))
                {
                    storageKey = mediaAsset.StoragePath;
                    if (await storage.ExistsAsync(storageKey, ct))
                    {
                        blobDeleted = await storage.DeleteAsync(storageKey, ct);
                        deletionConfirmed = blobDeleted || !await storage.ExistsAsync(storageKey, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                deletionConfirmed = false;
                logger.LogWarning(ex,
                    "Failed to delete blob for SpeakingRecording {RecordingId}", recording.Id);
            }

            if (!deletionConfirmed)
            {
                db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    OccurredAt = now,
                    ActorId = "system",
                    ActorName = "SpeakingAudioRetentionWorker",
                    Action = "SpeakingRecordingRetentionDeletionFailed",
                    ResourceType = "SpeakingRecording",
                    ResourceId = recording.Id,
                    Details = JsonSerializer.Serialize(new
                    {
                        sessionId = recording.SpeakingSessionId,
                        storageKey,
                        source = recording.Source.ToString(),
                        sha256 = recording.Sha256,
                        retryable = true,
                    }),
                });
                auditCount++;
                // Keep IsArchived=false and retain the storage pointer. The
                // next sweep must be able to retry the deletion safely.
                continue;
            }

            recording.IsArchived = true;
            // Snapshot the actual archival timestamp.
            recording.RetentionExpiresAt ??= now;

            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = "system",
                ActorName = "SpeakingAudioRetentionWorker",
                Action = "SpeakingRecordingExpiredByRetention",
                ResourceType = "SpeakingRecording",
                ResourceId = recording.Id,
                Details = JsonSerializer.Serialize(new
                {
                    sessionId = recording.SpeakingSessionId,
                    blobDeleted,
                    storageKey,
                    source = recording.Source.ToString(),
                    sha256 = recording.Sha256,
                }),
            });

            archivedCount++;
        }

        if (archivedCount == 0 && auditCount == 0)
        {
            return 0;
        }

        await db.SaveChangesAsync(ct);

        var redactedSessions = archivedCount > 0
            ? await RedactExpiredSessionEvidenceAsync(
                db,
                due.Where(x => x.IsArchived).Select(x => x.SpeakingSessionId),
                now,
                ct)
            : 0;
        if (redactedSessions > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "SpeakingRecording retention sweep archived {Count} rows, redacted {RedactedSessions} expired sessions, and wrote {AuditCount} audit rows.",
            archivedCount, redactedSessions, auditCount);
        return archivedCount;
    }

    private static async Task<int> RedactExpiredSessionEvidenceAsync(
        LearnerDbContext db,
        IEnumerable<string> candidateSessionIds,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var sessionIds = candidateSessionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (sessionIds.Length == 0)
        {
            return 0;
        }

        var sessions = await db.SpeakingSessions
            .Where(x => sessionIds.Contains(x.Id))
            .Select(x => new { x.Id, x.ExamSessionId })
            .ToListAsync(ct);
        var redacted = 0;

        foreach (var session in sessions)
        {
            // A full mock can have more than one recording. Do not remove
            // the relevant transcript until every recording in its retention
            // scope has either been archived successfully or has no remaining
            // retention window. Card reports can be redacted independently;
            // the combined report waits for both cards.
            var retentionScopeSessionIds = session.ExamSessionId is null
                ? new[] { session.Id }
                : await db.SpeakingSessions
                    .Where(x => x.ExamSessionId == session.ExamSessionId)
                    .Select(x => x.Id)
                    .ToArrayAsync(ct);
            if (retentionScopeSessionIds.Length == 0)
            {
                retentionScopeSessionIds = [session.Id];
            }

            var hasLiveRecording = await db.SpeakingRecordings
                .AnyAsync(x => retentionScopeSessionIds.Contains(x.SpeakingSessionId)
                    && !x.IsArchived
                    && (x.RetentionExpiresAt == null || x.RetentionExpiresAt > now), ct);
            var currentSessionHasLiveRecording = await db.SpeakingRecordings
                .AnyAsync(x => x.SpeakingSessionId == session.Id
                    && !x.IsArchived
                    && (x.RetentionExpiresAt == null || x.RetentionExpiresAt > now), ct);
            if (currentSessionHasLiveRecording)
            {
                continue;
            }

            var allExamRecordingsExpired = !hasLiveRecording;

            var transcriptRows = await db.SpeakingTranscripts
                .Where(x => x.SpeakingSessionId == session.Id)
                .ToListAsync(ct);
            foreach (var transcript in transcriptRows)
            {
                transcript.SegmentsJson = "[]";
                transcript.WordCount = 0;
                transcript.MeanConfidence = 0;
            }

            var turnEvidence = await db.SpeakingSimulationV11TurnEvidenceRows
                .Where(x => x.SpeakingSessionId == session.Id)
                .ToListAsync(ct);
            foreach (var turn in turnEvidence)
            {
                turn.Text = string.Empty;
                turn.WordConfidenceJson = "[]";
                turn.SourceTranscriptId = string.Empty;
                turn.SourceRecordingId = null;
            }

            var assessmentRows = await db.SpeakingSimulationV11Assessments
                .Where(x => x.SpeakingSessionId == session.Id
                    || (allExamRecordingsExpired
                        && session.ExamSessionId != null
                        && x.ExamSessionId == session.ExamSessionId))
                .ToListAsync(ct);
            var assessmentIds = assessmentRows.Select(x => x.Id).ToArray();
            foreach (var assessment in assessmentRows)
            {
                assessment.ReportJson = RedactReportJson(assessment.ReportJson);
                assessment.SourceTranscriptId = null;
                assessment.SourceRecordingId = null;
            }

            var reportEvidence = assessmentIds.Length == 0
                ? new List<SpeakingSimulationV11Evidence>()
                : await db.SpeakingSimulationV11EvidenceRows
                    .Where(x => assessmentIds.Contains(x.AssessmentId))
                    .ToListAsync(ct);
            foreach (var evidence in reportEvidence)
            {
                evidence.SourceReference = null;
                evidence.QuoteText = string.Empty;
                evidence.FindingText = null;
                evidence.ActionSuggestion = null;
                evidence.StartMs = null;
                evidence.EndMs = null;
                evidence.SourceTranscriptId = null;
                evidence.SourceRecordingId = null;
                evidence.EvidenceStatus = "expired";
                evidence.IsPrimary = false;
            }

            var criterionScores = assessmentIds.Length == 0
                ? new List<SpeakingSimulationV11CriterionScore>()
                : await db.SpeakingSimulationV11CriterionScores
                    .Where(x => assessmentIds.Contains(x.AssessmentId))
                    .ToListAsync(ct);
            foreach (var criterion in criterionScores)
            {
                criterion.Rationale = null;
            }

            var tutorOverrides = await db.SpeakingSimulationV11TutorOverrides
                .Where(x => x.SpeakingSessionId == session.Id)
                .ToListAsync(ct);
            foreach (var tutorOverride in tutorOverrides)
            {
                tutorOverride.OriginalReportJson = RedactReportJson(tutorOverride.OriginalReportJson) ?? "{}";
                tutorOverride.OverrideReportJson = RedactReportJson(tutorOverride.OverrideReportJson) ?? "{}";
                tutorOverride.Reason = "Tutor review evidence expired under the approved retention policy.";
            }

            var audioQuality = await db.SpeakingSimulationV11AudioQualityChecks
                .Where(x => x.SpeakingSessionId == session.Id)
                .ToListAsync(ct);
            foreach (var quality in audioQuality)
            {
                quality.SourceRecordingId = null;
                quality.SourceMediaAssetId = null;
                quality.OriginalSha256 = null;
            }

            var telemetry = await db.SpeakingSimulationV11TurnTelemetryRows
                .Where(x => x.SpeakingSessionId == session.Id)
                .ToListAsync(ct);
            foreach (var row in telemetry)
            {
                row.SourceTranscriptId = null;
            }

            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = "system",
                ActorName = "SpeakingAudioRetentionWorker",
                Action = "SpeakingSimulationV11ContentExpiredByRetention",
                ResourceType = "SpeakingSession",
                ResourceId = session.Id,
                Details = JsonSerializer.Serialize(new
                {
                    sessionId = session.Id,
                    transcriptRows = transcriptRows.Count,
                    assessmentRows = assessmentRows.Count,
                    reportEvidenceRows = reportEvidence.Count,
                    tutorOverrides = tutorOverrides.Count,
                }),
            });
            redacted++;
        }

        return redacted;
    }

    private static string? RedactReportJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            var report = JsonSerializer.Deserialize<SpeakingSimulationV11AssessmentReport>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (report is null)
            {
                return null;
            }

            var criteria = (report.Criteria ?? Array.Empty<SpeakingSimulationV11CriterionResult>())
                .Select(RedactCriterion)
                .ToArray();
            var cardBreakdowns = (report.CardBreakdowns ?? Array.Empty<SpeakingSimulationV11CardBreakdown>())
                .Select(card => card with
                {
                    Criteria = (card.Criteria ?? Array.Empty<SpeakingSimulationV11CriterionResult>())
                        .Select(RedactCriterion).ToArray(),
                    Strengths = Array.Empty<string>(),
                    Weaknesses = Array.Empty<string>(),
                    TaskMap = (card.TaskMap ?? Array.Empty<SpeakingSimulationV11TaskResult>())
                        .Select(task => task with { Evidence = null }).ToArray(),
                    Timeline = (card.Timeline ?? Array.Empty<SpeakingSimulationV11TimelineItem>())
                        .Select(item => item with { Note = null }).ToArray(),
                    LanguageAnalysis = new Dictionary<string, object?>(),
                    TimeManagement = new Dictionary<string, object?>(),
                    TopFive = Array.Empty<string>(),
                    BetterAlternatives = Array.Empty<SpeakingSimulationV11Alternative>(),
                    Tips = Array.Empty<string>(),
                    PracticePlan = Array.Empty<SpeakingSimulationV11PracticePlanItem>(),
                    SourceTranscriptId = null,
                    SourceRecordingId = null,
                })
                .ToArray();

            var redacted = report with
            {
                OverallSummary = "Detailed transcript evidence expired under the approved retention policy.",
                Criteria = criteria,
                CardBreakdowns = cardBreakdowns,
                Strengths = Array.Empty<string>(),
                Weaknesses = Array.Empty<string>(),
                TaskMap = (report.TaskMap ?? Array.Empty<SpeakingSimulationV11TaskResult>())
                    .Select(task => task with { Evidence = null }).ToArray(),
                Timeline = (report.Timeline ?? Array.Empty<SpeakingSimulationV11TimelineItem>())
                    .Select(item => item with { Note = null }).ToArray(),
                LanguageAnalysis = new Dictionary<string, object?>(),
                TimeManagement = new Dictionary<string, object?>(),
                TopFive = Array.Empty<string>(),
                BetterAlternatives = Array.Empty<SpeakingSimulationV11Alternative>(),
                Tips = Array.Empty<string>(),
                PracticePlan = Array.Empty<SpeakingSimulationV11PracticePlanItem>(),
                SourceTranscriptId = null,
                SourceRecordingId = null,
            };
            return JsonSerializer.Serialize(redacted);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SpeakingSimulationV11CriterionResult RedactCriterion(
        SpeakingSimulationV11CriterionResult criterion)
        => criterion with
        {
            Rationale = "Detailed transcript evidence expired under the approved retention policy.",
            Evidence = Array.Empty<SpeakingSimulationV11EvidenceResult>(),
            Strength = null,
            Weakness = null,
            Action = null,
        };
}
