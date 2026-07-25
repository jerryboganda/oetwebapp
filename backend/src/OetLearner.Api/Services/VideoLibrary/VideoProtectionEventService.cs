using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
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
    ILogger<VideoProtectionEventService> logger)
{
    private const int BatchMax = 20;
    private const int PerSessionCap = 500;

    private static readonly IReadOnlySet<string> RevocableKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        VideoProtectionKinds.CaptureDetected,
        VideoProtectionKinds.ScreenshotDetected,
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
