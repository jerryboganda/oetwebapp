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
    ISecurityEventLogger securityEventLogger)
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
                t.FamilyId, t.IpAddress, t.CountryCode, t.DeviceInfo, t.CreatedAt, t.LastUsedAt, t.ExpiresAt))
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
    DateTimeOffset ExpiresAt);

public sealed record AdminSecurityDeviceResponse(
    Guid Id,
    string DeviceId,
    string? DeviceName,
    string? Platform,
    DateTimeOffset TrustedAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? RevokedAt);
