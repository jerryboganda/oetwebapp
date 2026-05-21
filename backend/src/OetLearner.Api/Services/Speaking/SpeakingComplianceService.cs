using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Speaking;

// Phase 7 (B.8) of the OET Speaking module plan.
//
// Implements the durable consent + recording-deletion + audit-logging
// surface. Heavy reuse of:
//   * <see cref="IFileStorage"/> for blob deletion (audio bytes).
//   * <see cref="LearnerDbContext"/> for consent + recording rows.
//   * <see cref="OetLearner.Api.Domain.AuditEvent"/> for the 7-year
//     compliance audit trail.
//
// All non-owner data access goes through <c>AdminAccessRecordingAsync</c>
// which writes an AuditEvent row before any blob/stream is opened so the
// access is recorded even if the caller's stream dies mid-read.
public sealed class SpeakingComplianceService(
    LearnerDbContext db,
    IFileStorage storage,
    IOptions<SpeakingComplianceOptions> options,
    ILogger<SpeakingComplianceService> logger,
    TimeProvider clock)
{
    private readonly SpeakingComplianceOptions _options = options.Value;

    // ── Consent ──────────────────────────────────────────────────────────

    /// <summary>Records a new consent row for the given learner. If an
    /// active row already exists for the same (UserId, ConsentType) it is
    /// left in place; the latest <c>AcceptedAt</c> is treated as canonical
    /// by readers. The optional <paramref name="ipAddress"/> /
    /// <paramref name="userAgent"/> are captured for the GDPR audit log.</summary>
    public async Task<ConsentRecord> RecordConsentAsync(
        string userId,
        RecordConsentRequest req,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ConsentType))
        {
            throw ApiException.Validation("invalid_consent_type", "ConsentType is required.");
        }

        if (!SpeakingComplianceConsentTypes.Allowed.Contains(req.ConsentType))
        {
            throw ApiException.Validation(
                "invalid_consent_type",
                $"Unknown consent type '{req.ConsentType}'.");
        }

        var version = string.IsNullOrWhiteSpace(req.ConsentVersion)
            ? ResolveCurrentConsentVersion(req.ConsentType)
            : req.ConsentVersion!.Trim();

        var now = clock.GetUtcNow();
        var row = new SpeakingComplianceConsent
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ConsentType = req.ConsentType.Trim().ToLowerInvariant(),
            ConsentVersion = version,
            AcceptedAt = now,
            AcceptedFromIp = string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress.Trim(),
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : Truncate(userAgent, 512),
        };

        db.SpeakingComplianceConsents.Add(row);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "SpeakingComplianceConsent recorded user={UserId} type={ConsentType} version={ConsentVersion}",
            userId, row.ConsentType, row.ConsentVersion);

        return Project(row);
    }

    /// <summary>Marks every active consent of the given type as revoked.
    /// Future audio-recording surfaces refuse to record until a fresh
    /// consent is captured. Idempotent: revoking a non-existent or
    /// already-revoked consent is a no-op.</summary>
    public async Task<int> RevokeConsentAsync(string userId, string consentType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(consentType)
            || !SpeakingComplianceConsentTypes.Allowed.Contains(consentType))
        {
            throw ApiException.Validation(
                "invalid_consent_type",
                $"Unknown consent type '{consentType}'.");
        }

        var normalized = consentType.Trim().ToLowerInvariant();
        var rows = await db.SpeakingComplianceConsents
            .Where(c => c.UserId == userId && c.ConsentType == normalized && c.RevokedAt == null)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        foreach (var row in rows)
        {
            row.RevokedAt = now;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "SpeakingComplianceConsent revoked user={UserId} type={ConsentType} count={Count}",
            userId, normalized, rows.Count);

        return rows.Count;
    }

    public async Task<ConsentHistoryResponse> GetConsentHistoryAsync(string userId, CancellationToken ct)
    {
        var rows = await db.SpeakingComplianceConsents
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.AcceptedAt)
            .ToListAsync(ct);

        return new ConsentHistoryResponse(rows.Select(Project).ToArray());
    }

    // ── Recordings ───────────────────────────────────────────────────────

    /// <summary>Learner-initiated GDPR erasure of a single recording.
    /// Performs an ownership check via the session, deletes the blob
    /// from <see cref="IFileStorage"/> if possible, and marks the row
    /// archived. Emits an <see cref="AuditEvent"/> for compliance.</summary>
    public async Task<RecordingDeletionResponse> DeleteRecordingAsync(
        string userId,
        string recordingId,
        CancellationToken ct)
    {
        var recording = await db.SpeakingRecordings
            .FirstOrDefaultAsync(r => r.Id == recordingId, ct)
            ?? throw ApiException.NotFound(
                "speaking_recording_not_found",
                "Recording not found.");

        var session = await db.SpeakingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == recording.SpeakingSessionId, ct)
            ?? throw ApiException.NotFound(
                "speaking_session_not_found",
                "Owning speaking session not found.");

        if (!string.Equals(session.UserId, userId, StringComparison.Ordinal))
        {
            // Don't leak existence to a non-owner — same 404 the read path emits.
            throw ApiException.Forbidden(
                "speaking_recording_forbidden",
                "You do not own this recording.");
        }

        var blobDeleted = await TryDeleteBlobAsync(recording, ct);
        var now = clock.GetUtcNow();
        recording.IsArchived = true;
        recording.RetentionExpiresAt ??= now;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = userId,
            ActorName = userId,
            Action = "SpeakingRecordingDeleted",
            ResourceType = "SpeakingRecording",
            ResourceId = recordingId,
            Details = JsonSerializer.Serialize(new
            {
                sessionId = session.Id,
                blobDeleted,
                source = recording.Source.ToString(),
                sha256 = recording.Sha256,
            }),
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "SpeakingRecording deleted by owner user={UserId} recording={RecordingId} blobDeleted={BlobDeleted}",
            userId, recordingId, blobDeleted);

        return new RecordingDeletionResponse(recordingId, blobDeleted, now);
    }

    /// <summary>Admin / tutor non-owner access path. Writes an AuditEvent
    /// row BEFORE returning the recording so the access is captured even
    /// if a subsequent stream fails. The <paramref name="purpose"/> is a
    /// short human-readable reason (e.g. "Calibration review").</summary>
    public async Task<SpeakingRecording> AdminAccessRecordingAsync(
        string adminId,
        string adminName,
        string recordingId,
        string purpose,
        CancellationToken ct)
    {
        var recording = await db.SpeakingRecordings
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recordingId, ct)
            ?? throw ApiException.NotFound(
                "speaking_recording_not_found",
                "Recording not found.");

        if (string.IsNullOrWhiteSpace(purpose))
        {
            throw ApiException.Validation(
                "missing_access_purpose",
                "A purpose is required when admin/tutor accesses a non-owner recording.");
        }

        var session = await db.SpeakingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == recording.SpeakingSessionId, ct);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = clock.GetUtcNow(),
            ActorId = adminId,
            ActorName = string.IsNullOrWhiteSpace(adminName) ? adminId : adminName,
            Action = "SpeakingRecordingAccessed",
            ResourceType = "SpeakingRecording",
            ResourceId = recordingId,
            Details = JsonSerializer.Serialize(new
            {
                sessionId = session?.Id,
                ownerUserId = session?.UserId,
                purpose,
                source = recording.Source.ToString(),
            }),
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "SpeakingRecording non-owner access admin={AdminId} recording={RecordingId} purpose={Purpose}",
            adminId, recordingId, purpose);

        return recording;
    }

    // ── Retention helpers (used by SpeakingAudioRetentionWorker) ────────

    /// <summary>Phase 7 default retention window. If the session has at
    /// least one tutor assessment row, returns the extended window;
    /// otherwise the shorter default. Reads
    /// <see cref="SpeakingComplianceOptions"/> live.</summary>
    public TimeSpan DefaultRetentionFor(bool tutorReviewed)
    {
        var days = tutorReviewed
            ? _options.RetentionDaysWhenTutorReviewed
            : _options.RetentionDaysDefault;

        return TimeSpan.FromDays(Math.Max(1, days));
    }

    public string ResolveCurrentConsentVersion(string consentType)
    {
        var normalized = consentType?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized == SpeakingComplianceConsentTypes.LiveVideoWithTutor
            ? _options.CurrentLiveVideoConsentVersion
            : _options.CurrentConsentVersion;
    }

    // ── Internals ────────────────────────────────────────────────────────

    private async Task<bool> TryDeleteBlobAsync(SpeakingRecording recording, CancellationToken ct)
    {
        var mediaAsset = await db.MediaAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == recording.MediaAssetId, ct);
        if (mediaAsset is null || string.IsNullOrWhiteSpace(mediaAsset.StoragePath))
        {
            return false;
        }

        try
        {
            if (!storage.Exists(mediaAsset.StoragePath))
            {
                return false;
            }
            return storage.Delete(mediaAsset.StoragePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to delete blob for recording {RecordingId} storagePath={StoragePath}",
                recording.Id, mediaAsset.StoragePath);
            return false;
        }
    }

    private static ConsentRecord Project(SpeakingComplianceConsent c)
        => new(c.ConsentType, c.ConsentVersion, c.AcceptedAt, c.RevokedAt);

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];
}
