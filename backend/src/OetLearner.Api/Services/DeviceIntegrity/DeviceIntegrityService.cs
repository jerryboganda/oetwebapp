using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;

namespace OetLearner.Api.Services.DeviceIntegrity;

/// <summary>A server-issued, single-use, user-bound nonce the client feeds into
/// its platform attestation SDK.</summary>
public sealed record DeviceIntegrityChallengeResult(string Nonce, DateTimeOffset ExpiresAt);

/// <summary>
/// Composes device-integrity verification: issues/consumes the replay nonce,
/// selects the platform verifier, records the outcome as telemetry, and returns
/// the verdict.
///
/// <para>
/// CONTRACT: <see cref="VerifyAsync"/> NEVER throws for a provider failure — it
/// returns an "unknown" verdict. The verdict is a RISK SIGNAL and is never used
/// to grant or deny access.
/// </para>
/// </summary>
public interface IDeviceIntegrityService
{
    Task<DeviceIntegrityChallengeResult> IssueChallengeAsync(string userId, string platform, CancellationToken ct);

    Task<DeviceIntegrityVerdict> VerifyAsync(
        string userId,
        string platform,
        string? integrityToken,
        string? keyId,
        string? nonce,
        CancellationToken ct);
}

public sealed class DeviceIntegrityService(
    LearnerDbContext db,
    IDeviceIntegrityVerifierSelector selector,
    ISecurityEventLogger securityEvents,
    IOptions<DeviceAttestationOptions> options,
    ILogger<DeviceIntegrityService> logger) : IDeviceIntegrityService
{
    private static readonly TimeSpan ChallengeTtl = TimeSpan.FromSeconds(90);
    private const int PlatformMaxLength = 32;

    private readonly DeviceAttestationOptions _options = options.Value;

    public async Task<DeviceIntegrityChallengeResult> IssueChallengeAsync(
        string userId, string platform, CancellationToken ct)
    {
        var nonce = GenerateNonce();
        var now = DateTimeOffset.UtcNow;

        // REUSE NOTE: this reuses VideoAttestationChallenges (the existing
        // single-use nonce store) because Data/** is not owned here, so no new
        // table can be added this pass. The nonce remains user-bound, single-use,
        // and 90s-TTL; its only job is anti-replay, so sharing the store with the
        // video flow weakens nothing. A dedicated DeviceIntegrityChallenge table
        // (or a Purpose column) would be cleaner and is noted as follow-up work.
        db.VideoAttestationChallenges.Add(new VideoAttestationChallenge
        {
            Id = nonce,
            UserId = userId,
            IssuedAt = now,
            ExpiresAt = now.Add(ChallengeTtl),
            Platform = Truncate(platform, PlatformMaxLength),
        });
        await db.SaveChangesAsync(ct);

        return new DeviceIntegrityChallengeResult(nonce, now.Add(ChallengeTtl));
    }

    public async Task<DeviceIntegrityVerdict> VerifyAsync(
        string userId,
        string platform,
        string? integrityToken,
        string? keyId,
        string? nonce,
        CancellationToken ct)
    {
        platform = platform?.Trim() ?? string.Empty;

        // (1) Atomic single-use nonce consume. A captured integrity token cannot
        //     be replayed: the nonce must be presented back, and it is consumed
        //     here before the provider is ever contacted.
        if (string.IsNullOrWhiteSpace(nonce) || !await TryConsumeNonceAsync(nonce, userId, platform, ct))
        {
            return await RecordAndReturnAsync(userId, platform,
                DeviceIntegrityVerdict.Unknown("nonce_invalid"), ct);
        }

        // (2) Provider selection.
        var verifier = selector.Select(platform);
        DeviceIntegrityVerdict verdict;

        if (verifier is null)
        {
            verdict = DeviceIntegrityVerdict.Unknown("platform_unsupported");
        }
        else if (!verifier.IsConfigured)
        {
            verdict = DeviceIntegrityVerdict.Unknown("provider_not_configured", verifier.Provider);
        }
        else if (string.IsNullOrWhiteSpace(integrityToken))
        {
            verdict = DeviceIntegrityVerdict.Unknown("integrity_token_missing", verifier.Provider);
        }
        else
        {
            try
            {
                verdict = await verifier.VerifyAsync(
                    new DeviceIntegrityRequest(userId, platform, integrityToken, keyId, nonce), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A provider failure is never a crash and never "trusted".
                logger.LogWarning(
                    ex, "Device integrity provider {Provider} threw during verification.", verifier.Provider);
                verdict = DeviceIntegrityVerdict.Unknown("provider_error", verifier.Provider);
            }
        }

        return await RecordAndReturnAsync(userId, platform, verdict, ct);
    }

    /// <summary>
    /// Persists the outcome as telemetry and returns the verdict unchanged. Uses
    /// the closest existing security-event kind (<c>video.integrity_signal</c>) —
    /// a dedicated kind such as <c>device.integrity_verdict</c> would read better
    /// but Domain/** is owned elsewhere, so none could be added this pass.
    /// <see cref="ISecurityEventLogger.TryLogAsync"/> is documented never to throw
    /// into the caller.
    /// </summary>
    private async Task<DeviceIntegrityVerdict> RecordAndReturnAsync(
        string userId, string platform, DeviceIntegrityVerdict verdict, CancellationToken ct)
    {
        var severity = verdict.Trusted
            ? "info"
            : string.Equals(verdict.Verdict, "unknown", StringComparison.Ordinal) ? "warning" : "critical";

        await securityEvents.TryLogAsync(
            authAccountId: userId,
            kind: SecurityEventKinds.VideoIntegritySignal,
            details: new
            {
                source = "device_integrity",
                provider = verdict.Provider,
                verdict = verdict.Verdict,
                trusted = verdict.Trusted,
                reason = verdict.Reason,
                platform,
                // Sanitised provider summary only — never the raw token/assertion.
                summary = verdict.RawSummaryJson,
                // Policy flags are recorded (not enforced here) so the downstream
                // RISK layer — never the auth/authz stack — can decide how to
                // react, and so this safety-valve config is not dead.
                treatAsBlocking = _options.TreatAsBlocking,
                failClosedOnProviderError = _options.FailClosedOnProviderError,
                requireForCriticalFlows = _options.RequireForCriticalFlows,
            },
            severity: severity,
            cancellationToken: ct);

        return verdict;
    }

    private async Task<bool> TryConsumeNonceAsync(
        string nonce, string userId, string platform, CancellationToken ct)
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
                    .SetProperty(c => c.Platform, Truncate(platform, PlatformMaxLength)), ct);
            return updated == 1;
        }

        // EF InMemory test provider: no ExecuteUpdate support — same predicate,
        // load-then-save (single-process tests; the race window is acceptable).
        var row = await db.VideoAttestationChallenges
            .FirstOrDefaultAsync(c => c.Id == nonce
                && c.UserId == userId
                && c.ConsumedAt == null
                && c.ExpiresAt > now, ct);
        if (row is null)
        {
            return false;
        }

        row.ConsumedAt = now;
        row.Platform = Truncate(platform, PlatformMaxLength);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>base64url of 32 random bytes — matches
    /// <c>VideoAttestationService.GenerateNonce</c> so both nonces are
    /// indistinguishable and fit the shared PK (MaxLength 64).</summary>
    private static string GenerateNonce()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
