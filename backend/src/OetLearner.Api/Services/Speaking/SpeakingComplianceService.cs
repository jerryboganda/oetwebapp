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

    // ── My recordings (Phase 10 P10.1) ────────────────────────────────

    /// <summary>List the caller's own speaking recordings, joined with
    /// the owning session + role-play card so the My-Recordings UI can
    /// show profession, mode, scenario title, and retention countdown
    /// without an N+1.</summary>
    public async Task<MyRecordingsResponse> GetMyRecordingsAsync(
        string userId,
        CancellationToken ct)
    {
        var rows = await (
            from r in db.SpeakingRecordings.AsNoTracking()
            join s in db.SpeakingSessions.AsNoTracking() on r.SpeakingSessionId equals s.Id
            join c in db.RolePlayCards.AsNoTracking() on s.RolePlayCardId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            where s.UserId == userId
            orderby r.CreatedAt descending
            select new MyRecordingRow(
                r.Id,
                s.Id,
                r.CreatedAt,
                s.Mode.ToString(),
                c != null ? c.ProfessionId : "unknown",
                c != null ? c.ScenarioTitle : "(scenario unavailable)",
                r.DurationSeconds,
                r.MimeType,
                r.IsArchived,
                r.RetentionExpiresAt))
            .Take(500)
            .ToListAsync(ct);

        return new MyRecordingsResponse(rows.ToArray());
    }

    // ── Admin recording-access audit (Phase 10 P10.2) ─────────────────

    /// <summary>Project the AuditEvent rows that record admin / tutor
    /// access to learner recordings (and learner-initiated deletions)
    /// to a learner-friendly shape. Filters are AND-combined; null /
    /// blank filters are ignored.</summary>
    public async Task<SpeakingAccessAuditResponse> GetAccessAuditAsync(
        SpeakingAccessAuditFilter filter,
        CancellationToken ct)
    {
        var limit = filter.Limit <= 0 ? 100 : Math.Min(filter.Limit, 500);

        IQueryable<AuditEvent> q = db.AuditEvents
            .AsNoTracking()
            .Where(a => a.ResourceType == "SpeakingRecording"
                && (a.Action == "SpeakingRecordingAccessed"
                    || a.Action == "SpeakingRecordingDeleted"));

        if (!string.IsNullOrWhiteSpace(filter.RecordingId))
        {
            var rid = filter.RecordingId.Trim();
            q = q.Where(a => a.ResourceId == rid);
        }
        if (!string.IsNullOrWhiteSpace(filter.TutorEmailOrId))
        {
            var t = filter.TutorEmailOrId.Trim();
            q = q.Where(a => a.ActorId == t || a.ActorName.Contains(t));
        }
        if (filter.From.HasValue)
        {
            var from = filter.From.Value;
            q = q.Where(a => a.OccurredAt >= from);
        }
        if (filter.To.HasValue)
        {
            var to = filter.To.Value;
            q = q.Where(a => a.OccurredAt <= to);
        }

        var raw = await q
            .OrderByDescending(a => a.OccurredAt)
            .Take(limit)
            .Select(a => new
            {
                a.Id,
                a.OccurredAt,
                a.Action,
                a.ResourceId,
                a.ActorId,
                a.ActorName,
                a.Details,
            })
            .ToListAsync(ct);

        // Resolve learner ids by joining recording → session.
        var recordingIds = raw
            .Select(x => x.ResourceId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToArray();

        var ownerLookup = await (
            from r in db.SpeakingRecordings.AsNoTracking()
            join s in db.SpeakingSessions.AsNoTracking() on r.SpeakingSessionId equals s.Id
            where recordingIds.Contains(r.Id)
            select new { r.Id, s.UserId, SessionId = s.Id })
            .ToDictionaryAsync(x => x.Id, x => new { x.UserId, x.SessionId }, ct);

        var rows = raw.Select(a =>
        {
            string? learnerUserId = null;
            string? sessionId = null;
            if (!string.IsNullOrEmpty(a.ResourceId)
                && ownerLookup.TryGetValue(a.ResourceId, out var lookup))
            {
                learnerUserId = lookup.UserId;
                sessionId = lookup.SessionId;
            }

            // Learner-id filter applied after lookup so we can match by
            // the resolved owner rather than the raw ActorId column.
            if (!string.IsNullOrWhiteSpace(filter.LearnerEmailOrId)
                && learnerUserId != null
                && !string.Equals(learnerUserId, filter.LearnerEmailOrId.Trim(), StringComparison.Ordinal))
            {
                return null;
            }

            string? purpose = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(a.Details))
                {
                    using var doc = JsonDocument.Parse(a.Details);
                    if (doc.RootElement.TryGetProperty("purpose", out var p)
                        && p.ValueKind == JsonValueKind.String)
                    {
                        purpose = p.GetString();
                    }
                }
            }
            catch (JsonException) { /* best-effort */ }

            return new SpeakingAccessAuditRow(
                a.Id,
                a.OccurredAt,
                a.Action,
                a.ResourceId,
                sessionId,
                learnerUserId,
                a.ActorId,
                a.ActorName,
                ActorRole: null,
                Purpose: purpose,
                Reason: null,
                DetailsJson: a.Details);
        })
        .Where(x => x != null)
        .Select(x => x!)
        .ToArray();

        return new SpeakingAccessAuditResponse(rows);
    }

    // ── Erasure preflight (Phase 10 P10.3) ────────────────────────────

    /// <summary>Build the inventory of consent + recording + assessment
    /// rows the caller currently owns, without deleting anything. The
    /// frontend uses this to show learners exactly what a full GDPR
    /// erasure will remove.</summary>
    public async Task<ErasurePreflightResponse> GetErasurePreflightAsync(
        string userId,
        CancellationToken ct)
    {
        var consents = await db.SpeakingComplianceConsents
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.AcceptedAt)
            .Select(c => new ConsentRecord(c.ConsentType, c.ConsentVersion, c.AcceptedAt, c.RevokedAt))
            .ToListAsync(ct);

        var recordings = await (
            from r in db.SpeakingRecordings.AsNoTracking()
            join s in db.SpeakingSessions.AsNoTracking() on r.SpeakingSessionId equals s.Id
            where s.UserId == userId
            orderby r.CreatedAt descending
            select new ErasurePreflightRecording(
                r.Id, s.Id, r.CreatedAt, r.DurationSeconds, r.IsArchived))
            .Take(500)
            .ToListAsync(ct);

        var assessments = await (
            from a in db.SpeakingAiAssessments.AsNoTracking()
            join s in db.SpeakingSessions.AsNoTracking() on a.SpeakingSessionId equals s.Id
            where s.UserId == userId
            orderby a.GeneratedAt descending
            select new ErasurePreflightAssessment(
                a.Id, s.Id, "ai", a.GeneratedAt))
            .Take(500)
            .ToListAsync(ct);

        return new ErasurePreflightResponse(
            consents.ToArray(),
            recordings.ToArray(),
            assessments.ToArray());
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
            if (!await storage.ExistsAsync(mediaAsset.StoragePath, ct))
            {
                return false;
            }
            return await storage.DeleteAsync(mediaAsset.StoragePath, ct);
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
