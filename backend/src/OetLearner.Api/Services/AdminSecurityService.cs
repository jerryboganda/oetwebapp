using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Services;

/// <summary>Admin-facing session/device management (Course Platform Security
/// Requirements §4.4) — targeted session revoke, trusted-device reset, and
/// immediate playback block for a single account. Every mutation here is
/// dual-logged: a <see cref="SecurityEvent"/> (the account's own security
/// history) plus an <see cref="AuditEvent"/> (the admin's action log), and
/// routes through <see cref="ISessionRevocationService"/>/
/// <see cref="ITrustedDeviceService"/> rather than touching
/// <see cref="Domain.RefreshTokenRecord"/>/<see cref="TrustedDevice"/> rows
/// directly, so playback-kill + push-notify + logging all happen together.</summary>
public sealed class AdminSecurityService(
    LearnerDbContext db,
    AdminService adminService,
    ISessionRevocationService sessionRevocationService,
    ITrustedDeviceService trustedDeviceService,
    IVideoPlaybackSessionService playbackSessions,
    ISecurityEventLogger securityEventLogger,
    OetLearner.Api.Services.Settings.IRuntimeSettingsProvider runtimeSettingsProvider)
{
    public async Task<IReadOnlyList<AdminSecuritySessionResponse>> GetSessionsAsync(string userId, CancellationToken ct)
    {
        var authAccountId = await adminService.ResolveAuthAccountIdAsync(userId, ct);
        if (authAccountId is null)
        {
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        return await db.RefreshTokenRecords.AsNoTracking()
            .Where(t => t.ApplicationUserAccountId == authAccountId && t.RevokedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.LastUsedAt ?? t.CreatedAt)
            .Take(20)
            .Select(t => new AdminSecuritySessionResponse(
                t.FamilyId, t.IpAddress, t.CountryCode, t.DeviceInfo, t.CreatedAt, t.LastUsedAt, t.ExpiresAt,
                t.DeviceId, t.Platform))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AdminSecurityDeviceResponse>> GetDevicesAsync(string userId, CancellationToken ct)
    {
        var authAccountId = await adminService.ResolveAuthAccountIdAsync(userId, ct);
        if (authAccountId is null)
        {
            return [];
        }

        return await db.TrustedDevices.AsNoTracking()
            .Where(d => d.ApplicationUserAccountId == authAccountId)
            .OrderByDescending(d => d.TrustedAt)
            .Take(10)
            .Select(d => new AdminSecurityDeviceResponse(
                d.Id, d.DeviceId, d.DeviceName, d.Platform, d.TrustedAt, d.LastSeenAt, d.RevokedAt))
            .ToListAsync(ct);
    }

    public async Task<bool> RevokeSessionAsync(
        string adminId, string adminName, string userId, Guid familyId, CancellationToken ct)
    {
        var authAccountId = await RequireAuthAccountIdAsync(userId, ct);
        var revoked = await sessionRevocationService.RevokeFamilyAsync(authAccountId, familyId, "admin_revoke", ct);
        if (revoked)
        {
            await securityEventLogger.TryLogAsync(
                authAccountId, SecurityEventKinds.AdminSessionRevoked, sessionFamilyId: familyId, cancellationToken: ct);
            await LogAuditAsync(adminId, adminName, "Revoked Session", "User", userId,
                $"Revoked session family {familyId}.", ct);
        }
        return revoked;
    }

    public async Task ResetDeviceAsync(string adminId, string adminName, string userId, CancellationToken ct)
    {
        var authAccountId = await RequireAuthAccountIdAsync(userId, ct);
        await trustedDeviceService.ResetDeviceAsync(authAccountId, "admin_reset", ct);
        await securityEventLogger.TryLogAsync(authAccountId, SecurityEventKinds.AdminDeviceReset, cancellationToken: ct);
        await LogAuditAsync(adminId, adminName, "Reset Trusted Device", "User", userId,
            "Cleared the account's trusted device; next sign-in bootstraps a new one.", ct);
    }

    public async Task<bool> SetDeviceExemptionAsync(
        string adminId, string adminName, string userId, bool exempt, CancellationToken ct)
    {
        var learner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (learner is null)
        {
            throw ApiException.NotFound("user_not_found", "User not found.");
        }

        var authAccountId = learner.AuthAccountId;
        var emailToToggle = learner.Email?.Trim();
        if (string.IsNullOrWhiteSpace(emailToToggle) && authAccountId is not null)
        {
            var authAcc = await db.ApplicationUserAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == authAccountId, ct);
            emailToToggle = authAcc?.Email?.Trim();
        }

        if (string.IsNullOrWhiteSpace(emailToToggle))
        {
            throw ApiException.Validation("email_missing", "This user account does not have an email address.");
        }

        var normalizedEmail = AuthEmailAddress.NormalizeOrThrow(emailToToggle);

        var row = await db.RuntimeSettings.FirstOrDefaultAsync(ct)
            ?? new RuntimeSettingsRow { Id = "default" };

        var currentList = (row.SecurityDeviceVerificationExemptEmails ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        if (exempt)
        {
            if (!currentList.Contains(normalizedEmail, StringComparer.OrdinalIgnoreCase))
            {
                currentList.Add(normalizedEmail);
            }
            if (authAccountId is not null)
            {
                await trustedDeviceService.ResetDeviceAsync(authAccountId, "admin_exemption_granted", ct);
            }
        }
        else
        {
            currentList.RemoveAll(e => string.Equals(e, normalizedEmail, StringComparison.OrdinalIgnoreCase));
        }

        var updatedCsv = string.Join(',', currentList);
        row.SecurityDeviceVerificationExemptEmails = updatedCsv.Length > 0 ? updatedCsv : null;

        if (db.Entry(row).State == EntityState.Detached)
        {
            db.RuntimeSettings.Add(row);
        }
        await db.SaveChangesAsync(ct);
        runtimeSettingsProvider.Invalidate();

        await LogAuditAsync(adminId, adminName, exempt ? "Granted Device Security Exemption" : "Revoked Device Security Exemption",
            "User", userId, $"Set 'Too Many Device Changes' security exemption to {exempt} for {normalizedEmail}.", ct);

        return exempt;
    }

    public async Task ClearDeviceCooldownAsync(
        string adminId, string adminName, string userId, CancellationToken ct)
    {
        var authAccountId = await RequireAuthAccountIdAsync(userId, ct);
        await trustedDeviceService.ResetDeviceAsync(authAccountId, "admin_clear_cooldown", ct);
        await securityEventLogger.TryLogAsync(authAccountId, SecurityEventKinds.AdminDeviceReset, cancellationToken: ct);
        await LogAuditAsync(adminId, adminName, "Cleared Device Cooldown", "User", userId,
            "Cleared active device change cooldown and reset trusted device for sign-in.", ct);
    }

    public async Task<int> BlockPlaybackAsync(string adminId, string adminName, string userId, CancellationToken ct)
    {
        var authAccountId = await adminService.ResolveAuthAccountIdAsync(userId, ct);
        var revoked = await playbackSessions.RevokeAllForUserAsync(userId, ct);
        if (authAccountId is not null)
        {
            await securityEventLogger.TryLogAsync(
                authAccountId, SecurityEventKinds.AdminPlaybackBlocked, details: new { revoked }, cancellationToken: ct);
        }
        await LogAuditAsync(adminId, adminName, "Blocked Video Playback", "User", userId,
            $"Revoked {revoked} active playback session(s).", ct);
        return revoked;
    }

    public async Task<AdminDeviceLimitUpdateResponse> SetCandidateDeviceLimitAsync(
        string adminId, string adminName, string userId, int? maxDevices, CancellationToken ct)
    {
        if (maxDevices is < 1 or > TrustedDeviceService.MaxAllowedDevicesOverride)
        {
            throw ApiException.Validation(
                "invalid_device_limit",
                $"The approved-device limit must be between 1 and {TrustedDeviceService.MaxAllowedDevicesOverride}, or left at default.");
        }

        var authAccountId = await RequireAuthAccountIdAsync(userId, ct);
        var account = await db.ApplicationUserAccounts.FirstOrDefaultAsync(a => a.Id == authAccountId, ct);
        if (account is null)
        {
            throw ApiException.NotFound("user_not_found", "User account not found.");
        }

        var previousOverride = account.MaxDevicesOverride;
        account.MaxDevicesOverride = maxDevices;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var effectiveMaxDevices = await trustedDeviceService.GetEffectiveMaxDevicesAsync(authAccountId, CancellationToken.None);
        var revokedDevices = await trustedDeviceService.EnforceDeviceLimitAsync(authAccountId, effectiveMaxDevices, CancellationToken.None);
        await securityEventLogger.TryLogAsync(
            authAccountId,
            SecurityEventKinds.AdminDeviceLimitOverride,
            details: new
            {
                previousOverride,
                maxDevices,
                effectiveMaxDevices,
                revokedDevices,
            },
            cancellationToken: CancellationToken.None);
        await LogAuditAsync(adminId, adminName, "Updated Candidate Device Limit", "User", userId,
            $"Updated approved client-identity limit override from {previousOverride?.ToString() ?? "default"} to {maxDevices?.ToString() ?? "default"}. Effective limit: {effectiveMaxDevices}; revoked devices: {revokedDevices}.", CancellationToken.None);

        return new AdminDeviceLimitUpdateResponse(maxDevices, effectiveMaxDevices, revokedDevices);
    }

    public async Task<bool> RevokeDeviceAsync(
        string adminId, string adminName, string userId, Guid deviceId, CancellationToken ct)
    {
        var authAccountId = await RequireAuthAccountIdAsync(userId, ct);
        var device = await db.TrustedDevices.FirstOrDefaultAsync(
            d => d.ApplicationUserAccountId == authAccountId && d.Id == deviceId, ct);
        if (device is null)
        {
            return false;
        }

        device.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);

        var revokedSessions = await sessionRevocationService.RevokeDeviceFamiliesAsync(
            authAccountId, device.DeviceId, "admin_device_revoke", CancellationToken.None);

        await securityEventLogger.TryLogAsync(
            authAccountId,
            SecurityEventKinds.DeviceRevoked,
            deviceId: device.DeviceId,
            details: new { reason = "admin_device_revoke", revokedSessions },
            cancellationToken: CancellationToken.None);
        await LogAuditAsync(adminId, adminName, "Revoked Device", "User", userId,
            $"Revoked registered device {device.DeviceName ?? device.DeviceId}; revoked {revokedSessions} associated session family(ies).", CancellationToken.None);

        return true;
    }

    private async Task<string> RequireAuthAccountIdAsync(string userId, CancellationToken ct)
        => await adminService.ResolveAuthAccountIdAsync(userId, ct)
           ?? throw ApiException.Validation("auth_account_missing", "This user does not have an authentication account.");

    private async Task LogAuditAsync(
        string actorId, string actorName, string action, string resourceType, string? resourceId, string? details, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorAuthAccountId = actorId,
            ActorName = actorName,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
        });
        await db.SaveChangesAsync(ct);
    }
}

public sealed record AdminSecuritySessionResponse(
    Guid FamilyId,
    string? IpAddress,
    string? CountryCode,
    string? DeviceInfo,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset ExpiresAt,
    string? DeviceId,
    string? Platform);

public sealed record AdminDeviceLimitUpdateResponse(
    int? MaxDevices,
    int EffectiveMaxDevices,
    int RevokedDevices);

public sealed record AdminSecurityDeviceResponse(
    Guid Id,
    string DeviceId,
    string? DeviceName,
    string? Platform,
    DateTimeOffset TrustedAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? RevokedAt);
