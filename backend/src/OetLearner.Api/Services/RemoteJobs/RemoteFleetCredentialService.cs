using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>
/// Issues and rotates the fleet-service credential (<c>ofs1_...</c>, OET-RWP/1 sections 2.2 and 7.2). Only the SHA-256 of
/// the secret is stored; the value is returned to the owner exactly once and is never retrievable again. Rotation keeps
/// the previous credential valid for a grace period so the manager can swap its secret file without a gap.
/// </summary>
public sealed class RemoteFleetCredentialService(
    LearnerDbContext db,
    RemoteJobsSettings settings,
    RemoteAuthCache authCache,
    TimeProvider timeProvider)
{
    private const int MaxActive = 3;

    public async Task<(RemoteIssuedToken? Token, RemoteProblemResult? Error)> IssueAsync(
        int? graceSeconds,
        int? ttlDays,
        string actorId,
        string actorName,
        CancellationToken ct)
    {
        var options = settings.Current;
        var grace = graceSeconds ?? options.TokenRotationGraceSeconds;
        var ttl = ttlDays ?? options.FleetTokenTtlDays;
        if (grace is < 0 or > 86400) return (null, RemoteProblems.BadRequest("graceSeconds must be 0..86400."));
        if (ttl is < 1 or > 365) return (null, RemoteProblems.BadRequest("ttlDays must be 1..365."));

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var active = await db.RemoteCredentials
            .Where(c => c.Kind == RemoteTokenFormat.FleetKind && c.RevokedAt == null && c.ExpiresAt > now)
            .ToListAsync(ct);
        if (active.Count >= MaxActive)
        {
            return (null, RemoteProblems.Conflict("too_many_credentials", "The fleet service already has the maximum number of active credentials."));
        }

        var graceUntil = now.AddSeconds(grace);
        foreach (var credential in active)
        {
            if (grace == 0) credential.RevokedAt = now;
            else if (credential.ExpiresAt > graceUntil) credential.ExpiresAt = graceUntil;
        }

        var token = RemoteTokenFormat.Generate(RemoteTokenFormat.FleetKind);
        var expiresAt = now.AddDays(ttl);
        db.RemoteCredentials.Add(new RemoteCredential
        {
            TokenId = token.TokenId,
            Kind = RemoteTokenFormat.FleetKind,
            NodeId = null,
            SecretHash = token.SecretHashHex,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            CreatedBy = actorId.Length > 64 ? actorId[..64] : actorId,
        });

        // Neither the secret nor its hash goes into the audit row: the token id is enough to correlate.
        RemoteAudit.Add(db, actorId, actorName, "RemoteFleetCredential.Issue", RemoteAudit.ResourceFleetCredential, token.TokenId,
            new { tokenId = token.TokenId, graceSeconds = grace, ttlDays = ttl, retiredCredentials = active.Count }, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        authCache.Clear();

        return (new RemoteIssuedToken(token.TokenId, token.Token, expiresAt), null);
    }
}
