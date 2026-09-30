using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

/// <summary>How an <see cref="AiOperation"/> insert resolved.</summary>
public enum AiOperationInsertOutcome
{
    /// <summary>This caller owns the operation and must perform the work.</summary>
    Inserted = 0,

    /// <summary>The exact same canonical action already exists
    /// (<c>UX_AiOperations_IdempotencyKey</c>). Safe replay — never a second
    /// provider call.</summary>
    DuplicateIdempotencyKey = 1,

    /// <summary>A DIFFERENT payload already owns this business resource slot
    /// (<c>UX_AiOperations_ResourceSlotKey</c>). Controlled conflict — the
    /// caller should bump the resource version instead of racing.</summary>
    ResourceSlotConflict = 2,
}

public sealed record AiOperationInsertResult(AiOperationInsertOutcome Outcome, AiOperation Operation);

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// the ONLY place <see cref="AiOperation"/> rows are created/closed.
///
/// <para>
/// The whole point is that ownership is decided by the database, never by a
/// read-then-insert check: both racing callers issue the INSERT, exactly one
/// commits, and the loser is classified from the real unique-violation. A
/// unique violation is attributed <b>by constraint name</b> so an
/// idempotency-key collision (safe duplicate) is never confused with a
/// resource-slot collision (conflict), and — critically — every OTHER
/// <see cref="DbUpdateException"/> (foreign key, check, length, deadlock, …)
/// is re-thrown rather than being laundered into a retryable "someone else is
/// in flight" answer.
/// </para>
///
/// <para>
/// <b>Isolation.</b> Every method runs against a DEDICATED, short-lived
/// <see cref="LearnerDbContext"/> from its own <see cref="IServiceScope"/> —
/// the same singleton-safe pattern <see cref="DirectAiCallRecorder"/> uses.
/// Sharing the caller's request-scoped context would mean the control-plane
/// <c>SaveChanges</c> also flushes whatever unrelated domain changes the
/// business caller happens to be tracking, committing them BEFORE the AI call
/// they were meant to follow. The control plane must never decide when a
/// caller's domain writes land.
/// </para>
/// </summary>
public interface IAiOperationStore
{
    Task<AiOperationInsertResult> TryInsertAsync(AiOperation operation, CancellationToken ct);

    /// <summary>State-conditional close. Returns true when this call was the
    /// one that moved the operation out of a non-terminal state.</summary>
    Task<bool> TryMarkTerminalAsync(
        string operationId,
        AiOperationState state,
        string? resultRef,
        string? selectedProviderId,
        string? selectedModel,
        CancellationToken ct);

    Task<AiOperation?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct);

    Task<AiOperation?> FindByResourceSlotAsync(string resourceSlotKey, CancellationToken ct);

    /// <summary>Delete the operation owning <paramref name="resourceSlotKey"/> ONLY when it is a
    /// proven safe-failure (FailedTerminal / Cancelled / BlockedBudget) that produced no result
    /// and was never billed. Returns true when a stale row was removed, freeing the slot for a
    /// fresh attempt. A Completed / InFlight / Indeterminate owner is NEVER deleted — those may
    /// have been billed or hold a real result, so they must keep their slot (the double-billing
    /// guard this index exists for). This is the unblock path for the retry-storm deadlock where
    /// a fresh submission collides with a ghost of a previous failed attempt.</summary>
    Task<bool> DeleteIfSafeFailureAsync(string resourceSlotKey, CancellationToken ct);
}

public sealed class AiOperationStore : IAiOperationStore
{
    internal const string IdempotencyIndexName = "UX_AiOperations_IdempotencyKey";
    internal const string ResourceSlotIndexName = "UX_AiOperations_ResourceSlotKey";

    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly LearnerDbContext? _fixedDb;

    /// <summary>Production constructor — the only one DI can see. Each call
    /// opens its own scope so the caller's tracked domain changes are never
    /// flushed by control-plane persistence.</summary>
    public AiOperationStore(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Test/diagnostic seam: bind the store to one caller-owned context (a
    /// Sqlite in-memory or single-schema PostgreSQL fixture). Deliberately
    /// <c>internal</c> so the DI container cannot select it and accidentally
    /// re-introduce the shared-context flush.
    /// </summary>
    internal AiOperationStore(LearnerDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _fixedDb = db;
    }

    public async Task<AiOperationInsertResult> TryInsertAsync(AiOperation operation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (_fixedDb is not null) return await TryInsertCoreAsync(_fixedDb, operation, ct);

        await using var scope = _scopeFactory!.CreateAsyncScope();
        return await TryInsertCoreAsync(scope.ServiceProvider.GetRequiredService<LearnerDbContext>(), operation, ct);
    }

    public async Task<bool> TryMarkTerminalAsync(
        string operationId,
        AiOperationState state,
        string? resultRef,
        string? selectedProviderId,
        string? selectedModel,
        CancellationToken ct)
    {
        if (_fixedDb is not null)
        {
            return await TryMarkTerminalCoreAsync(_fixedDb, operationId, state, resultRef, selectedProviderId, selectedModel, ct);
        }

        await using var scope = _scopeFactory!.CreateAsyncScope();
        return await TryMarkTerminalCoreAsync(
            scope.ServiceProvider.GetRequiredService<LearnerDbContext>(),
            operationId, state, resultRef, selectedProviderId, selectedModel, ct);
    }

    public async Task<AiOperation?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct)
    {
        if (_fixedDb is not null) return await FindByIdempotencyKeyCoreAsync(_fixedDb, idempotencyKey, ct);

        await using var scope = _scopeFactory!.CreateAsyncScope();
        return await FindByIdempotencyKeyCoreAsync(
            scope.ServiceProvider.GetRequiredService<LearnerDbContext>(), idempotencyKey, ct);
    }

    public async Task<AiOperation?> FindByResourceSlotAsync(string resourceSlotKey, CancellationToken ct)
    {
        if (_fixedDb is not null) return await FindByResourceSlotCoreAsync(_fixedDb, resourceSlotKey, ct);

        await using var scope = _scopeFactory!.CreateAsyncScope();
        return await FindByResourceSlotCoreAsync(
            scope.ServiceProvider.GetRequiredService<LearnerDbContext>(), resourceSlotKey, ct);
    }

    public async Task<bool> DeleteIfSafeFailureAsync(string resourceSlotKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(resourceSlotKey)) return false;
        if (_fixedDb is not null) return await DeleteIfSafeFailureCoreAsync(_fixedDb, resourceSlotKey, ct);

        await using var scope = _scopeFactory!.CreateAsyncScope();
        return await DeleteIfSafeFailureCoreAsync(
            scope.ServiceProvider.GetRequiredService<LearnerDbContext>(), resourceSlotKey, ct);
    }

    // ── Core operations (one dedicated context per call) ───────────────────────

    private async Task<AiOperationInsertResult> TryInsertCoreAsync(
        LearnerDbContext db, AiOperation operation, CancellationToken ct)
    {
        db.AiOperations.Add(operation);
        try
        {
            await db.SaveChangesAsync(ct);
            // Detach immediately: every subsequent transition goes through the
            // scoped ExecuteUpdate in TryMarkTerminalAsync, never through
            // change tracking. A tracked entity would be re-flushed (with
            // stale values) by any unrelated SaveChanges later in the same
            // scope — e.g. the usage recorder's own insert — and clobber the
            // reconciliation.
            db.Entry(operation).State = EntityState.Detached;
            return new AiOperationInsertResult(AiOperationInsertOutcome.Inserted, operation);
        }
        catch (DbUpdateException ex)
        {
            db.Entry(operation).State = EntityState.Detached;

            var outcome = ClassifyUniqueViolation(ex);
            if (outcome is null)
            {
                // Not a unique violation we own (FK / check / length /
                // deadlock / transport). Turning these into "in flight" would
                // hide real corruption behind a retry loop.
                throw;
            }

            // A row can violate BOTH unique indexes at once (identical replay
            // of a resource-scoped action). Which one the engine reports first
            // is an implementation detail of index-check ordering, so an
            // apparent slot conflict is downgraded to a duplicate whenever the
            // idempotency key — which already hashes the request payload, so a
            // match means the SAME canonical action — is present. Without this
            // a plain retry would surface as a hard conflict on one engine and
            // a safe replay on another.
            if (outcome == AiOperationInsertOutcome.ResourceSlotConflict
                && await FindByIdempotencyKeyCoreAsync(db, operation.IdempotencyKey, ct) is not null)
            {
                return new AiOperationInsertResult(AiOperationInsertOutcome.DuplicateIdempotencyKey, operation);
            }

            return new AiOperationInsertResult(outcome.Value, operation);
        }
    }

    private static async Task<bool> TryMarkTerminalCoreAsync(
        LearnerDbContext db,
        string operationId,
        AiOperationState state,
        string? resultRef,
        string? selectedProviderId,
        string? selectedModel,
        CancellationToken ct)
    {
        // State-conditional: only a non-terminal row may be closed, so a late
        // reconciler can never overwrite an already-terminal outcome.
        var updated = await db.AiOperations
            .Where(o => o.Id == operationId
                && (o.State == AiOperationState.Queued
                    || o.State == AiOperationState.Leased
                    || o.State == AiOperationState.ProviderSucceeded
                    || o.State == AiOperationState.RetryScheduled))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(o => o.State, state)
                    .SetProperty(o => o.ResultRef, resultRef)
                    .SetProperty(o => o.SelectedProviderId, selectedProviderId)
                    .SetProperty(o => o.SelectedModel, selectedModel)
                    .SetProperty(o => o.UpdatedAt, DateTimeOffset.UtcNow),
                ct);

        return updated > 0;
    }

    private static Task<AiOperation?> FindByIdempotencyKeyCoreAsync(
        LearnerDbContext db, string idempotencyKey, CancellationToken ct)
        => db.AiOperations.AsNoTracking().FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey, ct);

    private static Task<AiOperation?> FindByResourceSlotCoreAsync(
        LearnerDbContext db, string resourceSlotKey, CancellationToken ct)
        => db.AiOperations.AsNoTracking().FirstOrDefaultAsync(o => o.ResourceSlotKey == resourceSlotKey, ct);

    private static async Task<bool> DeleteIfSafeFailureCoreAsync(
        LearnerDbContext db, string resourceSlotKey, CancellationToken ct)
    {
        // State-conditional delete: only a proven safe-failure row is removed, and the
        // predicate is enforced in SQL so a concurrent transition to a non-safe state
        // makes the delete a no-op (a row that just Completed is never removed).
        var deleted = await db.AiOperations
            .Where(o => o.ResourceSlotKey == resourceSlotKey
                && (o.State == AiOperationState.FailedTerminal
                    || o.State == AiOperationState.Cancelled
                    || o.State == AiOperationState.BlockedBudget))
            .ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    /// <summary>
    /// Attributes a unique violation to one of the two constraints we own, or
    /// null when the exception is anything else (caller must re-throw).
    ///
    /// <para>
    /// PostgreSQL is the authoritative production path: SQLSTATE 23505 plus
    /// <c>ConstraintName</c>. SQLite (used by focused unit tests and the
    /// desktop backend) reports no constraint name, so its message — which
    /// always names the failing column as
    /// <c>UNIQUE constraint failed: AiOperations.&lt;Column&gt;</c> — is
    /// matched instead. An unrecognised unique violation is deliberately NOT
    /// classified: better a loud failure than a wrong duplicate/conflict
    /// verdict.
    /// </para>
    /// </summary>
    internal static AiOperationInsertOutcome? ClassifyUniqueViolation(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is Npgsql.PostgresException pg)
            {
                if (!string.Equals(pg.SqlState, "23505", StringComparison.Ordinal)) continue;
                if (string.Equals(pg.ConstraintName, IdempotencyIndexName, StringComparison.Ordinal))
                    return AiOperationInsertOutcome.DuplicateIdempotencyKey;
                if (string.Equals(pg.ConstraintName, ResourceSlotIndexName, StringComparison.Ordinal))
                    return AiOperationInsertOutcome.ResourceSlotConflict;
                return null;
            }

            if (current is Microsoft.Data.Sqlite.SqliteException sqlite)
            {
                // 19 == SQLITE_CONSTRAINT.
                if (sqlite.SqliteErrorCode != 19) continue;
                var message = sqlite.Message ?? string.Empty;
                if (message.Contains("AiOperations.IdempotencyKey", StringComparison.OrdinalIgnoreCase))
                    return AiOperationInsertOutcome.DuplicateIdempotencyKey;
                if (message.Contains("AiOperations.ResourceSlotKey", StringComparison.OrdinalIgnoreCase))
                    return AiOperationInsertOutcome.ResourceSlotConflict;
                return null;
            }
        }

        return null;
    }
}
