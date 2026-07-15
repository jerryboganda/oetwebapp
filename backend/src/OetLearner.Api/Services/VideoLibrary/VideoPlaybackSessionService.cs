using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.VideoLibrary;

/// <summary>
/// Issues and renews token-signed playback sessions for attested native
/// clients. Attestation + entitlement checks happen BEFORE this service is
/// invoked on the ISSUE path (see VideoLibraryEndpoints); this service owns
/// concurrency limits, session persistence, CDN URL signing, and the watermark
/// text. RENEW re-checks visibility + entitlement itself: it mints a fresh
/// signed CDN URL, and the endpoint has no other gate in front of it.
/// </summary>
public interface IVideoPlaybackSessionService
{
    /// <summary>Issue (or re-use) a playback session for an attested caller.</summary>
    Task<PlaybackSessionResult> IssueAsync(
        string userId,
        LibraryVideo video,
        string platform,
        string keyId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct);

    /// <summary>Renew the signed URL of a still-valid session (403 session_expired otherwise).</summary>
    Task<PlaybackSessionResult> RenewAsync(string userId, string sessionId, CancellationToken ct);
}

public sealed record PlaybackSessionCaption(string LanguageCode, string Label);

public sealed record PlaybackSessionResult(
    string SessionId,
    string PlaybackUrl,
    DateTimeOffset ExpiresAt,
    string WatermarkText,
    IReadOnlyList<PlaybackSessionCaption> Captions);

public sealed class VideoPlaybackSessionService(
    LearnerDbContext db,
    IBunnyStreamClient bunny,
    IRuntimeSettingsProvider settingsProvider,
    VideoLibraryLearnerService learnerService,
    IVideoEntitlementService entitlements,
    ILogger<VideoPlaybackSessionService> logger) : IVideoPlaybackSessionService
{
    private const int MaxConcurrentDistinctVideos = 3;
    private static readonly TimeSpan RenewMinRemaining = TimeSpan.FromMinutes(5);

    public async Task<PlaybackSessionResult> IssueAsync(
        string userId,
        LibraryVideo video,
        string platform,
        string keyId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // (e1) Existing unexpired session for the SAME video → return it with a
        // freshly signed URL instead of burning another concurrency slot.
        var existing = await db.VideoPlaybackSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId
                && s.VideoId == video.Id
                && s.RevokedAt == null
                && s.ExpiresAt > now)
            .OrderByDescending(s => s.IssuedAt)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return await BuildResultAsync(existing.Id, video, existing.ExpiresAt, userId, ct);
        }

        // (e2) Concurrency cap: at most 3 active distinct-video sessions.
        var activeVideoIds = await db.VideoPlaybackSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .Select(s => s.VideoId)
            .Distinct()
            .CountAsync(ct);
        if (activeVideoIds >= MaxConcurrentDistinctVideos)
        {
            throw ApiException.Conflict("concurrent_session_limit",
                $"You already have {MaxConcurrentDistinctVideos} active playback sessions. Close one before starting another video.");
        }

        // Session TTL = min(max(2 × duration, 1h), configured cap [default 4h]).
        var settings = (await settingsProvider.GetAsync(ct)).BunnyStream;
        var ttlSeconds = Math.Min(
            Math.Max(2L * Math.Max(0, video.DurationSeconds), 3600L),
            settings.PlaybackTokenTtlSeconds);
        var expiresAt = now.AddSeconds(ttlSeconds);

        var session = new VideoPlaybackSession
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            VideoId = video.Id,
            Platform = platform,
            AttestationKeyId = keyId,
            IpAddress = Truncate(ipAddress, 64),
            UserAgent = Truncate(userAgent, 256),
            IssuedAt = now,
            ExpiresAt = expiresAt,
        };

        // (f) Sign FIRST — an unconfigured Bunny throws BunnyNotConfiguredException
        // (mapped to 503 bunny_not_configured) before any row is persisted.
        var result = await BuildResultAsync(session.Id, video, expiresAt, userId, ct);

        db.VideoPlaybackSessions.Add(session);
        var tracked = await db.LibraryVideos.FirstOrDefaultAsync(v => v.Id == video.Id, ct);
        if (tracked is not null)
        {
            tracked.ViewCount += 1;
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Issued video playback session {SessionId} for user {UserId} video {VideoId} on {Platform} (expires {ExpiresAt:O}).",
            session.Id, userId, video.Id, platform, expiresAt);
        return result;
    }

    public async Task<PlaybackSessionResult> RenewAsync(string userId, string sessionId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var session = await db.VideoPlaybackSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

        if (session is null
            || session.RevokedAt is not null
            || session.ExpiresAt <= now.Add(RenewMinRemaining))
        {
            throw ApiException.Forbidden("session_expired",
                "This playback session has expired. Start the video again to get a new session.");
        }

        // A renew mints a NEW signed CDN URL, so it must re-earn the grant that issued the
        // session — a session outliving the entitlement that created it (expired/frozen
        // subscription, module disabled, profession changed, video unpublished) would otherwise
        // keep handing out playable URLs for its whole TTL. A lapsed grant kills the session.
        var video = await learnerService.FindVisibleVideoAsync(userId, session.VideoId, now, ct);
        if (video is null)
        {
            await RevokeAsync(session.Id, now, ct);
            throw ApiException.Forbidden("session_expired", "This playback session is no longer valid.");
        }

        var access = await entitlements.AllowAccessAsync(userId, video, ct);
        if (!access.Allowed)
        {
            await RevokeAsync(session.Id, now, ct);
            logger.LogInformation(
                "Revoked video playback session {SessionId} for user {UserId} video {VideoId} on renew: {Reason}.",
                session.Id, userId, video.Id, access.Reason);

            // Maps the denial reason onto the same 402/403 contract the issue path uses.
            await entitlements.RequireAccessAsync(userId, video, ct);

            // Unreachable while RequireAccessAsync mirrors AllowAccessAsync — but the session is
            // revoked either way, so never fall through to a fresh signed URL.
            throw ApiException.Forbidden("session_expired", "This playback session is no longer valid.");
        }

        // New signed URL; expiry stays capped at the session's ExpiresAt.
        return await BuildResultAsync(session.Id, video, session.ExpiresAt, userId, ct);
    }

    private async Task RevokeAsync(string sessionId, DateTimeOffset now, CancellationToken ct)
    {
        var tracked = await db.VideoPlaybackSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (tracked is null || tracked.RevokedAt is not null) return;
        tracked.RevokedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private async Task<PlaybackSessionResult> BuildResultAsync(
        string sessionId, LibraryVideo video, DateTimeOffset expiresAt, string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(video.BunnyVideoId))
        {
            // Published videos always carry a Bunny id (publish gate), but be
            // defensive: without one there is nothing to sign.
            throw new BunnyNotConfiguredException();
        }

        var playbackUrl = await bunny.SignPlaybackUrlAsync(
            video.BunnyVideoId, expiresAt.ToUnixTimeSeconds(), ct);

        var email = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);

        var captions = await db.VideoCaptionTracks.AsNoTracking()
            .Where(c => c.VideoId == video.Id)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.LanguageCode)
            .Select(c => new PlaybackSessionCaption(c.LanguageCode, c.Label))
            .ToListAsync(ct);

        var watermark = $"{email ?? userId} · {sessionId[..Math.Min(8, sessionId.Length)]}";
        return new PlaybackSessionResult(sessionId, playbackUrl, expiresAt, watermark, captions);
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
