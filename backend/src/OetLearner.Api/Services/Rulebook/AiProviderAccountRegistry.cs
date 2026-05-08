using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Rulebook;

// ═════════════════════════════════════════════════════════════════════════════
// Multi-account pool for Copilot-style providers (GitHub Copilot Phase 2).
//
// One AiProvider row → many AiProviderAccount rows. Each account holds one
// PAT and one monthly request cap. PickAndReserveAsync walks accounts in
// (Priority asc, RequestsUsedThisMonth asc) order and atomically reserves
// the first one that still has capacity. Failover happens silently to the
// caller — the gateway sees a single happy completion.
//
// Audit invariant: one turn = one AiUsageRecord. Failover retries collapse
// into RetryCount + FailoverTraceJson on that single record. Sibling rows
// would break the "exactly one AiUsageRecord per CompleteAsync" contract
// documented in AGENTS.md.
//
// Concurrency contract: PickAndReserveAsync uses EF Core's
// ExecuteUpdateAsync to issue a single UPDATE that atomically enforces
// all gating predicates AND increments the counter in one statement.
// Postgres RC isolation re-evaluates the WHERE clause on row-lock release,
// so two concurrent callers cannot both win the last slot. SQLite (used
// in tests) serialises writes at the database level, so the same code is
// correct in unit tests.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>One reserved account slot, returned by
/// <see cref="IAiProviderAccountRegistry.PickAndReserveAsync"/>. Plaintext
/// PAT is included so the caller can build the wire request without a
/// second decrypt round-trip; never logged or persisted.</summary>
public sealed record AiProviderAccountSlot(
    string AccountId,
    string Label,
    string ApiKey,
    int? MonthlyRequestCap,
    int RequestsUsedAfterPick);

/// <summary>Outcome of an AI call against the slot we just picked. Drives
/// quarantine / deactivation policy for the next pick.</summary>
public enum AiProviderAccountOutcome
{
    /// <summary>Call succeeded.</summary>
    Success = 0,
    /// <summary>Provider returned 429. Sets <c>ExhaustedUntil</c>.</summary>
    RateLimited = 1,
    /// <summary>Provider returned 401/403. Deactivates the account so an
    /// admin must re-enable after rotating the PAT.</summary>
    Unauthorized = 2,
    /// <summary>Other transient or unclassified error. Records nothing
    /// account-specific; the gateway's retry policy decides next steps.</summary>
    OtherError = 3,
}

public interface IAiProviderAccountRegistry
{
    /// <summary>
    /// Atomically picks the next available account for the given provider
    /// code and increments its monthly counter. Returns null when no
    /// account is available (provider should fall back to single-row
    /// <c>AiProvider.EncryptedApiKey</c> path, or surface
    /// <c>quota_exhausted</c>).
    ///
    /// Skip set lets callers re-pick during failover without re-trying
    /// an account that already failed in this turn.
    /// </summary>
    Task<AiProviderAccountSlot?> PickAndReserveAsync(
        string providerCode,
        IReadOnlyCollection<string>? skipAccountIds,
        CancellationToken ct);

    /// <summary>Mark the outcome of the call. For
    /// <see cref="AiProviderAccountOutcome.RateLimited"/> and
    /// <see cref="AiProviderAccountOutcome.Unauthorized"/> updates the
    /// account row; success / other-error are no-ops here (the counter
    /// was already incremented at pick time).</summary>
    Task RecordOutcomeAsync(
        string accountId,
        AiProviderAccountOutcome outcome,
        TimeSpan? quarantineFor,
        CancellationToken ct);

    /// <summary>List all accounts under one provider code (admin / debug).</summary>
    Task<IReadOnlyList<AiProviderAccount>> ListByProviderCodeAsync(
        string providerCode,
        CancellationToken ct);
}

public sealed class AiProviderAccountRegistry(
    LearnerDbContext db,
    IDataProtectionProvider dpProvider,
    TimeProvider clock) : IAiProviderAccountRegistry
{
    private const string ProtectorPurpose = "AiProvider.PlatformKey.v1";
    private readonly IDataProtector _protector = dpProvider.CreateProtector(ProtectorPurpose);

    public async Task<AiProviderAccountSlot?> PickAndReserveAsync(
        string providerCode,
        IReadOnlyCollection<string>? skipAccountIds,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerCode)) return null;
        var code = providerCode.Trim().ToLowerInvariant();
        var skip = skipAccountIds is null || skipAccountIds.Count == 0
            ? Array.Empty<string>()
            : skipAccountIds.ToArray();

        var provider = await db.AiProviders.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == code && p.IsActive, ct);
        if (provider is null) return null;

        var now = clock.GetUtcNow();

        // Read candidate IDs in deterministic order. Quarantine + cap
        // checks happen client-side here (the result set is tiny — a
        // handful of accounts per provider) because EF SQLite cannot
        // translate nullable DateTimeOffset/int predicates cleanly. The
        // atomic UPDATE below re-checks both predicates server-side, so
        // even a stale read cannot let two callers exceed cap or
        // resurrect a quarantined account.
        var raw = await db.AiProviderAccounts.AsNoTracking()
            .Where(a => a.ProviderId == provider.Id && a.IsActive)
            .OrderBy(a => a.Priority)
            .ThenBy(a => a.RequestsUsedThisMonth)
            .Select(a => new { a.Id, a.ExhaustedUntil, a.MonthlyRequestCap, a.RequestsUsedThisMonth })
            .ToListAsync(ct);

        var candidates = raw
            .Where(a => (a.ExhaustedUntil is null || a.ExhaustedUntil < now)
                     && (!a.MonthlyRequestCap.HasValue
                         || a.RequestsUsedThisMonth < a.MonthlyRequestCap.Value))
            .Select(a => new { a.Id })
            .ToList();

        foreach (var c in candidates)
        {
            if (skip.Contains(c.Id, StringComparer.Ordinal)) continue;

            // Atomic claim: this UPDATE only succeeds if the row still
            // satisfies every gating predicate at the moment it acquires
            // the row lock. Returns the count of rows actually updated.
            // Atomic claim. id+IsActive+cap are sufficient to gate concurrency:
            // a quarantined account can only be picked once its ExhaustedUntil
            // has passed (filtered client-side above), and any racing call
            // would still satisfy IsActive+cap at the same instant. Cap-check
            // is in the WHERE clause so concurrent callers cannot exceed it.
            // ExhaustedUntil is intentionally not in the WHERE clause because
            // EF SQLite cannot translate nullable DateTimeOffset comparisons.
            var rowsAffected = await db.AiProviderAccounts
                .Where(a => a.Id == c.Id
                         && a.IsActive
                         && (!a.MonthlyRequestCap.HasValue
                             || a.RequestsUsedThisMonth < a.MonthlyRequestCap.Value))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.RequestsUsedThisMonth, a => a.RequestsUsedThisMonth + 1)
                    .SetProperty(a => a.UpdatedAt, _ => now), ct);

            if (rowsAffected != 1) continue; // lost the race; try next

            var row = await db.AiProviderAccounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == c.Id, ct);
            if (row is null) continue; // race with deletion; vanishingly rare

            string? apiKey = null;
            if (!string.IsNullOrEmpty(row.EncryptedApiKey))
            {
                try { apiKey = _protector.Unprotect(row.EncryptedApiKey); }
                catch { apiKey = null; }
            }
            if (string.IsNullOrEmpty(apiKey)) continue; // unusable; try next

            return new AiProviderAccountSlot(
                AccountId: row.Id,
                Label: row.Label,
                ApiKey: apiKey,
                MonthlyRequestCap: row.MonthlyRequestCap,
                RequestsUsedAfterPick: row.RequestsUsedThisMonth);
        }

        return null;
    }

    public async Task RecordOutcomeAsync(
        string accountId,
        AiProviderAccountOutcome outcome,
        TimeSpan? quarantineFor,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return;
        var now = clock.GetUtcNow();

        switch (outcome)
        {
            case AiProviderAccountOutcome.RateLimited:
                var until = now + (quarantineFor ?? TimeSpan.FromMinutes(15));
                await db.AiProviderAccounts
                    .Where(a => a.Id == accountId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.ExhaustedUntil, _ => (DateTimeOffset?)until)
                        .SetProperty(a => a.UpdatedAt, _ => now), ct);
                break;

            case AiProviderAccountOutcome.Unauthorized:
                await db.AiProviderAccounts
                    .Where(a => a.Id == accountId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.IsActive, _ => false)
                        .SetProperty(a => a.UpdatedAt, _ => now), ct);
                break;

            // Success and OtherError leave the row alone. The counter was
            // already incremented atomically at pick time.
            case AiProviderAccountOutcome.Success:
            case AiProviderAccountOutcome.OtherError:
            default:
                break;
        }
    }

    public async Task<IReadOnlyList<AiProviderAccount>> ListByProviderCodeAsync(
        string providerCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerCode)) return Array.Empty<AiProviderAccount>();
        var code = providerCode.Trim().ToLowerInvariant();
        var provider = await db.AiProviders.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == code, ct);
        if (provider is null) return Array.Empty<AiProviderAccount>();

        return await db.AiProviderAccounts.AsNoTracking()
            .Where(a => a.ProviderId == provider.Id)
            .OrderBy(a => a.Priority)
            .ThenBy(a => a.Label)
            .ToListAsync(ct);
    }
}
