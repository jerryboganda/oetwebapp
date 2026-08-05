using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Security;

public sealed class SessionRevocationService(
    LearnerDbContext db,
    IVideoPlaybackSessionService playbackSessions,
    IHubContext<NotificationHub> notificationHub,
    ISecurityEventLogger securityEventLogger,
    ILogger<SessionRevocationService> logger,
    TimeProvider timeProvider) : ISessionRevocationService
{
    public async Task<int> RevokeAllFamiliesAsync(
        string authAccountId, Guid? exceptFamilyId, string reason, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var tokens = await db.RefreshTokenRecords
            .Where(t => t.ApplicationUserAccountId == authAccountId && t.RevokedAt == null)
            .ToListAsync(ct);

        var familyIds = tokens
            .Select(t => t.FamilyId)
            .Distinct()
            .Where(family => exceptFamilyId is null || family != exceptFamilyId.Value)
            .ToList();
        if (familyIds.Count == 0) return 0;

        var familySet = familyIds.ToHashSet();
        foreach (var token in tokens.Where(t => familySet.Contains(t.FamilyId)))
        {
            token.RevokedAt = now;
        }
        // A disconnect must not cancel the security boundary after the
        // revocation decision has been made.
        await db.SaveChangesAsync(CancellationToken.None);

        await RevokePlaybackAndNotifyAsync(authAccountId, familyIds, reason, ct);
        return familyIds.Count;
    }

    public async Task<bool> RevokeFamilyAsync(
        string authAccountId, Guid familyId, string reason, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var tokens = await db.RefreshTokenRecords
            .Where(t => t.ApplicationUserAccountId == authAccountId && t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);
        if (tokens.Count == 0) return false;

        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }
        await db.SaveChangesAsync(CancellationToken.None);

        await RevokePlaybackAndNotifyAsync(authAccountId, [familyId], reason, ct);
        return true;
    }

    public async Task<int> RevokeDeviceFamiliesAsync(
        string authAccountId, string deviceId, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return 0;

        var familyIds = await db.RefreshTokenRecords
            .AsNoTracking()
            .Where(t => t.ApplicationUserAccountId == authAccountId
                && t.DeviceId == deviceId
                && t.RevokedAt == null)
            .Select(t => t.FamilyId)
            .Distinct()
            .ToListAsync(ct);

        var revoked = 0;
        foreach (var familyId in familyIds)
        {
            if (await RevokeFamilyAsync(authAccountId, familyId, reason, ct))
            {
                revoked++;
            }
        }

        return revoked;
    }

    private async Task RevokePlaybackAndNotifyAsync(
        string authAccountId, IReadOnlyList<Guid> familyIds, string reason, CancellationToken ct)
    {
        // Video playback sessions key on LearnerUser.Id, not the auth-account
        // id these families are keyed on — resolve it once. Null for
        // expert/admin accounts (no video library access), which is fine:
        // there's nothing to revoke there.
        var learnerId = await db.Users.AsNoTracking()
            .Where(u => u.AuthAccountId == authAccountId)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(learnerId))
        {
            try
            {
                await playbackSessions.RevokeAllForUserAsync(learnerId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Token-family invalidation and audit evidence remain the hard
                // boundary even if a playback store is temporarily unavailable.
                logger.LogWarning(ex, "Failed to revoke playback sessions for account {AuthAccountId}", authAccountId);
            }
        }

        foreach (var familyId in familyIds)
        {
            await securityEventLogger.TryLogAsync(
                authAccountId,
                SecurityEventKinds.SessionRevoked,
                sessionFamilyId: familyId,
                details: new { reason },
                cancellationToken: CancellationToken.None);

            var message = RevocationMessage(reason);
            await WriteSystemAuditAsync(authAccountId, familyId, reason, message, CancellationToken.None);

            try
            {
                await notificationHub.Clients
                    .Group(NotificationHub.SessionFamilyGroup(familyId))
                    .SendAsync("session_revoked", new { reason, message }, ct);
            }
            catch (Exception ex)
            {
                // Best-effort push — enforcement doesn't depend on it. The
                // family-liveness check in OnTokenValidated (worst case: next
                // access-token expiry) and the playback-session revoke above
                // are the hard guarantees; this push only shortens the delay.
                logger.LogWarning(ex, "Failed to push session_revoked for family {FamilyId}", familyId);
            }
        }
    }

    private async Task WriteSystemAuditAsync(
        string authAccountId, Guid familyId, string reason, string? message, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = timeProvider.GetUtcNow(),
            ActorId = "security-system",
            ActorName = "Security System",
            Action = message is null ? "Session Revoked" : "Device Session Revoked",
            ResourceType = "AuthAccount",
            ResourceId = authAccountId,
            Details = $"Session family {familyId} was revoked automatically (reason: {reason}). {message}".Trim(),
        });
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static string? RevocationMessage(string reason) => reason switch
    {
        "device_replaced" => "This session was signed out because a new device was approved for this account under the one-device security rule.",
        "device_limit_replaced" => "This session was signed out because the account's device limit was reached and a newer device was approved.",
        "device_limit_reduced" => "This session was signed out because an administrator reduced the allowed device limit for this account.",
        "admin_device_revoke" => "This session was signed out because an administrator revoked its device approval.",
        _ => null,
    };
}
