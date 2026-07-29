using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Security;

public enum DeviceResolution
{
    /// <summary>No device id was presented. The auth service rejects this
    /// result whenever trusted-device enforcement is active.</summary>
    NoDeviceId,

    /// <summary>The presented device id matches the account's current
    /// trusted device (or there wasn't one to compare yet).</summary>
    Trusted,

    /// <summary>The account has no trusted device yet — this one is trusted
    /// silently (registration already proved email ownership).</summary>
    Bootstrap,

    /// <summary>A different device is already trusted — an email-OTP
    /// challenge is required before this one can be trusted.</summary>
    OtpRequired,

    /// <summary>Too many device changes in the configured rolling window.</summary>
    CooldownBlocked,
}

public sealed record DeviceResolutionResult(DeviceResolution Resolution);

public interface ITrustedDeviceService
{
    Task<DeviceResolutionResult> ResolveForSignInAsync(
        string authAccountId, string? deviceId, int changeWindowDays, int changeMaxPerWindow, CancellationToken ct);

    /// <summary>Trusts <paramref name="deviceId"/>, revoking whatever device
    /// was previously trusted (and, via <see cref="ISessionRevocationService"/>,
    /// its sessions) — spec §3.2 "approving a new device revokes the old one".</summary>
    Task TrustDeviceAsync(
        string authAccountId, string deviceId, string? deviceName, string? platform, string grantedVia, CancellationToken ct);

    /// <summary>Admin-initiated: clears the account's current trusted device
    /// (next sign-in bootstraps a new one silently) and, since a cleared
    /// device is a security-boundary reset, revokes every live session too —
    /// spec §4.4 admin device reset.</summary>
    Task ResetDeviceAsync(string authAccountId, string reason, CancellationToken ct);

    /// <summary>The account's currently-trusted device, or null when none has
    /// been bootstrapped yet. Read-only (no LastSeenAt touch).</summary>
    Task<TrustedDevice?> GetActiveDeviceAsync(string authAccountId, CancellationToken ct);
}

public sealed class TrustedDeviceService(
    LearnerDbContext db,
    ISessionRevocationService sessionRevocationService,
    ISecurityEventLogger securityEventLogger,
    TimeProvider timeProvider) : ITrustedDeviceService
{
    public async Task<DeviceResolutionResult> ResolveForSignInAsync(
        string authAccountId, string? deviceId, int changeWindowDays, int changeMaxPerWindow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return new DeviceResolutionResult(DeviceResolution.NoDeviceId);
        }

        var current = await db.TrustedDevices
            .Where(d => d.ApplicationUserAccountId == authAccountId && d.RevokedAt == null)
            .OrderByDescending(d => d.TrustedAt)
            .FirstOrDefaultAsync(ct);

        if (current is null)
        {
            return new DeviceResolutionResult(DeviceResolution.Bootstrap);
        }

        if (string.Equals(current.DeviceId, deviceId, StringComparison.Ordinal))
        {
            current.LastSeenAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return new DeviceResolutionResult(DeviceResolution.Trusted);
        }

        var windowStart = timeProvider.GetUtcNow().AddDays(-changeWindowDays);
        var recentChanges = await db.TrustedDevices
            .CountAsync(d => d.ApplicationUserAccountId == authAccountId && d.TrustedAt > windowStart, ct);
        if (recentChanges >= changeMaxPerWindow)
        {
            await securityEventLogger.TryLogAsync(
                authAccountId, SecurityEventKinds.DeviceChangeBlockedCooldown, deviceId: deviceId, cancellationToken: ct);
            return new DeviceResolutionResult(DeviceResolution.CooldownBlocked);
        }

        await securityEventLogger.TryLogAsync(
            authAccountId, SecurityEventKinds.DeviceTrustRequested, deviceId: deviceId, cancellationToken: ct);
        return new DeviceResolutionResult(DeviceResolution.OtpRequired);
    }

    public async Task TrustDeviceAsync(
        string authAccountId, string deviceId, string? deviceName, string? platform, string grantedVia, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var priorDevices = await db.TrustedDevices
            .Where(d => d.ApplicationUserAccountId == authAccountId && d.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var prior in priorDevices)
        {
            prior.RevokedAt = now;
        }

        db.TrustedDevices.Add(new TrustedDevice
        {
            Id = Guid.NewGuid(),
            ApplicationUserAccountId = authAccountId,
            DeviceId = deviceId,
            DeviceName = deviceName,
            Platform = platform,
            CreatedAt = now,
            TrustedAt = now,
            LastSeenAt = now,
            TrustGrantedVia = grantedVia,
        });
        await db.SaveChangesAsync(ct);

        await securityEventLogger.TryLogAsync(
            authAccountId, SecurityEventKinds.DeviceTrusted, deviceId: deviceId,
            details: new { grantedVia }, cancellationToken: ct);

        if (priorDevices.Count > 0)
        {
            // The old device's sessions must die too — a device change is
            // meaningless as a security boundary if the previous device can
            // keep using its still-live session. RevokeAllFamiliesAsync with
            // no exception revokes every current session for the account;
            // the NEW session for this sign-in is created by the caller
            // (AuthService) AFTER this method returns, so there is nothing
            // live yet to accidentally except-out. Only one such call is
            // needed regardless of how many prior device rows there are —
            // it revokes every family for the account, not per-device.
            await sessionRevocationService.RevokeAllFamiliesAsync(authAccountId, exceptFamilyId: null, reason: "device_trusted", ct);

            // Normally there is exactly one prior row (single-writer usage),
            // but a race between two concurrent TrustDeviceAsync calls could
            // leave more than one non-revoked row here — log one DeviceRevoked
            // event per row actually revoked above, not just the first, so the
            // audit trail doesn't silently under-report what was revoked.
            foreach (var prior in priorDevices)
            {
                await securityEventLogger.TryLogAsync(
                    authAccountId, SecurityEventKinds.DeviceRevoked,
                    deviceId: prior.DeviceId, cancellationToken: ct);
            }
        }
    }

    public async Task ResetDeviceAsync(string authAccountId, string reason, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var current = await db.TrustedDevices
            .Where(d => d.ApplicationUserAccountId == authAccountId && d.RevokedAt == null)
            .ToListAsync(ct);
        if (current.Count == 0)
        {
            // Nothing to revoke, but an admin's reset attempt is itself a
            // security-relevant action worth an audit trail — silence here
            // would make it impossible to tell "reset was never attempted"
            // apart from "reset was attempted against an account with no
            // active device", which an admin reviewing the log needs to
            // distinguish.
            await securityEventLogger.TryLogAsync(
                authAccountId, SecurityEventKinds.DeviceAdminReset,
                details: new { noActiveDevice = true }, cancellationToken: ct);
            return;
        }

        foreach (var device in current)
        {
            device.RevokedAt = now;
        }
        await db.SaveChangesAsync(ct);

        await securityEventLogger.TryLogAsync(
            authAccountId, SecurityEventKinds.DeviceAdminReset, cancellationToken: ct);

        // Same as an ordinary device change: clearing trust is meaningless as
        // a security boundary if the old device's live session survives it.
        await sessionRevocationService.RevokeAllFamiliesAsync(authAccountId, exceptFamilyId: null, reason: reason, ct);
    }

    public Task<TrustedDevice?> GetActiveDeviceAsync(string authAccountId, CancellationToken ct)
        => db.TrustedDevices
            .AsNoTracking()
            .Where(d => d.ApplicationUserAccountId == authAccountId && d.RevokedAt == null)
            .OrderByDescending(d => d.TrustedAt)
            .FirstOrDefaultAsync(ct);
}
