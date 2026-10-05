using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

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
    ILogger<SpeakingTranscriptionPipeline> logger,
    ISpeakingCanonicalAssessmentService? canonical = null)
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

    /// <summary>
    /// A row is <c>__processing__</c> for one ASR call (the HTTP client's own
    /// timeout is 100 s) plus a few queries, and <see cref="SpeakingTranscript.GeneratedAt"/>
    /// is stamped when it enters that state. A row older than this lost its worker
    /// (process killed mid-ASR) and is put back in the queue by
    /// <see cref="RequeueStaleProcessingAsync(CancellationToken)"/>.
    /// </summary>
    internal static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Ceiling for the inline grade that follows a landed transcript. A healthy
    /// Max-reasoning grade takes ~12 minutes, so this only bounds a hung call. It
    /// stays under <see cref="Ai.AiOperationWorker.LeaseDuration"/> (30 minutes) so
    /// the hand-back below happens before the operation's lease would expire.
    /// </summary>
    internal static readonly TimeSpan InlineAssessCeiling = TimeSpan.FromMinutes(20);

    /// <summary>How many times one pass re-reads the queue after losing a claim race.</summary>
    private const int MaxClaimAttempts = 5;

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

    /// <summary>Claims the oldest queued transcript row (atomically: see
    /// <see cref="ClaimNextQueuedAsync"/>), hands it to the configured
    /// <see cref="ISpeakingTranscriptionProvider"/>, persists the result, and
    /// marks it the latest transcript for its session. Returns true if a row was
    /// processed, false if the queue was empty.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        // The claim moves the row to processing before any heavy lifting, so a
        // crashed worker leaves a visible state (see RequeueStaleProcessingAsync).
        var claimedId = await ClaimNextQueuedAsync(ct);
        if (claimedId is null)
        {
            return false;
        }

        var row = await LoadFreshAsync(claimedId, ct);
        if (row is null)
        {
            // Deleted between the claim and the load: nothing left to do for it.
            return true;
        }

        var sessionId = row.SpeakingSessionId;
        var languageHint = string.IsNullOrWhiteSpace(row.Language) ? DefaultLanguage : row.Language;

        // Resolve the most recent recording on the session, then its
        // backing MediaAsset for the storage path. We deliberately
        // re-query at process time so the pipeline always transcribes
        // the latest take if a learner re-recorded between enqueue and
        // processing.
        // A recorder-fallback upload is the whole role-play, so it wins over
        // any shorter per-turn recording on the same session.
        var fallbackRecordingId = SpeakingSessionRecordingService.RecordingIdFor(sessionId);
        var recording = await db.SpeakingRecordings
                .FirstOrDefaultAsync(r => r.Id == fallbackRecordingId, ct)
            ?? await db.SpeakingRecordings
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

        if (!HasUsableSegments(result))
        {
            // Never grade an empty transcript: surface it as a failed
            // transcription the learner can see instead of a silent 0 score.
            MarkFailed(row, "no_speech",
                "No speech could be detected in the recording.",
                retryable: false);
            await db.SaveChangesAsync(ct);
            return true;
        }

        await PromoteLatestAsync(db, row, result, provider.ProviderCode, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Completed ASR transcription for session {SessionId} (transcriptId={TranscriptId}, words={WordCount}, conf={Confidence:F2}).",
            sessionId,
            row.Id,
            row.WordCount,
            row.MeanConfidence);

        await AssessIfRequestedAsync(sessionId, ct);
        return true;
    }

    /// <summary>
    /// Atomically moves the oldest queued row to <c>__processing__</c> (stamping
    /// <see cref="SpeakingTranscript.GeneratedAt"/> as the start of the lease) and
    /// returns its id; null when the queue is empty. Every process polls this
    /// queue, and the old read-then-write let two of them take the same row and
    /// call the paid ASR provider twice. On a relational provider the move is a
    /// compare-and-swap (<c>WHERE Id = x AND Provider = '__queued__'</c>), so
    /// exactly one claimant sees one affected row; a loser re-reads the queue and
    /// tries the next row.
    /// </summary>
    internal async Task<string?> ClaimNextQueuedAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxClaimAttempts; attempt++)
        {
            var candidateId = await db.SpeakingTranscripts.AsNoTracking()
                .Where(t => t.Provider == StateQueued)
                .OrderBy(t => t.GeneratedAt)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(ct);
            if (candidateId is null)
            {
                return null;
            }

            var claimedAt = DateTimeOffset.UtcNow;
            if (db.Database.IsRelational())
            {
                var rows = await db.SpeakingTranscripts
                    .Where(t => t.Id == candidateId && t.Provider == StateQueued)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(t => t.Provider, StateProcessing)
                        .SetProperty(t => t.GeneratedAt, claimedAt), ct);
                if (rows == 1)
                {
                    return candidateId;
                }
            }
            else
            {
                // The in-memory test provider has no ExecuteUpdate; it is
                // single-process, so load + conditional save loses no atomicity.
                var row = await db.SpeakingTranscripts
                    .FirstOrDefaultAsync(t => t.Id == candidateId && t.Provider == StateQueued, ct);
                if (row is not null)
                {
                    row.Provider = StateProcessing;
                    row.GeneratedAt = claimedAt;
                    await db.SaveChangesAsync(ct);
                    return candidateId;
                }
            }

            // Lost the race (another claimant moved it first): look again.
        }

        return null;
    }

    /// <summary>
    /// Puts rows orphaned in <c>__processing__</c> back in the queue: the worker
    /// that claimed them died before finishing (nothing else ever leaves that
    /// state), so they would otherwise stay "being generated" forever. Keeps the
    /// row's original timestamp, which puts it at the front of the queue.
    /// </summary>
    public Task<int> RequeueStaleProcessingAsync(CancellationToken ct)
        => RequeueStaleProcessingAsync(DateTimeOffset.UtcNow, ct);

    internal async Task<int> RequeueStaleProcessingAsync(DateTimeOffset now, CancellationToken ct)
    {
        var staleBefore = now - ProcessingLease;
        int requeued;
        if (db.Database.IsRelational())
        {
            requeued = await db.SpeakingTranscripts
                .Where(t => t.Provider == StateProcessing && t.GeneratedAt < staleBefore)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.Provider, StateQueued), ct);
        }
        else
        {
            var stale = await db.SpeakingTranscripts
                .Where(t => t.Provider == StateProcessing && t.GeneratedAt < staleBefore)
                .ToListAsync(ct);
            foreach (var row in stale)
            {
                row.Provider = StateQueued;
            }

            if (stale.Count > 0)
            {
                await db.SaveChangesAsync(ct);
            }

            requeued = stale.Count;
        }

        if (requeued > 0)
        {
            logger.LogWarning(
                "Re-queued {Count} Speaking transcription row(s) orphaned in processing for over {Lease} (worker lost).",
                requeued, ProcessingLease);
        }

        return requeued;
    }

    /// <summary>Loads the row as it is in the database now. A stale tracked copy
    /// (this scope enqueued it, or an earlier pass here already saw it) would
    /// otherwise win over the claim that just changed the row underneath it, and
    /// later writes that happen to match its stale original values would be
    /// dropped.</summary>
    private async Task<SpeakingTranscript?> LoadFreshAsync(string transcriptId, CancellationToken ct)
    {
        var tracked = db.ChangeTracker.Entries<SpeakingTranscript>()
            .FirstOrDefault(entry => entry.Entity.Id == transcriptId);
        if (tracked is not null)
        {
            tracked.State = EntityState.Detached;
        }

        return await db.SpeakingTranscripts.FirstOrDefaultAsync(t => t.Id == transcriptId, ct);
    }

    /// <summary>
    /// Recorder fallback: once the transcript lands, run the assessment the
    /// learner already asked for (<c>/ai-assess</c> recorded the canonical
    /// operation) or implied by submitting (<c>/submit</c> stamped
    /// SubmittedAt). Failures are persisted by the canonical service and
    /// surface as a retryable result, so they never break the queue.
    ///
    /// <para>
    /// The grade runs inline here, so it is bounded by
    /// <see cref="InlineAssessCeiling"/>. If that ceiling fires, the canonical
    /// service has already handed the operation back to the AI worker queue (it
    /// does so for any cancellation of the token it was given), so the learner's
    /// grade is not lost and this loop is released.
    /// </para>
    /// </summary>
    private async Task AssessIfRequestedAsync(string sessionId, CancellationToken ct)
    {
        if (canonical is null) return;

        var submitted = await db.SpeakingSessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.SubmittedAt != null, ct);
        var requested = submitted || await db.AiOperations.AsNoTracking()
            .AnyAsync(o => o.FeatureCode == AiFeatureCodes.SpeakingGrade
                && o.ResourceType == "speaking_session"
                && o.ResourceId == sessionId, ct);
        if (!requested) return;

        using var ceiling = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ceiling.CancelAfter(InlineAssessCeiling);
        try
        {
            await canonical.AssessNowAsync(sessionId, ceiling.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && ceiling.IsCancellationRequested)
        {
            // Only OUR ceiling counts as "handed back": the canonical service requeues the
            // operation for any cancellation of the token it was given.
            logger.LogWarning(
                "Inline assessment for session {SessionId} hit the {Ceiling} ceiling; handed back to the AI worker queue.",
                sessionId, InlineAssessCeiling);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Any other failure - including a cancellation this ceiling did not cause (an
            // HttpClient timeout surfaces as TaskCanceledException, and the canonical service
            // does not hand those back) - is a failed auto-assessment the learner can retry.
            // Only a real shutdown (ct) propagates.
            logger.LogWarning(ex,
                "Auto-assessment after transcription failed for session {SessionId}; the learner can retry.",
                sessionId);
        }
    }

    /// <summary>True when an ASR result carries at least one transcript
    /// segment. Shared gate for the attempt bridge
    /// (<c>SpeakingEvaluationPipeline</c>) and this session queue.</summary>
    public static bool HasUsableSegments([NotNullWhen(true)] SpeakingTranscriptionProviderResult? result)
        => result is not null
           && !string.IsNullOrWhiteSpace(result.SegmentsJson)
           && result.SegmentsJson.Trim() != EmptySegmentsJson;

    /// <summary>
    /// Writes an ASR result into <paramref name="row"/> and makes it the
    /// session's single latest transcript (prior latest rows are demoted).
    /// Shared by the attempt bridge and <see cref="ProcessNextAsync"/> so
    /// both grading paths read an identically-shaped transcript. The caller
    /// saves.
    /// </summary>
    public static async Task PromoteLatestAsync(
        LearnerDbContext db,
        SpeakingTranscript row,
        SpeakingTranscriptionProviderResult result,
        string fallbackProvider,
        CancellationToken ct)
    {
        var priorLatestRows = await db.SpeakingTranscripts
            .Where(t => t.SpeakingSessionId == row.SpeakingSessionId
                        && t.Id != row.Id
                        && t.IsLatest)
            .ToListAsync(ct);
        foreach (var prior in priorLatestRows)
        {
            prior.IsLatest = false;
        }

        row.Provider = string.IsNullOrWhiteSpace(result.Provider) ? fallbackProvider : result.Provider;
        row.Language = string.IsNullOrWhiteSpace(result.Language) ? DefaultLanguage : result.Language;
        row.SegmentsJson = string.IsNullOrWhiteSpace(result.SegmentsJson) ? EmptySegmentsJson : result.SegmentsJson;
        row.WordCount = Math.Max(0, result.WordCount);
        row.MeanConfidence = Math.Clamp(result.MeanConfidence, 0d, 1d);
        row.IsLatest = true;
        row.GeneratedAt = DateTimeOffset.UtcNow;
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
            StateFailed => ("failed",
                            ReadFailureField(head.SegmentsJson, "reasonCode") ?? "provider_error",
                            ReadFailureField(head.SegmentsJson, "message") ?? "Transcription failed.",
                            ReadFailureField(head.SegmentsJson, "reasonCode") != "no_speech"),
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

    /// <summary>Reads a field (<c>reasonCode</c> / <c>message</c>) of the
    /// failure envelope <see cref="MarkFailed"/> stashes in a failed row's
    /// SegmentsJson; null when the row is not a failure envelope.</summary>
    public static string? ReadFailureField(string segmentsJson, string field)
    {
        if (string.IsNullOrWhiteSpace(segmentsJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(segmentsJson);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("failure", out var failure)
                && failure.TryGetProperty(field, out var value)
                && value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return value.GetString();
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
