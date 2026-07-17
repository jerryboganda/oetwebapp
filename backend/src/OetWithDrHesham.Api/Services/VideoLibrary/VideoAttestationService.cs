using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Services.VideoLibrary;

// ═════════════════════════════════════════════════════════════════════════════
// Video playback attestation — MISSION-CRITICAL gate.
//
// Playback sessions are issued ONLY to attested native clients (Tauri desktop /
// Capacitor mobile), never to browsers. The handshake:
//
//   1. POST /v1/video-library/attestation/challenge → single-use nonce
//      (base64url of 32 random bytes, 90s TTL, bound to the caller's user id).
//   2. The native shell signs "{nonce}|{videoId}|{userId}|{platform}|{keyId}"
//      (UTF-8) with its embedded per-platform HMAC-SHA256 secret and sends the
//      LOWERCASE HEX signature to POST /videos/{videoId}/playback-session.
//   3. The server consumes the nonce atomically (single use), looks up the
//      "{platform}:{keyId}" secret from RuntimeSettings, recomputes the HMAC
//      with ITS OWN authenticated user id (a spoofed id simply fails the
//      compare) and verifies via CryptographicOperations.FixedTimeEquals.
//
// Every failure is audited (video.playback.attestation_failed) and logged.
// ═════════════════════════════════════════════════════════════════════════════

public interface IVideoAttestationService
{
    Task<VideoAttestationChallengeResult> IssueChallengeAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Verify steps (a) nonce consume, (b) key lookup, (c) HMAC compare.
    /// Throws <see cref="ApiException"/> (403) on any failure — after writing
    /// the audit trail. Returns normally only for a fully attested caller.
    /// </summary>
    Task VerifyAsync(
        string userId,
        string videoId,
        string? nonce,
        string? platform,
        string? keyId,
        string? signature,
        string? ipAddress,
        CancellationToken ct);
}

public sealed record VideoAttestationChallengeResult(string Nonce, DateTimeOffset ExpiresAt);

public sealed class VideoAttestationService(
    LearnerDbContext db,
    IRuntimeSettingsProvider settingsProvider,
    ILogger<VideoAttestationService> logger) : IVideoAttestationService
{
    private static readonly TimeSpan ChallengeTtl = TimeSpan.FromSeconds(90);

    public async Task<VideoAttestationChallengeResult> IssueChallengeAsync(string userId, CancellationToken ct)
    {
        var nonce = GenerateNonce();
        var now = DateTimeOffset.UtcNow;
        db.VideoAttestationChallenges.Add(new VideoAttestationChallenge
        {
            Id = nonce,
            UserId = userId,
            IssuedAt = now,
            ExpiresAt = now.Add(ChallengeTtl),
        });
        await db.SaveChangesAsync(ct);
        return new VideoAttestationChallengeResult(nonce, now.Add(ChallengeTtl));
    }

    public async Task VerifyAsync(
        string userId,
        string videoId,
        string? nonce,
        string? platform,
        string? keyId,
        string? signature,
        string? ipAddress,
        CancellationToken ct)
    {
        platform = platform?.Trim() ?? string.Empty;
        keyId = keyId?.Trim() ?? string.Empty;

        // (a) Atomic single-use nonce consume. 0 rows updated covers: unknown
        // nonce, another user's nonce, already-consumed nonce, expired nonce.
        if (string.IsNullOrWhiteSpace(nonce)
            || !await TryConsumeNonceAsync(nonce, userId, platform, ct))
        {
            await RecordFailureAsync(userId, videoId, "nonce_invalid", platform, ipAddress, ct);
            throw ApiException.Forbidden("attestation_invalid", "Attestation challenge is invalid or expired.");
        }

        // (b) Key lookup "{platform}:{keyId}".
        var settings = (await settingsProvider.GetAsync(ct)).VideoAttestation;
        if (!settings.IsConfigured
            || !settings.Keys.TryGetValue($"{platform}:{keyId}", out var secret)
            || string.IsNullOrWhiteSpace(secret))
        {
            await RecordFailureAsync(userId, videoId, "key_unavailable", platform, ipAddress, ct);
            throw ApiException.Forbidden("attestation_unavailable",
                "Playback attestation is not available for this client.");
        }

        // (c) Recompute the HMAC over the canonical message with OUR user id.
        var expected = ComputeSignature(secret, nonce, videoId, userId, platform, keyId);
        if (!FixedTimeHexEquals(expected, signature))
        {
            await RecordFailureAsync(userId, videoId, "signature_mismatch", platform, ipAddress, ct);
            throw ApiException.Forbidden("attestation_invalid", "Attestation signature is invalid.");
        }
    }

    // ── Pure helpers (unit-test pinned) ────────────────────────────────────

    /// <summary>
    /// message = "{nonce}|{videoId}|{userId}|{platform}|{keyId}" (UTF-8),
    /// HMAC-SHA256 with the per-platform secret, LOWERCASE HEX output.
    /// </summary>
    public static string ComputeSignature(
        string secret, string nonce, string videoId, string userId, string platform, string keyId)
    {
        var message = $"{nonce}|{videoId}|{userId}|{platform}|{keyId}";
        var mac = HMACSHA256.HashData(DecodeSecret(secret), Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    /// <summary>
    /// The HMAC key is the configured secret string's raw UTF-8 bytes — never
    /// hex-decoded. This MUST match the native shells, all of which key HMAC
    /// with the literal secret string bytes: Tauri `secret.as_bytes()`
    /// (src-tauri/src/attestation.rs), Android `secret.getBytes(UTF_8)`
    /// (PlaybackAttestationPlugin.java), iOS `Data(secret.utf8)`
    /// (PlaybackAttestationPlugin.swift). A 64-hex secret is therefore a
    /// 64-byte ASCII key on BOTH sides. Do not "optimize" this into hex
    /// decoding — it would silently break every production attestation while
    /// the non-hex dev fallback keeps working.
    /// </summary>
    public static byte[] DecodeSecret(string secret)
    {
        return Encoding.UTF8.GetBytes(secret.Trim());
    }

    public static string GenerateNonce()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    // ── Internals ──────────────────────────────────────────────────────────

    private async Task<bool> TryConsumeNonceAsync(string nonce, string userId, string platform, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational())
        {
            // Atomic: UPDATE ... SET ConsumedAt WHERE Id=@nonce AND UserId=@user
            //         AND ConsumedAt IS NULL AND ExpiresAt > now
            var updated = await db.VideoAttestationChallenges
                .Where(c => c.Id == nonce
                    && c.UserId == userId
                    && c.ConsumedAt == null
                    && c.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.ConsumedAt, now)
                    .SetProperty(c => c.Platform, platform), ct);
            return updated == 1;
        }

        // EF InMemory test provider: no ExecuteUpdate support — same predicate,
        // load-then-save (single-process tests; the race window is acceptable).
        var row = await db.VideoAttestationChallenges
            .FirstOrDefaultAsync(c => c.Id == nonce
                && c.UserId == userId
                && c.ConsumedAt == null
                && c.ExpiresAt > now, ct);
        if (row is null) return false;
        row.ConsumedAt = now;
        row.Platform = platform;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static bool FixedTimeHexEquals(string expectedLowerHex, string? providedHex)
    {
        if (string.IsNullOrWhiteSpace(providedHex)) return false;
        byte[] expectedBytes;
        byte[] providedBytes;
        try
        {
            expectedBytes = Convert.FromHexString(expectedLowerHex);
            providedBytes = Convert.FromHexString(providedHex.Trim());
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private async Task RecordFailureAsync(
        string userId, string videoId, string reason, string platform, string? ipAddress, CancellationToken ct)
    {
        logger.LogWarning(
            "Video playback attestation FAILED for user {UserId} video {VideoId}: {Reason} (platform {Platform}, ip {Ip})",
            userId, videoId, reason, platform, ipAddress ?? "unknown");

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = userId,
            ActorName = userId,
            Action = "video.playback.attestation_failed",
            ResourceType = "library_video",
            ResourceId = videoId,
            Details = JsonSupport.Serialize(new { reason, platform, ip = ipAddress }),
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}
