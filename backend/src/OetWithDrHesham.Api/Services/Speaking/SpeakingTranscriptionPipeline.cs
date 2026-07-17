using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Speaking;

/// <summary>
/// Phase 4 of the OET Speaking module plan (B.2) — orchestrates the ASR
/// transcription workflow for a unified <see cref="SpeakingSession"/>.
///
/// State machine (encoded in <see cref="SpeakingTranscript.Provider"/>):
/// <code>
///   queued → processing → completed
///                       ↘ failed
/// </code>
///
/// The pipeline uses the <see cref="SpeakingTranscript"/> table itself as
/// the queue: a "pending" row is created at enqueue time and is updated
/// in-place as the state advances. This keeps the schema unchanged and
/// guarantees one logical transcription per session at a time. When a
/// completed row exists, it is marked <see cref="SpeakingTranscript.IsLatest"/>
/// and any prior rows for the same session are demoted (so re-transcribing
/// against a higher-confidence engine still works).
///
/// Companion to (but independent from) the legacy
/// <c>SpeakingEvaluationPipeline.CompleteTranscriptionAsync</c> path,
/// which operates on the older <see cref="Attempt"/> model. New code
/// should route through this pipeline.
///
/// DI: <c>ISpeakingTranscriptionProvider</c> and this pipeline are wired
/// up by Agent W2-A in <c>Program.cs</c>.
/// </summary>
public sealed class SpeakingTranscriptionPipeline(
    LearnerDbContext db,
    ISpeakingTranscriptionProvider provider,
    ILogger<SpeakingTranscriptionPipeline> logger)
{
    // ── State markers (encoded in SpeakingTranscript.Provider) ────────
    // We reuse the existing string column to track state so the schema
    // is unchanged. A completed row carries the real provider code
    // (e.g. "whisper" or "mock"); queued/processing/failed rows carry a
    // sentinel that the GetStatus call can detect.
    public const string StateQueued = "__queued__";
    public const string StateProcessing = "__processing__";
    public const string StateFailed = "__failed__";

    private const string EmptySegmentsJson = "[]";
    private const string DefaultLanguage = "en";

    /// <summary>Add a session to the transcription queue. Idempotent — if
    /// there's already a non-failed pending/processing row for this
    /// session, returns without creating a duplicate. The
    /// <paramref name="recordingMediaAssetId"/> is validated to belong to
    /// the session; <see cref="ProcessNextAsync"/> later resolves the most
    /// recent recording on the session to obtain the audio.</summary>
    public async Task EnqueueAsync(
        string speakingSessionId,
        string recordingMediaAssetId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(speakingSessionId))
        {
            throw new ArgumentException("Speaking session id is required.", nameof(speakingSessionId));
        }
        if (string.IsNullOrWhiteSpace(recordingMediaAssetId))
        {
            throw new ArgumentException("Recording media asset id is required.", nameof(recordingMediaAssetId));
        }

        var session = await db.SpeakingSessions
            .FirstOrDefaultAsync(s => s.Id == speakingSessionId, ct)
            ?? throw new InvalidOperationException(
                $"Speaking session '{speakingSessionId}' does not exist.");

        // Validate the recording exists and belongs to this session. The
        // caller passes either the SpeakingRecording.Id or the
        // MediaAsset.Id; we accept both.
        var recording = await db.SpeakingRecordings
            .FirstOrDefaultAsync(r => r.Id == recordingMediaAssetId
                                      || r.MediaAssetId == recordingMediaAssetId, ct)
            ?? throw new InvalidOperationException(
                $"Speaking recording '{recordingMediaAssetId}' does not exist.");
        if (recording.SpeakingSessionId != session.Id)
        {
            throw new InvalidOperationException(
                $"Recording '{recordingMediaAssetId}' does not belong to session '{speakingSessionId}'.");
        }

        // Short-circuit if an active queued/processing row already exists.
        var existing = await db.SpeakingTranscripts
            .Where(t => t.SpeakingSessionId == speakingSessionId
                        && (t.Provider == StateQueued || t.Provider == StateProcessing))
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            logger.LogDebug(
                "Speaking transcription already in-flight for session {SessionId} (transcriptId={TranscriptId}, state={State}).",
                speakingSessionId,
                existing.Id,
                existing.Provider);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var row = new SpeakingTranscript
        {
            Id = Guid.NewGuid().ToString("N"),
            SpeakingSessionId = speakingSessionId,
            Provider = StateQueued,
            Language = DefaultLanguage,
            SegmentsJson = EmptySegmentsJson,
            IsLatest = false,
            WordCount = 0,
            MeanConfidence = 0,
            GeneratedAt = now,
        };
        db.SpeakingTranscripts.Add(row);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Queued ASR transcription for session {SessionId} (recording={RecordingMediaAssetId}, transcriptId={TranscriptId}).",
            speakingSessionId,
            recording.MediaAssetId,
            row.Id);
    }

    /// <summary>Picks the oldest queued transcript row, hands it to the
    /// configured <see cref="ISpeakingTranscriptionProvider"/>, persists
    /// the result, and marks it the latest transcript for its session.
    /// Returns true if a row was processed, false if the queue was
    /// empty.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var row = await db.SpeakingTranscripts
            .Where(t => t.Provider == StateQueued)
            .OrderBy(t => t.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return false;
        }

        var sessionId = row.SpeakingSessionId;
        var languageHint = string.IsNullOrWhiteSpace(row.Language) ? DefaultLanguage : row.Language;

        // Transition to processing before doing any heavy lifting so a
        // crashed worker leaves a visible state.
        row.Provider = StateProcessing;
        row.GeneratedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        // Resolve the most recent recording on the session, then its
        // backing MediaAsset for the storage path. We deliberately
        // re-query at process time so the pipeline always transcribes
        // the latest take if a learner re-recorded between enqueue and
        // processing.
        var recording = await db.SpeakingRecordings
            .Where(r => r.SpeakingSessionId == sessionId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (recording is null)
        {
            MarkFailed(row, "no_recording",
                $"Session '{sessionId}' has no recording to transcribe.",
                retryable: false);
            await db.SaveChangesAsync(ct);
            return true;
        }
        var mediaAsset = await db.MediaAssets
            .FirstOrDefaultAsync(m => m.Id == recording.MediaAssetId, ct);
        if (mediaAsset is null)
        {
            MarkFailed(row, "media_asset_missing",
                $"Media asset '{recording.MediaAssetId}' could not be resolved.",
                retryable: false);
            await db.SaveChangesAsync(ct);
            return true;
        }

        SpeakingTranscriptionProviderResult result;
        try
        {
            result = await provider.TranscribeAsync(
                mediaAsset.StoragePath,
                languageHint,
                ct);
        }
        catch (OperationCanceledException)
        {
            // Re-queue so a later cycle picks it up.
            row.Provider = StateQueued;
            row.Language = languageHint.Length > 8 ? DefaultLanguage : languageHint;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "ASR provider failed for session {SessionId} (transcriptId={TranscriptId}).",
                sessionId,
                row.Id);
            MarkFailed(row, "provider_error", ex.Message, retryable: true);
            await db.SaveChangesAsync(ct);
            return true;
        }

        // Demote prior latest rows for this session before promoting
        // this one — there is at most one IsLatest row per session.
        var priorLatestRows = await db.SpeakingTranscripts
            .Where(t => t.SpeakingSessionId == sessionId
                        && t.Id != row.Id
                        && t.IsLatest)
            .ToListAsync(ct);
        foreach (var prior in priorLatestRows)
        {
            prior.IsLatest = false;
        }

        row.Provider = string.IsNullOrWhiteSpace(result.Provider) ? provider.ProviderCode : result.Provider;
        row.Language = string.IsNullOrWhiteSpace(result.Language) ? DefaultLanguage : result.Language;
        row.SegmentsJson = string.IsNullOrWhiteSpace(result.SegmentsJson) ? EmptySegmentsJson : result.SegmentsJson;
        row.WordCount = Math.Max(0, result.WordCount);
        row.MeanConfidence = Math.Clamp(result.MeanConfidence, 0d, 1d);
        row.IsLatest = true;
        row.GeneratedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Completed ASR transcription for session {SessionId} (transcriptId={TranscriptId}, words={WordCount}, conf={Confidence:F2}).",
            sessionId,
            row.Id,
            row.WordCount,
            row.MeanConfidence);

        return true;
    }

    /// <summary>Returns the current state-machine snapshot for the
    /// session. If no transcription has ever been enqueued, returns an
    /// <c>idle</c> status.</summary>
    public async Task<SpeakingTranscriptionStatus> GetStatusAsync(
        string speakingSessionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(speakingSessionId))
        {
            throw new ArgumentException("Speaking session id is required.", nameof(speakingSessionId));
        }

        var rows = await db.SpeakingTranscripts
            .Where(t => t.SpeakingSessionId == speakingSessionId)
            .OrderByDescending(t => t.GeneratedAt)
            .Take(8)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return new SpeakingTranscriptionStatus
            {
                SpeakingSessionId = speakingSessionId,
                State = "idle",
                StatusReasonCode = "no_transcription",
                StatusMessage = "No transcription has been requested for this session.",
                Retryable = false,
                RetryCount = 0,
            };
        }

        // The most recent row is the canonical state.
        var head = rows[0];
        var (state, reason, message, retryable) = head.Provider switch
        {
            StateQueued => ("queued", "pending", "Transcription is queued.", false),
            StateProcessing => ("processing", "running", "Transcription is being generated.", false),
            StateFailed => ("failed", "provider_error",
                            ExtractFailureMessage(head.SegmentsJson) ?? "Transcription failed.", true),
            _ => ("completed", "completed", "Transcription completed.", false),
        };

        var latest = rows.FirstOrDefault(r => r.IsLatest && IsTerminalProvider(r.Provider));

        return new SpeakingTranscriptionStatus
        {
            SpeakingSessionId = speakingSessionId,
            State = state,
            StatusReasonCode = reason,
            StatusMessage = message,
            Retryable = retryable,
            // RetryCount mirrors the number of failed attempts on this
            // session, useful for surfacing in the UI.
            RetryCount = rows.Count(r => r.Provider == StateFailed),
            QueuedAt = head.GeneratedAt,
            CompletedAt = latest?.GeneratedAt,
            LatestTranscriptId = latest?.Id,
            Provider = latest?.Provider,
            Language = latest?.Language,
            WordCount = latest?.WordCount,
            MeanConfidence = latest?.MeanConfidence,
        };
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private static void MarkFailed(SpeakingTranscript row, string reasonCode, string message, bool retryable)
    {
        row.Provider = StateFailed;
        // Stash the failure detail in the otherwise-empty SegmentsJson so
        // GetStatusAsync can surface it without a separate column.
        row.SegmentsJson = $"{{\"failure\":{{\"reasonCode\":\"{Escape(reasonCode)}\",\"message\":\"{Escape(message)}\",\"retryable\":{(retryable ? "true" : "false")}}}}}";
        row.IsLatest = false;
        row.GeneratedAt = DateTimeOffset.UtcNow;
    }

    private static string Escape(string value)
        => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string? ExtractFailureMessage(string segmentsJson)
    {
        if (string.IsNullOrWhiteSpace(segmentsJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(segmentsJson);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("failure", out var failure)
                && failure.TryGetProperty("message", out var message)
                && message.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch
        {
            // segmentsJson wasn't a failure envelope — surface nothing.
        }
        return null;
    }

    private static bool IsTerminalProvider(string provider)
        => !string.IsNullOrEmpty(provider)
           && provider != StateQueued
           && provider != StateProcessing
           && provider != StateFailed;
}
