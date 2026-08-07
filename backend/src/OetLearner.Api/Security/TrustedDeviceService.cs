using System.Data;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Security;

public enum DeviceResolution
{
    /// <summary>No device id was presented. The auth service rejects this
    /// result whenever trusted-device enforcement is active.</summary>
    NoDeviceId,

    /// <summary>The device id was present but malformed or too long.</summary>
    InvalidDeviceId,

    /// <summary>The presented device id is already approved for the account.</summary>
    Trusted,

    /// <summary>The account has no approved device yet — this one is trusted
    /// silently (registration already proved email ownership).</summary>
    Bootstrap,

    /// <summary>A different client identity needs email-OTP approval before it
    /// can occupy a device slot.</summary>
    OtpRequired,

    /// <summary>Too many OTP-approved device replacements in the configured
    /// rolling window.</summary>
    CooldownBlocked,
}

public sealed record DeviceResolutionResult(DeviceResolution Resolution);

public interface ITrustedDeviceService
{
    Task<DeviceResolutionResult> ResolveForSignInAsync(
        string authAccountId, string? deviceId, int changeWindowDays, int changeMaxPerWindow, CancellationToken ct);

    /// <summary>Approves a client identity. The default policy keeps one active
    /// identity and therefore revokes the previous device's sessions; an
    /// explicit positive per-learner override may retain more identities.</summary>
    Task TrustDeviceAsync(
        string authAccountId, string deviceId, string? deviceName, string? platform, string grantedVia, CancellationToken ct);

    /// <summary>Admin-initiated: clears every approved device and revokes every
    /// live session because a cleared device is a security-boundary reset.</summary>
    Task ResetDeviceAsync(string authAccountId, string reason, CancellationToken ct);

    /// <summary>Returns active approved client identities ordered by recent use.</summary>
    Task<IReadOnlyList<TrustedDevice>> GetActiveDevicesAsync(string authAccountId, CancellationToken ct);

    /// <summary>Returns the effective approved-device limit. The safe default is
    /// one; invalid legacy overrides are treated as the default.</summary>
    Task<int> GetEffectiveMaxDevicesAsync(string authAccountId, CancellationToken ct);

    /// <summary>Immediately trims active devices after an admin lowers a
    /// learner's limit and revokes sessions tied to identities that no longer
    /// fit the policy.</summary>
    Task<int> EnforceDeviceLimitAsync(string authAccountId, int maxDevices, CancellationToken ct);

    /// <summary>Compatibility helper for the learner sessions card. Returns the
    /// most recently used active identity, or null.</summary>
    Task<TrustedDevice?> GetActiveDeviceAsync(string authAccountId, CancellationToken ct);
}

public sealed class TrustedDeviceService(
    LearnerDbContext db,
    ISessionRevocationService sessionRevocationService,
    ISecurityEventLogger securityEventLogger,
    TimeProvider timeProvider) : ITrustedDeviceService
{
    public const int DefaultMaxDevices = 1;
    public const int MaxAllowedDevicesOverride = 5;

    public async Task<DeviceResolutionResult> ResolveForSignInAsync(
        string authAccountId, string? deviceId, int changeWindowDays, int changeMaxPerWindow, CancellationToken ct)
    {
        var normalizedDeviceId = NormalizeDeviceId(deviceId);
        if (normalizedDeviceId is null)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                await LogRejectedAsync(authAccountId, null, "missing_device_id", ct);
                return new DeviceResolutionResult(DeviceResolution.NoDeviceId);
            }

            await LogRejectedAsync(authAccountId, deviceId, "invalid_device_id", ct);
            return new DeviceResolutionResult(DeviceResolution.InvalidDeviceId);
        }

        var activeDevices = await GetTrackedActiveDevicesAsync(authAccountId, ct);
        var matchingDevice = activeDevices.FirstOrDefault(d =>
            string.Equals(d.DeviceId, normalizedDeviceId, StringComparison.Ordinal));

        if (matchingDevice is not null)
        {
            matchingDevice.LastSeenAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return new DeviceResolutionResult(DeviceResolution.Trusted);
        }

        if (activeDevices.Count == 0)
        {
            return new DeviceResolutionResult(DeviceResolution.Bootstrap);
        }

        // This is deliberately separate from the per-learner number of active
        // device slots. The rolling cooldown limits OTP-approved replacements;
        // the initial bootstrap is not a device change and must not consume a
        // legitimate learner's replacement budget. An admin override changes
        // how many identities may remain approved, not how many times a learner
        // may rotate devices in a short period.
        var windowStart = timeProvider.GetUtcNow().AddDays(-Math.Max(1, changeWindowDays));
        var recentChanges = await db.TrustedDevices
            .CountAsync(d => d.ApplicationUserAccountId == authAccountId
                && d.TrustedAt > windowStart
                && d.TrustGrantedVia == "otp_verified", ct);
        var effectiveChangeLimit = Math.Max(1, changeMaxPerWindow);
        if (recentChanges >= effectiveChangeLimit)
        {
            await securityEventLogger.TryLogAsync(
                authAccountId,
                SecurityEventKinds.DeviceChangeBlockedCooldown,
                deviceId: normalizedDeviceId,
                details: new
                {
                    reason = "cooldown",
                    recentChanges,
                    changeWindowDays,
                    changeMaxPerWindow = effectiveChangeLimit,
                    activeDeviceCount = activeDevices.Count,
                },
                cancellationToken: CancellationToken.None);
            await LogRejectedAsync(authAccountId, normalizedDeviceId, "cooldown", CancellationToken.None);
            return new DeviceResolutionResult(DeviceResolution.CooldownBlocked);
        }

        var maxDevices = await GetEffectiveMaxDevicesAsync(authAccountId, ct);
        await securityEventLogger.TryLogAsync(
            authAccountId,
            SecurityEventKinds.DeviceTrustRequested,
            deviceId: normalizedDeviceId,
            details: new { activeDeviceCount = activeDevices.Count, maxDevices },
            cancellationToken: ct);
        return new DeviceResolutionResult(DeviceResolution.OtpRequired);
    }

    public Task TrustDeviceAsync(
        string authAccountId, string deviceId, string? deviceName, string? platform, string grantedVia, CancellationToken ct)
        => WithDeviceMutationLockAsync(
            authAccountId,
            ct,
            () => TrustDeviceCoreAsync(authAccountId, deviceId, deviceName, platform, grantedVia, ct));

    private async Task TrustDeviceCoreAsync(
        string authAccountId, string deviceId, string? deviceName, string? platform, string grantedVia, CancellationToken ct)
    {
        var normalizedDeviceId = NormalizeDeviceId(deviceId)
            ?? throw new InvalidOperationException("A valid device id is required to approve a device.");
        var now = timeProvider.GetUtcNow();
        var maxDevices = await GetEffectiveMaxDevicesAsync(authAccountId, ct);
        var activeDevices = await GetTrackedActiveDevicesAsync(authAccountId, ct);

        var existing = activeDevices.FirstOrDefault(d =>
            string.Equals(d.DeviceId, normalizedDeviceId, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.LastSeenAt = now;
            await db.SaveChangesAsync(ct);
            return;
        }

        var devicesToRevoke = activeDevices
            .OrderBy(d => d.LastSeenAt ?? d.TrustedAt)
            .ThenBy(d => d.TrustedAt)
            .Take(Math.Max(0, activeDevices.Count - maxDevices + 1))
            .ToList();

        foreach (var prior in devicesToRevoke)
        {
            prior.RevokedAt = now;
        }

        db.TrustedDevices.Add(new TrustedDevice
        {
            Id = Guid.NewGuid(),
            ApplicationUserAccountId = authAccountId,
            DeviceId = normalizedDeviceId,
            DeviceName = Truncate(deviceName, 256),
            Platform = Truncate(platform, 32),
            CreatedAt = now,
            TrustedAt = now,
            LastSeenAt = now,
            TrustGrantedVia = Truncate(grantedVia, 32) ?? "otp_verified",
        });
        // Audit evidence must survive a client disconnect after the security
        // decision has already been made.
        await db.SaveChangesAsync(CancellationToken.None);

        var replacementReason = maxDevices == DefaultMaxDevices
            ? "device_replaced"
            : "device_limit_replaced";

        await securityEventLogger.TryLogAsync(
            authAccountId,
            SecurityEventKinds.DeviceTrusted,
            deviceId: normalizedDeviceId,
            details: new
            {
                grantedVia,
                activeDeviceCount = activeDevices.Count - devicesToRevoke.Count + 1,
                maxDevices,
                replacedDeviceCount = devicesToRevoke.Count,
            },
            cancellationToken: CancellationToken.None);
        await LogSystemAuditAsync(
            authAccountId,
            "Device Approved",
            $"Approved client identity {MaskDeviceId(normalizedDeviceId)} via {grantedVia}. Active device count is now {activeDevices.Count - devicesToRevoke.Count + 1}/{maxDevices}.",
            CancellationToken.None);

        if (devicesToRevoke.Count == 0)
        {
            return;
        }

        foreach (var prior in devicesToRevoke)
        {
            await securityEventLogger.TryLogAsync(
                authAccountId,
                SecurityEventKinds.DeviceRevoked,
                deviceId: prior.DeviceId,
                details: new { reason = replacementReason, replacedByDeviceId = normalizedDeviceId },
                cancellationToken: CancellationToken.None);
            await LogSystemAuditAsync(
                authAccountId,
                "Device Revoked",
                $"Revoked client identity {MaskDeviceId(prior.DeviceId)} because {MaskDeviceId(normalizedDeviceId)} was approved under the active device limit.",
                CancellationToken.None);
        }

        if (maxDevices == DefaultMaxDevices)
        {
            // The strict one-device rule revokes every previous family. There
            // should be only one active identity, but this also heals an
            // anomalous concurrent-write state without leaving a live session.
            await sessionRevocationService.RevokeAllFamiliesAsync(
                authAccountId, exceptFamilyId: null, reason: replacementReason, CancellationToken.None);
            return;
        }

        foreach (var prior in devicesToRevoke)
        {
            await sessionRevocationService.RevokeDeviceFamiliesAsync(
                authAccountId, prior.DeviceId, replacementReason, CancellationToken.None);
        }
    }

    public Task ResetDeviceAsync(string authAccountId, string reason, CancellationToken ct)
        => WithDeviceMutationLockAsync(
            authAccountId,
            ct,
            () => ResetDeviceCoreAsync(authAccountId, reason, ct));

    private async Task ResetDeviceCoreAsync(string authAccountId, string reason, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var current = await GetTrackedActiveDevicesAsync(authAccountId, ct);
        if (current.Count == 0)
        {
            await securityEventLogger.TryLogAsync(
                authAccountId,
                SecurityEventKinds.DeviceAdminReset,
                details: new { noActiveDevice = true, reason },
                cancellationToken: CancellationToken.None);
            await LogSystemAuditAsync(authAccountId, "Device Approval Reset", "A device reset was requested while no active device was registered.", CancellationToken.None);
            return;
        }

        foreach (var device in current)
        {
            device.RevokedAt = now;
        }
        await db.SaveChangesAsync(CancellationToken.None);

        await securityEventLogger.TryLogAsync(
            authAccountId,
            SecurityEventKinds.DeviceAdminReset,
            details: new { revokedDeviceCount = current.Count, reason },
            cancellationToken: CancellationToken.None);
        await LogSystemAuditAsync(
            authAccountId,
            "Device Approval Reset",
            $"Cleared {current.Count} approved client identity(ies); the next sign-in must bootstrap a new device.",
            CancellationToken.None);

        await sessionRevocationService.RevokeAllFamiliesAsync(authAccountId, exceptFamilyId: null, reason, CancellationToken.None);
    }

    public async Task<IReadOnlyList<TrustedDevice>> GetActiveDevicesAsync(string authAccountId, CancellationToken ct)
        => await db.TrustedDevices
            .AsNoTracking()
            .Where(d => d.ApplicationUserAccountId == authAccountId && d.RevokedAt == null)
            .OrderByDescending(d => d.LastSeenAt ?? d.TrustedAt)
            .ThenByDescending(d => d.TrustedAt)
            .ToListAsync(ct);

    public async Task<int> GetEffectiveMaxDevicesAsync(string authAccountId, CancellationToken ct)
    {
        var overrideValue = await db.ApplicationUserAccounts
            .AsNoTracking()
            .Where(a => a.Id == authAccountId)
            .Select(a => a.MaxDevicesOverride)
            .FirstOrDefaultAsync(ct);
        return ResolveEffectiveMaxDevices(overrideValue);
    }

    public Task<int> EnforceDeviceLimitAsync(string authAccountId, int maxDevices, CancellationToken ct)
        => WithDeviceMutationLockResultAsync(
            authAccountId,
            ct,
            () => EnforceDeviceLimitCoreAsync(authAccountId, maxDevices, ct));

    private async Task<int> EnforceDeviceLimitCoreAsync(string authAccountId, int maxDevices, CancellationToken ct)
    {
        maxDevices = ResolveRequestedMaxDevices(maxDevices);
        var now = timeProvider.GetUtcNow();
        var activeDevices = await GetTrackedActiveDevicesAsync(authAccountId, ct);
        var devicesToRevoke = activeDevices
            .OrderBy(d => d.LastSeenAt ?? d.TrustedAt)
            .ThenBy(d => d.TrustedAt)
            .Take(Math.Max(0, activeDevices.Count - maxDevices))
            .ToList();
        if (devicesToRevoke.Count == 0) return 0;

        foreach (var device in devicesToRevoke)
        {
            device.RevokedAt = now;
        }
        await db.SaveChangesAsync(CancellationToken.None);

        foreach (var device in devicesToRevoke)
        {
            await securityEventLogger.TryLogAsync(
                authAccountId,
                SecurityEventKinds.DeviceRevoked,
                deviceId: device.DeviceId,
                details: new { reason = "device_limit_reduced", maxDevices },
                cancellationToken: CancellationToken.None);
            await LogSystemAuditAsync(
                authAccountId,
                "Device Revoked",
                $"Revoked client identity {MaskDeviceId(device.DeviceId)} because the approved-device limit was reduced to {maxDevices}.",
                CancellationToken.None);
            await sessionRevocationService.RevokeDeviceFamiliesAsync(
                authAccountId, device.DeviceId, "device_limit_reduced", CancellationToken.None);
        }

        return devicesToRevoke.Count;
    }

    public async Task<TrustedDevice?> GetActiveDeviceAsync(string authAccountId, CancellationToken ct)
        => await db.TrustedDevices
            .AsNoTracking()
            .Where(d => d.ApplicationUserAccountId == authAccountId && d.RevokedAt == null)
            .OrderByDescending(d => d.LastSeenAt ?? d.TrustedAt)
            .ThenByDescending(d => d.TrustedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<List<TrustedDevice>> GetTrackedActiveDevicesAsync(string authAccountId, CancellationToken ct)
        => await db.TrustedDevices
            .Where(d => d.ApplicationUserAccountId == authAccountId && d.RevokedAt == null)
            .OrderByDescending(d => d.LastSeenAt ?? d.TrustedAt)
            .ThenByDescending(d => d.TrustedAt)
            .ToListAsync(ct);

    private async Task LogRejectedAsync(string authAccountId, string? deviceId, string reason, CancellationToken ct)
    {
        await securityEventLogger.TryLogAsync(
            authAccountId,
            SecurityEventKinds.DeviceTrustRejected,
            deviceId: Truncate(deviceId, 128),
            details: new { reason },
            cancellationToken: ct);
        await LogSystemAuditAsync(
            authAccountId,
            "Device Rejected",
            $"Rejected a device approval request ({reason}) for client identity {MaskDeviceId(deviceId)}.",
            ct);
    }

    private async Task LogSystemAuditAsync(string authAccountId, string action, string details, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = timeProvider.GetUtcNow(),
            ActorId = "security-system",
            ActorName = "Security System",
            Action = action,
            ResourceType = "AuthAccount",
            ResourceId = authAccountId,
            Details = details,
        });
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Serializes device-slot mutations per authentication account on
    /// PostgreSQL. Without the row lock, two concurrent OTP completions could
    /// both observe a free slot and leave more active identities than the
    /// policy allows. InMemory/SQLite test providers deliberately skip the
    /// PostgreSQL-specific lock.</summary>
    private async Task WithDeviceMutationLockAsync(
        string authAccountId,
        CancellationToken ct,
        Func<Task> mutation)
    {
        if (!IsPostgresProvider())
        {
            await mutation();
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await LockAccountRowAsync(authAccountId, ct);
            await mutation();
            await transaction.CommitAsync(CancellationToken.None);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<T> WithDeviceMutationLockResultAsync<T>(
        string authAccountId,
        CancellationToken ct,
        Func<Task<T>> mutation)
    {
        if (!IsPostgresProvider())
        {
            return await mutation();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await LockAccountRowAsync(authAccountId, ct);
            var result = await mutation();
            await transaction.CommitAsync(CancellationToken.None);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task LockAccountRowAsync(string authAccountId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM \"ApplicationUserAccounts\" WHERE \"Id\" = {authAccountId} FOR UPDATE",
            ct);
    }

    private bool IsPostgresProvider()
        => db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private static int ResolveEffectiveMaxDevices(int? overrideValue)
        => overrideValue is > 0 and <= MaxAllowedDevicesOverride
            ? overrideValue.Value
            : DefaultMaxDevices;

    private static int ResolveRequestedMaxDevices(int maxDevices)
    {
        if (maxDevices is < 1 or > MaxAllowedDevicesOverride)
        {
            throw ApiException.Validation(
                "invalid_device_limit",
                $"The approved-device limit must be between 1 and {MaxAllowedDevicesOverride}.");
        }

        return maxDevices;
    }

    private static string? NormalizeDeviceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > 128 || normalized.Any(char.IsControl)) return null;
        return normalized;
    }

    private static string? Truncate(string? value, int maxLength)
        => value is null ? null : value.Length > maxLength ? value[..maxLength] : value;

    private static string MaskDeviceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown device";
        var normalized = new string(value.Trim().Select(character =>
            char.IsControl(character) ? '?' : character).ToArray());
        return normalized.Length <= 8 ? normalized : $"{normalized[..4]}…{normalized[^4..]}";
    }
}
