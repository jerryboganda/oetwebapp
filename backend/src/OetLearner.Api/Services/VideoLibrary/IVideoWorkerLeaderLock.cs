using System.Data;
using Npgsql;

namespace OetLearner.Api.Services.VideoLibrary;

/// <summary>
/// Coordinates which API instance runs the Bunny encode-status reconciliation
/// sweep so it executes on exactly one container instead of every blue/green
/// replica. Copy of the vocabulary audio leader lock with a DIFFERENT advisory
/// key. FAIL-OPEN: reconciliation is idempotent (it just re-reads Bunny state),
/// so a false "I'm the leader" wastes a few API calls, never corrupts data.
/// </summary>
public interface IVideoWorkerLeaderLock
{
    Task<bool> IsLeaderAsync(CancellationToken ct);
}

/// <summary>Single-instance / non-Postgres (SQLite, InMemory, tests) — always the leader.</summary>
public sealed class AlwaysVideoWorkerLeaderLock : IVideoWorkerLeaderLock
{
    public Task<bool> IsLeaderAsync(CancellationToken ct) => Task.FromResult(true);
}

/// <summary>
/// Postgres session-level advisory lock. The first replica to win
/// <c>pg_try_advisory_lock</c> holds it for the lifetime of its dedicated
/// connection; if that replica dies the session ends and the lock auto-releases.
/// </summary>
public sealed class PostgresVideoWorkerLeaderLock(
    string connectionString,
    ILogger<PostgresVideoWorkerLeaderLock> logger) : IVideoWorkerLeaderLock, IAsyncDisposable
{
    // Arbitrary but stable application-wide key for the video-library worker.
    // MUST differ from the vocab-audio key (472_026_0617).
    private const long AdvisoryKey = 472_026_0718L;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private NpgsqlConnection? _conn;
    private bool _holdsLock;

    public async Task<bool> IsLeaderAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_holdsLock && _conn is { State: ConnectionState.Open })
                return true;

            _conn ??= new NpgsqlConnection(connectionString);
            if (_conn.State != ConnectionState.Open)
                await _conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_lock(@k)";
            var p = cmd.CreateParameter();
            p.ParameterName = "k";
            p.Value = AdvisoryKey;
            cmd.Parameters.Add(p);

            var acquired = (bool)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            _holdsLock = acquired;

            if (!acquired)
            {
                try { await _conn.CloseAsync().ConfigureAwait(false); } catch { /* ignore */ }
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            // FAIL-OPEN — a lock hiccup must never stop encode reconciliation.
            logger.LogWarning(ex, "Video worker leader-lock check failed; assuming leader (fail-open)");
            _holdsLock = false;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_conn is not null) await _conn.DisposeAsync().ConfigureAwait(false); }
        catch { /* best-effort */ }
    }
}
