using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.VideoLibrary;

public sealed record VideoProtectionEventInput(
    string? VideoId,
    string? SessionId,
    string Kind,
    DateTimeOffset? OccurredAt,
    Dictionary<string, object?>? Metadata);

/// <summary>
/// Ingests capture-protection / tamper telemetry from the video player
/// (Course Platform Security Requirements §2). Modeled on
/// <c>MockService.RecordProctoringEventsAsync</c>: batch-capped, kind
/// whitelist, per-session row cap. High-severity kinds also write an
/// <see cref="AuditEvent"/> (mirrors <c>VideoAttestationService.RecordFailureAsync</c>)
/// and, when <c>VideoProtectionRevokeOnCaptureDetected</c> is enabled and the
/// event carries a valid session id, immediately revoke that playback
/// session.
/// </summary>
public sealed class VideoProtectionEventService(
    LearnerDbContext db,
    IRuntimeSettingsProvider settingsProvider,
    IVideoPlaybackSessionService playbackSessions,
    ISecurityEventLogger securityEventLogger,
    ILogger<VideoProtectionEventService> logger)
{
    private const int BatchMax = 20;
    private const int PerSessionCap = 500;

    private static readonly IReadOnlySet<string> RevocableKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        VideoProtectionKinds.CaptureDetected,
        VideoProtectionKinds.ScreenshotDetected,
    };

    /// <summary>Maps <see cref="VideoProtectionKinds"/> onto the matching
    /// <see cref="SecurityEventKinds"/> "video.*" constant (third write path,
    /// see class doc) — a 1:1 switch rather than string concatenation so a
    /// typo/rename in either whitelist is a compile error, not a silent
    /// feed gap. <paramref name="kind"/> is always one of
    /// <see cref="VideoProtectionKinds"/>.All by the time this is called
    /// (validated in the loop below), so the fallback arm is unreachable.</summary>
    private static string ToSecurityEventKind(string kind) => kind switch
    {
        VideoProtectionKinds.ProtectionEngaged => SecurityEventKinds.VideoProtectionEngaged,
        VideoProtectionKinds.ProtectionUnavailable => SecurityEventKinds.VideoProtectionUnavailable,
        VideoProtectionKinds.CaptureDetected => SecurityEventKinds.VideoCaptureDetected,
        VideoProtectionKinds.ScreenshotDetected => SecurityEventKinds.VideoScreenshotDetected,
        VideoProtectionKinds.WatermarkTampered => SecurityEventKinds.VideoWatermarkTampered,
        VideoProtectionKinds.DevtoolsSuspected => SecurityEventKinds.VideoDevtoolsSuspected,
        VideoProtectionKinds.VisibilityHidden => SecurityEventKinds.VideoVisibilityHidden,
        VideoProtectionKinds.FocusLost => SecurityEventKinds.VideoFocusLost,
        VideoProtectionKinds.IntegritySignal => SecurityEventKinds.VideoIntegritySignal,
        _ => $"video.{kind}",
    };

    public async Task<object> RecordAsync(
        string userId,
        string? platform,
        string? ipAddress,
        IReadOnlyList<VideoProtectionEventInput>? events,
        CancellationToken ct)
    {
        if (events is null || events.Count == 0)
        {
            throw ApiException.Validation("invalid_request", "events array is required.");
        }
        if (events.Count > BatchMax)
        {
            throw ApiException.Validation("batch_too_large", $"Up to {BatchMax} events per request.");
        }

        var settings = await settingsProvider.GetAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var accepted = 0;
        var dropped = 0;
        string? sessionToRevoke = null;

        // userId here is LearnerUser.Id, not the auth-account id SecurityEvent
        // keys on elsewhere (auth.* / session.* / playback.* events) — resolve
        // it once up front (mirrors VideoPlaybackSessionService.IssueAsync) so
        // the admin security feed can join this telemetry to the same account
        // as sign-ins, session revocations, and playback activity.
        var authAccountId = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.AuthAccountId)
            .FirstOrDefaultAsync(ct);

        foreach (var ev in events)
        {
            if (string.IsNullOrWhiteSpace(ev.Kind) || !VideoProtectionKinds.All.Contains(ev.Kind))
            {
                dropped++;
                continue;
            }

            // Per-session cap mirrors MockProctoringEvent's per-attempt cap —
            // only meaningful when a sessionId is supplied; events without one
            // (e.g. protection_engaged fired before a session exists) are
            // always accepted subject only to the batch limit.
            if (!string.IsNullOrWhiteSpace(ev.SessionId))
            {
                var existing = await db.VideoProtectionEvents.CountAsync(x => x.SessionId == ev.SessionId, ct);
                if (existing >= PerSessionCap)
                {
                    dropped++;
                    continue;
                }
            }

            var occurredAt = ev.OccurredAt ?? now;
            if (occurredAt > now.AddMinutes(5)) occurredAt = now; // clock-skew guard
            var severity = VideoProtectionKinds.DefaultSeverity(ev.Kind);
            var metadataJson = ev.Metadata is { Count: > 0 } ? JsonSupport.Serialize(ev.Metadata) : "{}";

            db.VideoProtectionEvents.Add(new VideoProtectionEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                VideoId = ev.VideoId,
                SessionId = ev.SessionId,
                Kind = ev.Kind,
                Severity = severity,
                Platform = platform,
                IpAddress = ipAddress,
                OccurredAt = occurredAt,
                MetadataJson = metadataJson,
            });
            accepted++;

            if (VideoProtectionKinds.AuditWorthy.Contains(ev.Kind))
            {
                db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ActorId = userId,
                    ActorName = userId,
                    Action = $"video.protection.{ev.Kind}",
                    ResourceType = "library_video",
                    ResourceId = ev.VideoId,
                    Details = JsonSupport.Serialize(new { ev.Kind, platform, ip = ipAddress, sessionId = ev.SessionId }),
                    OccurredAt = now,
                });
            }

            // Third write path (additive, alongside VideoProtectionEvents above
            // and the AuditEvents mirror): every accepted kind, not just the
            // AuditWorthy subset, so admins can browse/filter this telemetry in
            // the general security-events feed the same way they already do
            // for auth/session/device/risk events. Uses its own DB scope (see
            // SecurityEventLogger) so a logging failure here can never break
            // event ingestion. Does not affect AdminAlertService's existing
            // capture_protection_triggered alert, which reads VideoProtectionEvents directly.
            await securityEventLogger.TryLogAsync(
                authAccountId,
                ToSecurityEventKind(ev.Kind),
                deviceId: null,
                details: new { kind = ev.Kind, videoId = ev.VideoId, sessionId = ev.SessionId, platform },
                severity: severity,
                cancellationToken: ct);

            if (settings.VideoProtection.RevokeOnCaptureDetected
                && RevocableKinds.Contains(ev.Kind)
                && !string.IsNullOrWhiteSpace(ev.SessionId))
            {
                // Multiple events in one batch could each name a session;
                // revoking is idempotent, so just remember the last one and
                // act once after the loop rather than awaiting inside it
                // (this loop already awaits for the per-session cap check —
                // the revoke call is deferred purely to keep both intents visible).
                sessionToRevoke = ev.SessionId;
            }
        }

        if (accepted > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        if (sessionToRevoke is not null)
        {
            var revoked = await playbackSessions.RevokeSessionAsync(userId, sessionToRevoke, ct);
            if (revoked)
            {
                logger.LogWarning(
                    "Revoked video playback session {SessionId} for user {UserId}: capture detected.",
                    sessionToRevoke, userId);
            }
        }

        return new { ok = true, accepted, dropped };
    }
}
