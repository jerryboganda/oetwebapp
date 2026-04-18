using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.AiManagement;

// ═════════════════════════════════════════════════════════════════════════════
// Credit ledger (Slice 6). Append-only journal of AI credit movements.
//
// This exists alongside (not replacing) the quota counter in Slice 2.
// Quota counter = "how many tokens have you used this period?"
// Credit ledger = "where did your credit allowance come from, and when does
//                 each grant expire?"
//
// The two converge at renewal time: the renewal worker reads the plan,
// inserts a PlanRenewal ledger entry, and resets the monthly counter.
// ═════════════════════════════════════════════════════════════════════════════

public interface IAiCreditService
{
    /// <summary>Compute the non-expired balance for a user.</summary>
    Task<AiCreditBalance> GetBalanceAsync(string userId, CancellationToken ct);

    /// <summary>Grant credits (positive TokensDelta).</summary>
    Task<AiCreditLedgerEntry> GrantAsync(
        string userId,
        int tokens,
        decimal costUsd,
        AiCreditSource source,
        string? description,
        string? referenceId,
        DateTimeOffset? expiresAt,
        string? adminId,
        CancellationToken ct);

    /// <summary>List ledger entries for admin / learner dashboards.</summary>
    Task<IReadOnlyList<AiCreditLedgerEntry>> ListAsync(
        string userId, int page, int pageSize, CancellationToken ct);

    /// <summary>Sweep expired grants, inserting matching Expiration entries.
    /// Idempotent. Intended for a scheduled job (Slice 7).</summary>
    Task<int> SweepExpiredAsync(DateTimeOffset asOf, CancellationToken ct);
}

public sealed record AiCreditBalance(
    int TokensAvailable,
    decimal CostAvailableUsd,
    int TokensGrantedLifetime,
    int TokensConsumedLifetime);

public sealed class AiCreditService(LearnerDbContext db) : IAiCreditService
{
    public async Task<AiCreditBalance> GetBalanceAsync(string userId, CancellationToken ct)
    {
        var asOf = DateTimeOffset.UtcNow;

        // Non-expired grants: entries with no expiry, or not-yet-expired,
        // or already-flipped-by-Expiration (those are already zero-netted).
        var rows = await db.AiCreditLedger.AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(ct);

        var available = rows
            .Where(r => r.TokensDelta > 0 && r.ExpiredByEntryId == null
                        && (r.ExpiresAt == null || r.ExpiresAt > asOf))
            .Sum(r => r.TokensDelta);

        var debits = rows
            .Where(r => r.TokensDelta < 0 && r.Source == AiCreditSource.UsageDebit)
            .Sum(r => r.TokensDelta);

        var costAvailable = rows
            .Where(r => r.TokensDelta > 0 && r.ExpiredByEntryId == null
                        && (r.ExpiresAt == null || r.ExpiresAt > asOf))
            .Sum(r => r.CostDeltaUsd);

        var grantedLifetime = rows.Where(r => r.TokensDelta > 0).Sum(r => r.TokensDelta);
        var consumedLifetime = Math.Abs(debits);

        return new AiCreditBalance(
            TokensAvailable: Math.Max(0, available + debits),
            CostAvailableUsd: Math.Max(0m, costAvailable),
            TokensGrantedLifetime: grantedLifetime,
            TokensConsumedLifetime: consumedLifetime);
    }

    public async Task<AiCreditLedgerEntry> GrantAsync(
        string userId,
        int tokens,
        decimal costUsd,
        AiCreditSource source,
        string? description,
        string? referenceId,
        DateTimeOffset? expiresAt,
        string? adminId,
        CancellationToken ct)
    {
        if (tokens <= 0) throw new ArgumentOutOfRangeException(nameof(tokens), "Grants must be positive.");
        var entry = new AiCreditLedgerEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            TokensDelta = tokens,
            CostDeltaUsd = costUsd,
            Source = source,
            Description = description,
            ReferenceId = referenceId,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByAdminId = adminId,
        };
        db.AiCreditLedger.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<IReadOnlyList<AiCreditLedgerEntry>> ListAsync(
        string userId, int page, int pageSize, CancellationToken ct)
    {
        var p = Math.Max(1, page);
        var s = Math.Clamp(pageSize, 1, 200);
        return await db.AiCreditLedger.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Skip((p - 1) * s).Take(s)
            .ToListAsync(ct);
    }

    public async Task<int> SweepExpiredAsync(DateTimeOffset asOf, CancellationToken ct)
    {
        var expirable = await db.AiCreditLedger
            .Where(x => x.TokensDelta > 0
                        && x.ExpiredByEntryId == null
                        && x.ExpiresAt != null && x.ExpiresAt <= asOf)
            .ToListAsync(ct);
        if (expirable.Count == 0) return 0;

        foreach (var grant in expirable)
        {
            var expEntry = new AiCreditLedgerEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = grant.UserId,
                TokensDelta = -grant.TokensDelta,
                CostDeltaUsd = -grant.CostDeltaUsd,
                Source = AiCreditSource.Expiration,
                Description = $"Expired grant {grant.Id}",
                ReferenceId = grant.Id,
                CreatedAt = asOf,
            };
            db.AiCreditLedger.Add(expEntry);
            grant.ExpiredByEntryId = expEntry.Id;
        }
        await db.SaveChangesAsync(ct);
        return expirable.Count;
    }
}
