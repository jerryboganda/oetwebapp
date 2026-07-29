using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.VideoLibrary;

/// <summary>
/// Issues and renews short-lived protected playback sessions for authorised
/// native and web clients. Proof + entitlement checks happen BEFORE this service is
/// invoked on the ISSUE path (see VideoLibraryEndpoints); this service owns
/// concurrency limits, session persistence, CDN URL signing, and the watermark
/// text. RENEW re-checks visibility + entitlement itself: it mints a fresh
/// signed CDN URL, and the endpoint has no other gate in front of it.
/// </summary>
public interface IVideoPlaybackSessionService
{
    /// <summary>Issue (or re-use) a playback session for an authorised caller.</summary>
    Task<PlaybackSessionResult> IssueAsync(
        string userId,
        LibraryVideo video,
        string platform,
        string keyId,
        string? deviceId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct);

    /// <summary>Renew the signed URL of a still-valid session (403 session_expired otherwise).</summary>
    Task<PlaybackSessionResult> RenewAsync(
        string userId, string sessionId, string? deviceId, CancellationToken ct);

    /// <summary>Immediately revokes one session owned by <paramref name="userId"/>
    /// (e.g. a capture-detected protection event, per RuntimeSettings
    /// <c>VideoProtectionRevokeOnCaptureDetected</c>). No-op — and returns
    /// false — if the session doesn't exist, isn't owned by this user, or is
    /// already revoked. The player's next renew call 403s as usual.</summary>
    Task<bool> RevokeSessionAsync(string userId, string sessionId, CancellationToken ct);

    /// <summary>Immediately revokes EVERY active playback session owned by
    /// <paramref name="userId"/> (Security spec §3.1 "previous device loses
    /// playback access even mid-video" — called when a sign-in elsewhere
    /// revokes this account's other auth sessions, and by admin
    /// block-playback). Returns the number of sessions revoked.</summary>
    Task<int> RevokeAllForUserAsync(string userId, CancellationToken ct);
}

public sealed record PlaybackSessionCaption(string LanguageCode, string Label);

/// <summary>
/// Structured watermark payload (Course Platform Security Requirements §2.3):
/// full name, masked email, a stable user/session reference, and the issuing
/// platform. <see cref="IssuedAt"/> is the session-reference timestamp; the
/// player renders a LIVE clock alongside it (a live clock is strictly
/// stronger than a frozen issue time for the "current date and time"
/// requirement).
/// </summary>
public sealed record PlaybackWatermark(
    string FullName,
    string MaskedEmail,
    string UserRef,
    string SessionRef,
    string Platform,
    DateTimeOffset IssuedAt);

public sealed record PlaybackSessionResult(
    string SessionId,
    string PlaybackUrl,
    string DeliveryMode,
    DateTimeOffset ExpiresAt,
    DateTimeOffset SessionExpiresAt,
    string WatermarkText,
    PlaybackWatermark Watermark,
    IReadOnlyList<PlaybackSessionCaption> Captions);

public sealed class VideoPlaybackSessionService(
    LearnerDbContext db,
    IBunnyStreamClient bunny,
    IRuntimeSettingsProvider settingsProvider,
    VideoLibraryLearnerService learnerService,
    IVideoEntitlementService entitlements,
    ISecurityEventLogger securityEventLogger,
    ILogger<VideoPlaybackSessionService> logger) : IVideoPlaybackSessionService
{
    private const int MaxConcurrentDistinctVideos = 3;
    private static readonly TimeSpan MaxSessionLifetime = TimeSpan.FromHours(8);

    public async Task<PlaybackSessionResult> IssueAsync(
        string userId,
        LibraryVideo video,
        string platform,
        string keyId,
        string? deviceId,
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
                && s.DeviceId == deviceId
                && s.RevokedAt == null
                && s.ExpiresAt > now)
            .OrderByDescending(s => s.IssuedAt)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return await BuildResultAsync(existing.Id, video, existing.ExpiresAt, userId, existing.Platform, ct);
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

        // The database session is deliberately longer than the URL token. A
        // learner can finish a normal lesson without re-attesting every five
        // minutes, while the bearer URL itself is short-lived and renewed only
        // after this service re-checks auth, publication and entitlement.
        var sessionLifetimeSeconds = Math.Min(
            Math.Max(2L * Math.Max(0, video.DurationSeconds), 900L),
            (long)MaxSessionLifetime.TotalSeconds);
        var expiresAt = now.AddSeconds(sessionLifetimeSeconds);

        var session = new VideoPlaybackSession
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            VideoId = video.Id,
            Platform = platform,
            AttestationKeyId = keyId,
            IpAddress = Truncate(ipAddress, 64),
            UserAgent = Truncate(userAgent, 256),
            DeviceId = Truncate(deviceId, 128),
            IssuedAt = now,
            ExpiresAt = expiresAt,
        };

        // (f) Sign FIRST — an unconfigured Bunny throws BunnyNotConfiguredException
        // (mapped to 503 bunny_not_configured) before any row is persisted.
        var result = await BuildResultAsync(session.Id, video, expiresAt, userId, platform, ct);

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

        // userId here is LearnerUser.Id, not the auth-account id SecurityEvent
        // keys on elsewhere (auth.* / session.* events) — resolve it so the
        // admin security feed can join playback activity to the same account
        // as sign-ins/session revocations.
        var authAccountId = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.AuthAccountId)
            .FirstOrDefaultAsync(ct);
        await securityEventLogger.TryLogAsync(
            authAccountId,
            SecurityEventKinds.PlaybackSessionStarted,
            deviceId: deviceId,
            details: new { videoId = video.Id, platform },
            cancellationToken: ct);

        return result;
    }

    public async Task<PlaybackSessionResult> RenewAsync(
        string userId, string sessionId, string? deviceId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var session = await db.VideoPlaybackSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

        if (session is null
            || session.RevokedAt is not null
            || session.ExpiresAt <= now)
        {
            throw ApiException.Forbidden("session_expired",
                "This playback session has expired. Start the video again to get a new session.");
        }

        if (!string.IsNullOrWhiteSpace(session.DeviceId)
            && !string.Equals(session.DeviceId, deviceId, StringComparison.Ordinal))
        {
            session.RevokedAt = now;
            await db.SaveChangesAsync(ct);
            logger.LogWarning(
                "Revoked video playback session {SessionId}: device mismatch.",
                sessionId);
            throw ApiException.Forbidden(
                "session_device_mismatch",
                "This playback session belongs to a different device.");
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
        return await BuildResultAsync(session.Id, video, session.ExpiresAt, userId, session.Platform, ct);
    }

    public async Task<bool> RevokeSessionAsync(string userId, string sessionId, CancellationToken ct)
    {
        var tracked = await db.VideoPlaybackSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (tracked is null || tracked.RevokedAt is not null) return false;
        tracked.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> RevokeAllForUserAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.VideoPlaybackSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, now), ct);
    }

    private async Task RevokeAsync(string sessionId, DateTimeOffset now, CancellationToken ct)
    {
        var tracked = await db.VideoPlaybackSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (tracked is null || tracked.RevokedAt is not null) return;
        tracked.RevokedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private async Task<PlaybackSessionResult> BuildResultAsync(
        string sessionId, LibraryVideo video, DateTimeOffset expiresAt, string userId, string platform, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(video.BunnyVideoId))
        {
            // Published videos always carry a Bunny id (publish gate), but be
            // defensive: without one there is nothing to sign.
            throw new BunnyNotConfiguredException();
        }

        var settings = (await settingsProvider.GetAsync(ct)).BunnyStream;
        var playbackUrlExpiresAt = DateTimeOffset.UtcNow.AddSeconds(
            Math.Clamp(settings.PlaybackTokenTtlSeconds, 300, 900));
        if (playbackUrlExpiresAt > expiresAt)
        {
            playbackUrlExpiresAt = expiresAt;
        }

        var playbackUrl = await bunny.SignPlaybackUrlAsync(
            video.BunnyVideoId, playbackUrlExpiresAt.ToUnixTimeSeconds(), ct);

        var learner = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.DisplayName, u.Email })
            .FirstOrDefaultAsync(ct);

        var captions = await db.VideoCaptionTracks.AsNoTracking()
            .Where(c => c.VideoId == video.Id)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.LanguageCode)
            .Select(c => new PlaybackSessionCaption(c.LanguageCode, c.Label))
            .ToListAsync(ct);

        var sessionRef = sessionId[..Math.Min(8, sessionId.Length)];
        var userRef = userId[..Math.Min(8, userId.Length)];
        var displayName = learner?.DisplayName;
        var learnerEmail = learner?.Email;
        var fullName = !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : !string.IsNullOrWhiteSpace(learnerEmail)
                ? learnerEmail.Split('@')[0]
                : userRef;
        var maskedEmail = MaskEmail(learnerEmail);
        var issuedAt = DateTimeOffset.UtcNow;

        // Legacy single-string watermark kept for one deploy cycle so a
        // frontend that hasn't picked up the structured `watermark` field yet
        // (deploy skew) still renders something identifying.
        var watermarkText = $"{learner?.Email ?? userId} · {sessionRef}";
        var watermark = new PlaybackWatermark(fullName, maskedEmail, userRef, sessionRef, platform, issuedAt);

        return new PlaybackSessionResult(
            sessionId,
            playbackUrl,
            "secure_embed",
            playbackUrlExpiresAt,
            expiresAt,
            watermarkText,
            watermark,
            captions);
    }

    /// <summary>Masks an email for on-screen display (Course Platform Security
    /// Requirements §2.3: "masked email or user ID"): keeps the first
    /// character of the local part and of the domain name, masks the rest,
    /// and preserves the TLD (e.g. "john.doe@gmail.com" → "j•••@g•••.com").</summary>
    internal static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "unknown";
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1) return "unknown";

        var local = email[..at];
        var domain = email[(at + 1)..];
        var maskedLocal = MaskSegment(local);

        var dot = domain.IndexOf('.');
        var maskedDomain = dot > 0
            ? MaskSegment(domain[..dot]) + domain[dot..]
            : MaskSegment(domain);

        return $"{maskedLocal}@{maskedDomain}";

        static string MaskSegment(string segment)
            => segment.Length <= 1 ? segment + "•••" : segment[0] + "•••";
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
