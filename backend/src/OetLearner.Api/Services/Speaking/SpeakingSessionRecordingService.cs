using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Recorder fallback for the shared Speaking session engine (owner decision
/// 23 Sep 2026): when no realtime voice provider is available the candidate
/// records the 5-minute role-play in the browser and uploads it once. The
/// audio is stored like any other session recording, transcribed by the
/// existing session transcription queue (<see cref="SpeakingTranscriptionPipeline"/>,
/// same <see cref="ISpeakingTranscriptionProvider"/> as the legacy attempt
/// pipeline) and graded by the existing classic assessor. Real audio, real
/// STT, real grading; nothing is mocked.
/// </summary>
public sealed class SpeakingSessionRecordingService(
    LearnerDbContext db,
    IFileStorage storage,
    SpeakingTranscriptionPipeline transcription,
    IOptions<SpeakingComplianceOptions> complianceOptions,
    ILogger<SpeakingSessionRecordingService> logger,
    IOptions<StorageOptions>? storageOptions = null,
    IOptions<LiveVoiceOptions>? liveVoiceOptions = null)
{
    private static readonly Dictionary<string, string> ExtensionByMime = new(StringComparer.Ordinal)
    {
        ["audio/webm"] = "webm",
        ["audio/ogg"] = "ogg",
        ["audio/mp4"] = "m4a",
        ["audio/mpeg"] = "mp3",
        ["audio/wav"] = "wav",
        ["audio/x-wav"] = "wav",
    };

    /// <summary>One fallback recording per session, keyed deterministically so
    /// a concurrent second upload loses on the primary key and the assessor
    /// can recognise a recorder-fallback session.</summary>
    public static string RecordingIdFor(string sessionId) => $"srec_{sessionId}";

    public long MaxUploadBytes { get; } = storageOptions?.Value.MaxUploadBytes is long max && max > 0
        ? max
        : 25L * 1024 * 1024;

    /// <summary>
    /// Stores the session's role-play recording and queues its transcription.
    /// Returns false when a recording was already received for this session
    /// (the caller answers 409 <c>recording_already_received</c>).
    /// </summary>
    public async Task<bool> ReceiveAsync(
        string userId,
        string sessionId,
        Stream audio,
        string? contentType,
        long declaredLength,
        int? durationSeconds,
        CancellationToken ct)
    {
        var session = await db.SpeakingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || !string.Equals(session.UserId, userId, StringComparison.Ordinal))
        {
            throw ApiException.NotFound("speaking_session_not_found", "That Speaking session does not exist.");
        }
        await RequireOwnProfessionAsync(userId, session.RolePlayCardId, ct);

        if (session.Mode == SpeakingSessionMode.LiveTutor)
        {
            throw ApiException.Conflict("speaking_recording_ai_mode_required",
                "Live-tutor sessions are recorded in the tutor room, not uploaded.");
        }
        if (session.State is not (SpeakingSessionState.Active or SpeakingSessionState.Finished))
        {
            throw ApiException.Conflict("speaking_session_invalid_state",
                $"Upload the recording after the role-play has started (current: {SpeakingSessionStates.ToCode(session.State)}).");
        }
        if (session.ConsentAcceptedAt is null)
        {
            throw ApiException.Conflict("speaking_consent_required",
                "Accept the Speaking recording consent before uploading a recording.");
        }

        // A repeat of an upload that already landed (its response was lost) is always answered
        // recording_already_received, even after the write window below has closed: the client
        // treats that one 409 as success, and any other 409 as a refusal.
        var recordingId = RecordingIdFor(sessionId);
        if (await db.SpeakingRecordings.AsNoTracking().AnyAsync(r => r.Id == recordingId, ct))
        {
            return false;
        }

        // Same write window as the live voice transcript: bounded after the role-play ended, so a
        // recording cannot be attached to a long-finished (and graded) session.
        var cardSeconds = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.Id == session.RolePlayCardId)
            .Select(c => c.RolePlayTimeSeconds)
            .FirstOrDefaultAsync(ct);
        var voiceOptions = liveVoiceOptions?.Value;
        if (!SpeakingRolePlayLimits.IsWithinWriteWindow(session, cardSeconds, DateTimeOffset.UtcNow, voiceOptions))
        {
            throw ApiException.Conflict("live_voice_transcript_window_closed",
                "The window for uploading this role-play recording has closed.");
        }

        var mimeType = (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();
        if (!ExtensionByMime.TryGetValue(mimeType, out var extension))
        {
            throw ApiException.Validation("unsupported_audio_content_type",
                "Only WebM, Ogg, MP4, MPEG or WAV audio recordings can be uploaded.");
        }
        if (declaredLength <= 0)
        {
            throw ApiException.Validation("empty_audio_upload", "The uploaded audio file was empty.");
        }
        if (declaredLength > MaxUploadBytes)
        {
            throw ApiException.Validation("audio_file_too_large",
                $"Audio uploads must be {MaxUploadBytes / (1024 * 1024)} MB or smaller.");
        }

        // Unique blob key per upload: a racing duplicate deletes only its own
        // blob, never the winner's.
        var mediaAssetId = $"smed_{Guid.NewGuid():N}";
        var storageKey = $"speaking/sessions/{sessionId}/{mediaAssetId}.{extension}";
        var sizeBytes = await storage.WriteAsync(storageKey, audio, ct);
        if (sizeBytes <= 0)
        {
            await storage.DeleteAsync(storageKey, CancellationToken.None);
            throw ApiException.Validation("empty_audio_upload", "The uploaded audio file was empty.");
        }

        var now = DateTimeOffset.UtcNow;
        var retentionDays = Math.Max(1, complianceOptions.Value.RetentionDaysDefault);
        // The declared length is client-supplied and nothing decodes the audio here, so it is only
        // trusted up to the longest a role-play plus its flush window can be.
        var longestPossible = SpeakingRolePlayLimits.CeilingSeconds(voiceOptions)
            + SpeakingRolePlayLimits.GraceSeconds(voiceOptions)
            + SpeakingRolePlayLimits.FlushSeconds(voiceOptions);
        var duration = durationSeconds is > 0 ? Math.Min(durationSeconds.Value, longestPossible) : 0;
        db.MediaAssets.Add(new MediaAsset
        {
            Id = mediaAssetId,
            OriginalFilename = $"{recordingId}.{extension}",
            MimeType = mimeType,
            Format = extension,
            SizeBytes = sizeBytes,
            DurationSeconds = duration > 0 ? duration : null,
            StoragePath = storageKey,
            Status = MediaAssetStatus.Ready,
            MediaKind = "audio",
            UploadedBy = userId,
            UploadedAt = now,
            ProcessedAt = now,
        });
        db.SpeakingRecordings.Add(new SpeakingRecording
        {
            Id = recordingId,
            SpeakingSessionId = sessionId,
            MediaAssetId = mediaAssetId,
            Kind = SpeakingRecordingKind.Audio,
            Source = SpeakingRecordingSource.ClientMediaRecorder,
            DurationSeconds = duration,
            SizeBytes = sizeBytes,
            Sha256 = string.Empty,
            MimeType = mimeType,
            ConsentVersion = string.IsNullOrWhiteSpace(session.ConsentVersion) ? "recording.v1" : session.ConsentVersion,
            IsArchived = false,
            RetentionExpiresAt = now.AddDays(retentionDays),
            IsWarmup = false,
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            await TryDeleteAsync(storageKey);
            if (await db.SpeakingRecordings.AsNoTracking().AnyAsync(r => r.Id == recordingId, ct))
            {
                return false;
            }
            throw;
        }

        await transcription.EnqueueAsync(sessionId, recordingId, ct);
        logger.LogInformation(
            "Stored recorder-fallback audio for Speaking session {SessionId} ({Bytes} bytes); transcription queued.",
            sessionId, sizeBytes);
        return true;
    }

    /// <summary>
    /// <c>POST /ai-assess</c> on a recorder-fallback session whose transcript
    /// is not ready: records the assessment request (the transcription queue
    /// runs it as soon as the transcript lands) and returns true so the
    /// caller answers 202 <c>processing</c>. A failed transcription is
    /// re-queued, which is the learner's retry. Returns false when the
    /// assessment can run now.
    /// </summary>
    public async Task<bool> DeferAssessmentUntilTranscribedAsync(
        string sessionId,
        ISpeakingCanonicalAssessmentService canonical,
        CancellationToken ct)
    {
        var recordingId = RecordingIdFor(sessionId);
        if (!await db.SpeakingRecordings.AsNoTracking().AnyAsync(r => r.Id == recordingId, ct)
            || await db.SpeakingTranscripts.AsNoTracking()
                .AnyAsync(t => t.SpeakingSessionId == sessionId && t.IsLatest, ct))
        {
            return false;
        }

        var status = await transcription.GetStatusAsync(sessionId, ct);
        if (status.State is "failed" or "idle")
        {
            if (status.StatusReasonCode == "no_speech")
            {
                throw ApiException.Conflict("speaking_no_speech_detected",
                    status.StatusMessage);
            }
            await transcription.EnqueueAsync(sessionId, recordingId, ct);
        }

        var ticket = await canonical.EnqueueAsync(sessionId, ct);
        if (ticket.State == AiOperationState.FailedTerminal)
        {
            var operation = await db.AiOperations.FirstOrDefaultAsync(o => o.Id == ticket.OperationId, ct);
            if (operation is not null)
            {
                operation.State = AiOperationState.Queued;
                operation.NextAttemptAt = null;
                operation.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        return true;
    }

    private async Task RequireOwnProfessionAsync(string userId, string cardId, CancellationToken ct)
    {
        var cardProfession = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.Id == cardId)
            .Select(c => c.ProfessionId)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(cardProfession)) return;

        var ownProfession = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.ActiveProfessionId)
            .FirstOrDefaultAsync(ct);
        if (!string.Equals(cardProfession.Trim(), ownProfession?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            // 404, never 403: do not reveal the session exists.
            throw ApiException.NotFound("speaking_session_not_found", "That Speaking session does not exist.");
        }
    }

    private async Task TryDeleteAsync(string storageKey)
    {
        try
        {
            await storage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete duplicate Speaking recording blob {StorageKey}.", storageKey);
        }
    }
}
