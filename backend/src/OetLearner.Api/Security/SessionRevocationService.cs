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
        await db.SaveChangesAsync(ct);

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
        await db.SaveChangesAsync(ct);

        await RevokePlaybackAndNotifyAsync(authAccountId, [familyId], reason, ct);
        return true;
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
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(learnerId))
        {
            await playbackSessions.RevokeAllForUserAsync(learnerId, ct);
        }

        foreach (var familyId in familyIds)
        {
            await securityEventLogger.TryLogAsync(
                authAccountId,
                SecurityEventKinds.SessionRevoked,
                sessionFamilyId: familyId,
                details: new { reason },
                cancellationToken: ct);

            try
            {
                await notificationHub.Clients
                    .Group(NotificationHub.SessionFamilyGroup(familyId))
                    .SendAsync("session_revoked", new { reason }, ct);
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
}
