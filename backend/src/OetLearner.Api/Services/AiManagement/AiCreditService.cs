using System.Data;
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

    /// <summary>Debit one or more scoring credits for a persisted usage row.</summary>
    Task<bool> DebitUsageAsync(AiCreditUsageDebitRequest request, CancellationToken ct);

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

public sealed record AiCreditUsageDebitRequest(
    string UserId,
    string UsageRecordId,
    string FeatureCode,
    int Credits,
    decimal CostUsd,
    DateTimeOffset? OccurredAt = null);

public sealed class AiCreditService(LearnerDbContext db) : IAiCreditService
{
    public async Task<AiCreditBalance> GetBalanceAsync(string userId, CancellationToken ct)
    {
        var asOf = DateTimeOffset.UtcNow;

        var rows = await db.AiCreditLedger.AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(ct);

        var lots = AllocateLots(rows, asOf);
        var available = lots.Sum(lot => lot.RemainingTokens);
        var costAvailable = lots.Sum(lot => lot.RemainingCostUsd);

        var grantedLifetime = rows.Where(r => r.TokensDelta > 0).Sum(r => r.TokensDelta);
        var consumedLifetime = Math.Abs(rows
            .Where(r => r.TokensDelta < 0 && r.Source == AiCreditSource.UsageDebit)
            .Sum(r => r.TokensDelta));

        return new AiCreditBalance(
            TokensAvailable: Math.Max(0, available),
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

    public async Task<bool> DebitUsageAsync(AiCreditUsageDebitRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new ArgumentException("User id is required.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.UsageRecordId))
        {
            throw new ArgumentException("Usage record id is required.", nameof(request));
        }
        if (request.Credits <= 0)
        {
            return false;
        }

        await using var transaction = IsInMemoryProvider(db)
            ? null
            : await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var balance = await GetBalanceAsync(request.UserId, ct);
        if (balance.TokensAvailable < request.Credits)
        {
            return false;
        }

        var referenceId = $"usage:{request.UsageRecordId}";
        var existing = await db.AiCreditLedger.AsNoTracking()
            .AnyAsync(x => x.UserId == request.UserId
                           && x.Source == AiCreditSource.UsageDebit
                           && x.ReferenceId == referenceId,
                ct);
        if (existing)
        {
            return false;
        }

        var entry = new AiCreditLedgerEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            TokensDelta = -request.Credits,
            CostDeltaUsd = -Math.Abs(request.CostUsd),
            Source = AiCreditSource.UsageDebit,
            Description = $"{request.FeatureCode} AI usage debit",
            ReferenceId = referenceId,
            CreatedAt = request.OccurredAt ?? DateTimeOffset.UtcNow,
        };

        db.AiCreditLedger.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(entry).State = EntityState.Detached;
            var insertedByRace = await db.AiCreditLedger.AsNoTracking()
                .AnyAsync(x => x.Source == AiCreditSource.UsageDebit
                               && x.ReferenceId == referenceId,
                    ct);
            if (insertedByRace)
            {
                return false;
            }

            throw;
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }
        return true;
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
        var candidateUserIds = await db.AiCreditLedger.AsNoTracking()
            .Where(x => x.TokensDelta > 0
                        && x.ExpiredByEntryId == null
                        && x.ExpiresAt != null
                        && x.ExpiresAt <= asOf)
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);
        if (candidateUserIds.Count == 0) return 0;

        var rows = await db.AiCreditLedger
            .Where(x => candidateUserIds.Contains(x.UserId))
            .ToListAsync(ct);
        var expirable = rows
            .Where(x => x.TokensDelta > 0
                        && x.ExpiredByEntryId == null
                        && x.ExpiresAt != null
                        && x.ExpiresAt <= asOf)
            .ToList();
        if (expirable.Count == 0) return 0;

        foreach (var grant in expirable)
        {
            var remainingTokens = RemainingForGrant(rows, grant.Id, grant.ExpiresAt!.Value.AddTicks(-1));
            if (remainingTokens <= 0)
            {
                grant.ExpiredByEntryId = $"zero-{Guid.NewGuid():N}";
                continue;
            }

            var remainingCost = grant.TokensDelta <= 0
                ? 0m
                : decimal.Round(grant.CostDeltaUsd * remainingTokens / grant.TokensDelta, 6, MidpointRounding.AwayFromZero);
            var expEntry = new AiCreditLedgerEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = grant.UserId,
                TokensDelta = -remainingTokens,
                CostDeltaUsd = -remainingCost,
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

    private static int RemainingForGrant(IReadOnlyList<AiCreditLedgerEntry> rows, string grantId, DateTimeOffset asOf)
        => AllocateLots(rows, asOf)
            .FirstOrDefault(lot => string.Equals(lot.Entry.Id, grantId, StringComparison.Ordinal))
            ?.RemainingTokens ?? 0;

    private static bool IsInMemoryProvider(LearnerDbContext context)
        => context.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true;

    private static List<CreditLot> AllocateLots(IEnumerable<AiCreditLedgerEntry> rows, DateTimeOffset asOf)
    {
        var lots = new List<CreditLot>();
        foreach (var row in rows.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            if (row.TokensDelta > 0)
            {
                if (row.ExpiredByEntryId is not null || (row.ExpiresAt is not null && row.ExpiresAt <= asOf))
                {
                    continue;
                }

                lots.Add(new CreditLot(row));
                continue;
            }

            if (row.TokensDelta >= 0 || row.Source == AiCreditSource.Expiration)
            {
                continue;
            }

            var remainingDebit = Math.Abs(row.TokensDelta);
            foreach (var lot in MatchingLots(lots, row))
            {
                if (remainingDebit <= 0) break;
                if (lot.RemainingTokens <= 0) continue;

                var applied = Math.Min(lot.RemainingTokens, remainingDebit);
                lot.Consume(applied);
                remainingDebit -= applied;
            }
        }

        return lots.Where(lot => lot.RemainingTokens > 0).ToList();
    }

    private static IEnumerable<CreditLot> MatchingLots(IEnumerable<CreditLot> lots, AiCreditLedgerEntry debit)
    {
        var purchaseReference = TryParseRefundPurchaseReference(debit.ReferenceId);
        var candidates = lots.Where(lot => lot.Entry.CreatedAt <= debit.CreatedAt);
        if (purchaseReference is not null)
        {
            candidates = candidates.Where(lot => string.Equals(lot.Entry.ReferenceId, purchaseReference, StringComparison.Ordinal));
        }

        return candidates
            .Where(lot => lot.Entry.ExpiresAt is null || lot.Entry.ExpiresAt > debit.CreatedAt)
            .OrderBy(lot => lot.Entry.ExpiresAt ?? DateTimeOffset.MaxValue)
            .ThenBy(lot => lot.Entry.CreatedAt)
            .ThenBy(lot => lot.Entry.Id, StringComparer.Ordinal);
    }

    private static string? TryParseRefundPurchaseReference(string? referenceId)
    {
        if (string.IsNullOrWhiteSpace(referenceId)) return null;
        if (referenceId.StartsWith("addon-refund:", StringComparison.Ordinal))
        {
            var suffix = referenceId["addon-refund:".Length..];
            return "addon:" + suffix;
        }

        if (referenceId.StartsWith("plan-refund:", StringComparison.Ordinal))
        {
            var suffix = referenceId["plan-refund:".Length..];
            return "plan:" + suffix;
        }

        return null;
    }

    private sealed class CreditLot(AiCreditLedgerEntry entry)
    {
        public AiCreditLedgerEntry Entry { get; } = entry;
        public int RemainingTokens { get; private set; } = entry.TokensDelta;
        public decimal RemainingCostUsd { get; private set; } = Math.Max(0m, entry.CostDeltaUsd);

        public void Consume(int tokens)
        {
            if (tokens <= 0 || RemainingTokens <= 0) return;
            var applied = Math.Min(tokens, RemainingTokens);
            var costShare = RemainingTokens == 0
                ? 0m
                : RemainingCostUsd * applied / RemainingTokens;
            RemainingTokens -= applied;
            RemainingCostUsd = Math.Max(0m, RemainingCostUsd - costShare);
        }
    }
}
