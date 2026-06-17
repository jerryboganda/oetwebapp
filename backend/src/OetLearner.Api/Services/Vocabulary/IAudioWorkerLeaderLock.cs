using System.Data;
using Npgsql;

namespace OetLearner.Api.Services.Vocabulary;

/// <summary>
/// Coordinates which API instance acts as the audio-generation "leader" so the
/// expensive bulk operations (startup batch resume, periodic reconciliation
/// sweep) run on exactly one container instead of every blue/green replica.
///
/// FAIL-OPEN by contract: any uncertainty resolves to <c>true</c>. Because
/// recall audio keys are deterministic, double-processing is idempotent, so the
/// worst case of a false "I'm the leader" is some duplicate TTS spend — never
/// data corruption and never a total halt of generation.
/// </summary>
public interface IAudioWorkerLeaderLock
{
    Task<bool> IsLeaderAsync(CancellationToken ct);
}

/// <summary>
/// Single-instance / non-Postgres (Sqlite, InMemory, tests) leader lock — this
/// process is always the leader.
/// </summary>
public sealed class AlwaysLeaderLock : IAudioWorkerLeaderLock
{
    public Task<bool> IsLeaderAsync(CancellationToken ct) => Task.FromResult(true);
}

/// <summary>
/// Postgres session-level advisory lock. The first replica to win
/// <c>pg_try_advisory_lock</c> holds it for the lifetime of its dedicated
/// connection; if that replica dies the session ends and the lock auto-releases,
/// letting another replica take over on its next check.
/// </summary>
public sealed class PostgresAudioWorkerLeaderLock(
    string connectionString,
    ILogger<PostgresAudioWorkerLeaderLock> logger) : IAudioWorkerLeaderLock, IAsyncDisposable
{
    // Arbitrary but stable application-wide key for the vocab-audio leader.
    private const long AdvisoryKey = 472_026_0617L;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private NpgsqlConnection? _conn;
    private bool _holdsLock;

    public async Task<bool> IsLeaderAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Already holding the lock on a live connection → still leader.
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
                // Another live replica holds it. Drop our idle connection and
                // retry next cycle (the lock frees automatically if that leader
                // dies).
                try { await _conn.CloseAsync().ConfigureAwait(false); } catch { /* ignore */ }
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            // FAIL-OPEN — a lock hiccup must never stop audio generation.
            logger.LogWarning(ex, "Audio leader-lock check failed; assuming leader (fail-open)");
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
