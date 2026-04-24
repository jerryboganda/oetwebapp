using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Data;

/// <summary>
/// Small bounded-retry helper for <see cref="DbUpdateConcurrencyException"/>.
///
/// Use only on idempotent read-modify-write paths — typically webhook handlers
/// and background workers that re-derive their target state from input data
/// rather than diffing against the pre-read entity. User-initiated edits MUST
/// NOT use this helper; those flows should surface the conflict to the caller
/// as <c>409 Conflict</c> so the user can reload and retry.
///
/// <para>
/// The <paramref name="action"/> delegate runs inside a fresh scope each time:
/// callers must re-load the entity themselves. This avoids the common
/// stale-tracking-graph bug where a retry carries over the original xmin.
/// </para>
/// </summary>
public static class ConcurrencyRetry
{
    /// <summary>
    /// Runs <paramref name="action"/>, retrying up to <paramref name="maxAttempts"/>
    /// times on <see cref="DbUpdateConcurrencyException"/>. Any other exception
    /// (including <see cref="DbUpdateException"/>) is rethrown immediately.
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        int maxAttempts = 3,
        CancellationToken ct = default)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }
        DbUpdateConcurrencyException? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action(ct);
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < maxAttempts)
            {
                last = ex;
                // Fall through and retry. Caller is responsible for
                // re-reading state inside the delegate.
            }
        }
        throw last ?? new DbUpdateConcurrencyException("Concurrency retry exhausted.");
    }

    /// <inheritdoc cref="ExecuteAsync{T}(System.Func{System.Threading.CancellationToken, System.Threading.Tasks.Task{T}}, int, System.Threading.CancellationToken)"/>
    public static async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        int maxAttempts = 3,
        CancellationToken ct = default)
    {
        await ExecuteAsync<int>(async token => { await action(token); return 0; }, maxAttempts, ct);
    }
}
