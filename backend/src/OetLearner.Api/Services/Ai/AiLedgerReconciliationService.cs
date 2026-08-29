using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Ai;

public sealed record AiLedgerDiscrepancy(
    string UserId,
    int LedgerTokenBalance,
    int PackageCreditBalance,
    int Delta,
    IReadOnlyList<string> Notes);

public sealed record AiLedgerReconciliationReport(
    DateTimeOffset GeneratedAtUtc,
    int UsersScanned,
    int DiscrepancyCount,
    IReadOnlyList<AiLedgerDiscrepancy> Discrepancies,
    bool MutatedBalances);

public interface IAiLedgerReconciliationService
{
    Task<AiLedgerReconciliationReport> BuildReportAsync(CancellationToken ct);
}

/// <summary>
/// Compares legacy <c>AiCreditLedger</c> token balances with package credit
/// accounts. Produces an owner-reviewable report and never mutates balances.
/// </summary>
public sealed class AiLedgerReconciliationService(LearnerDbContext db) : IAiLedgerReconciliationService
{
    public async Task<AiLedgerReconciliationReport> BuildReportAsync(CancellationToken ct)
    {
        var ledger = await db.AiCreditLedger.AsNoTracking()
            .GroupBy(e => e.UserId)
            .Select(g => new { UserId = g.Key, Balance = g.Sum(x => x.TokensDelta) })
            .ToListAsync(ct);

        var packages = await db.AiPackageCreditAccounts.AsNoTracking()
            .Select(a => new
            {
                a.UserId,
                Balance = a.SharedCredits + a.FlexibleCredits + a.WritingOnlyCredits + a.SpeakingOnlyCredits,
            })
            .ToListAsync(ct);

        var unreferenced = await db.AiCreditLedger.AsNoTracking()
            .Where(e => e.ReferenceId == null || e.ReferenceId == "")
            .GroupBy(e => e.UserId)
            .Select(g => g.Key)
            .ToListAsync(ct);

        var unreferencedSet = unreferenced.ToHashSet(StringComparer.Ordinal);
        var packageByUser = packages.ToDictionary(p => p.UserId, p => p.Balance, StringComparer.Ordinal);
        var users = ledger.Select(l => l.UserId)
            .Concat(packages.Select(p => p.UserId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(u => u, StringComparer.Ordinal)
            .ToList();

        var discrepancies = new List<AiLedgerDiscrepancy>();
        foreach (var userId in users)
        {
            var ledgerBalance = ledger.FirstOrDefault(l => l.UserId == userId)?.Balance ?? 0;
            packageByUser.TryGetValue(userId, out var packageBalance);
            var notes = new List<string>();
            if (ledgerBalance != packageBalance)
                notes.Add("ledger_vs_package_mismatch");
            if (unreferencedSet.Contains(userId))
                notes.Add("ledger_rows_missing_source_reference");
            if (notes.Count == 0)
                continue;

            discrepancies.Add(new AiLedgerDiscrepancy(
                userId, ledgerBalance, packageBalance, ledgerBalance - packageBalance, notes));
        }

        return new AiLedgerReconciliationReport(
            DateTimeOffset.UtcNow,
            users.Count,
            discrepancies.Count,
            discrepancies,
            MutatedBalances: false);
    }
}
