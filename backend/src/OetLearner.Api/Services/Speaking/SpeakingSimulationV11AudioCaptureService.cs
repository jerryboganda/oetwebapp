using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Conversation;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Persists the original learner audio received by the v1.1 role-play loop.
/// Each audio turn gets its own immutable MediaAsset/SpeakingRecording pair;
/// the transcript segment stores the recording id so every assessed quote can
/// be traced back to the exact source audio and timestamp.
/// </summary>
public sealed class SpeakingSimulationV11AudioCaptureService(
    LearnerDbContext db,
    IFileStorage storage,
    IConversationOptionsProvider conversationOptions,
    IOptions<SpeakingComplianceOptions> complianceOptions,
    ILogger<SpeakingSimulationV11AudioCaptureService> logger)
{
    public async Task<SpeakingSimulationV11AudioCaptureResult> CaptureTurnAsync(
        SpeakingSession session,
        byte[] audio,
        string mimeType,
        bool isWarmup,
        long? durationMs,
        CancellationToken ct)
    {
        if (audio.Length == 0)
        {
            throw ApiException.Validation("SPEAKING_AUDIO_EMPTY", "No audio was received.");
        }

        var requiredConsentTypes = session.Mode == SpeakingSessionMode.LiveTutor
            ? new[]
            {
                SpeakingComplianceConsentTypes.Recording,
                SpeakingComplianceConsentTypes.TutorReview,
                SpeakingComplianceConsentTypes.Retention,
                SpeakingComplianceConsentTypes.LiveVideoWithTutor,
            }
            : new[]
            {
                SpeakingComplianceConsentTypes.Recording,
                SpeakingComplianceConsentTypes.AiProcessing,
                SpeakingComplianceConsentTypes.Retention,
            };
        var activeConsentTypes = await db.SpeakingComplianceConsents
            .AsNoTracking()
            .Where(x => x.UserId == session.UserId
                && x.RevokedAt == null
                && requiredConsentTypes.Contains(x.ConsentType))
            .Select(x => x.ConsentType)
            .Distinct()
            .ToListAsync(ct);
        if (requiredConsentTypes.Any(type => !activeConsentTypes.Contains(type, StringComparer.OrdinalIgnoreCase)))
        {
            throw ApiException.Forbidden(
                "SPEAKING_CONSENT_REQUIRED",
                "Recording, processing, tutor-review, and retention consent must be active before audio is stored.");
        }

        var options = await conversationOptions.GetAsync(ct);
        var normalizedMimeType = string.IsNullOrWhiteSpace(mimeType)
            ? "audio/webm"
            : mimeType.Trim().ToLowerInvariant();

        if (!options.AllowedMimeTypes.Contains(normalizedMimeType, StringComparer.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "SPEAKING_AUDIO_MIME_NOT_ALLOWED",
                $"Audio MIME '{normalizedMimeType}' is not allowed.");
        }

        if (audio.LongLength > options.MaxAudioBytes)
        {
            throw ApiException.Validation(
                "SPEAKING_AUDIO_TOO_LARGE",
                $"Audio exceeds the configured limit of {options.MaxAudioBytes} bytes.");
        }

        var now = DateTimeOffset.UtcNow;
        var recordingId = $"spv11_rec_{Guid.NewGuid():N}";
        var mediaAssetId = $"spv11_media_{Guid.NewGuid():N}";
        var sha256 = Convert.ToHexString(SHA256.HashData(audio)).ToLowerInvariant();
        var extension = GuessExtension(normalizedMimeType);
        var storageKey = $"speaking-simulation-v11/audio/{session.Id}/{recordingId}-{sha256}.{extension}";

        // Resolve the approved v1.1 retention before writing the blob. This
        // keeps a cancelled/failed settings query from creating an orphaned
        // object with no authoritative expiry row.
        var approvedRetentionValue = await db.SpeakingSimulationV11OwnerApprovals
            .AsNoTracking()
            .Where(x => x.ApprovalKey == "retention_days"
                && x.ScopeKey == "global"
                && x.Status == SpeakingSimulationV11ApprovalStatus.Approved
                && x.SpecVersion == SpeakingSimulationV11Contracts.SpecVersion
                && x.RubricVersion == SpeakingSimulationV11Contracts.RubricVersion
                && x.NumericValue > 0)
            .OrderByDescending(x => x.ApprovedAt ?? x.UpdatedAt)
            .Select(x => x.NumericValue)
            .FirstOrDefaultAsync(ct);
        var approvedRetentionDays = approvedRetentionValue is > 0
            ? (int?)Math.Min((decimal)int.MaxValue, approvedRetentionValue.Value)
            : null;
        var retentionDays = Math.Max(
            1,
            approvedRetentionDays ?? complianceOptions.Value.RetentionDaysDefault);

        try
        {
            await using var source = new MemoryStream(audio, writable: false);
            await storage.WriteAsync(storageKey, source, ct);
        }
        catch
        {
            // The storage abstraction owns the actual blob. If the database
            // write below fails, the orphan sweep can remove this namespaced
            // object without exposing a partially persisted recording row.
            throw;
        }

        var durationSeconds = durationMs is > 0
            ? Math.Max(1, (int)Math.Ceiling(durationMs.Value / 1000d))
            : 0;

        db.MediaAssets.Add(new MediaAsset
        {
            Id = mediaAssetId,
            OriginalFilename = $"{recordingId}.{extension}",
            MimeType = normalizedMimeType,
            Format = extension,
            SizeBytes = audio.LongLength,
            DurationSeconds = durationSeconds > 0 ? durationSeconds : null,
            StoragePath = storageKey,
            Status = MediaAssetStatus.Ready,
            Sha256 = sha256,
            MediaKind = "audio",
            UploadedBy = session.UserId,
            UploadedAt = now,
            ProcessedAt = now,
        });

        db.SpeakingRecordings.Add(new SpeakingRecording
        {
            Id = recordingId,
            SpeakingSessionId = session.Id,
            MediaAssetId = mediaAssetId,
            Kind = SpeakingRecordingKind.Audio,
            Source = SpeakingRecordingSource.ConversationHub,
            DurationSeconds = durationSeconds,
            SizeBytes = audio.LongLength,
            Sha256 = sha256,
            MimeType = normalizedMimeType,
            ConsentVersion = string.IsNullOrWhiteSpace(session.ConsentVersion)
                ? "recording.v1"
                : session.ConsentVersion,
            IsArchived = false,
            RetentionExpiresAt = retentionDays > 0 ? now.AddDays(retentionDays) : null,
            IsWarmup = isWarmup,
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Keep the immutable blob namespace free of an object that has no
            // authoritative database row. Cleanup is best-effort because the
            // original persistence failure is the actionable error.
            try
            {
                await storage.DeleteAsync(storageKey, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                logger.LogWarning(cleanupException,
                    "Could not clean up orphaned v1.1 speaking audio {StorageKey} after database failure.",
                    storageKey);
            }

            throw;
        }
        logger.LogDebug(
            "Persisted v1.1 speaking audio turn {RecordingId} for session {SessionId} ({Bytes} bytes, warmup={IsWarmup}).",
            recordingId,
            session.Id,
            audio.LongLength,
            isWarmup);

        return new SpeakingSimulationV11AudioCaptureResult(
            recordingId,
            mediaAssetId,
            storageKey,
            sha256,
            audio.LongLength,
            normalizedMimeType,
            durationSeconds,
            isWarmup);
    }

    private static string GuessExtension(string mimeType) => mimeType switch
    {
        "audio/mpeg" => "mp3",
        "audio/ogg" => "ogg",
        "audio/mp4" => "m4a",
        "audio/wav" or "audio/x-wav" => "wav",
        _ => "webm",
    };
}

public sealed record SpeakingSimulationV11AudioCaptureResult(
    string RecordingId,
    string MediaAssetId,
    string StorageKey,
    string Sha256,
    long SizeBytes,
    string MimeType,
    int DurationSeconds,
    bool IsWarmup);
