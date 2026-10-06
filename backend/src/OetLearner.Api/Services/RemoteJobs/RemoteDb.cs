using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>
/// Raw-SQL access for the remote-job state machine. Claim, fenced completion and the reaper use
/// Postgres-only statements (<c>FOR UPDATE SKIP LOCKED</c>, <c>clock_timestamp()</c>), so every entry
/// point asserts the provider (OET-RWP/1 section 9.4: there is no SQLite or InMemory code path).
///
/// <para>
/// The scope opens the context's connection only if it is not open already and closes it again when
/// nothing else (a transaction) still needs it, so no handler ever holds a pooled connection while it
/// streams a body or waits for a slow client.
/// </para>
/// </summary>
internal sealed class PgScope : IAsyncDisposable
{
    private readonly LearnerDbContext _db;
    private readonly bool _opened;

    private PgScope(LearnerDbContext db, NpgsqlConnection connection, bool opened)
    {
        _db = db;
        Connection = connection;
        _opened = opened;
    }

    public NpgsqlConnection Connection { get; }

    public static async Task<PgScope> OpenAsync(LearnerDbContext db, CancellationToken ct)
    {
        RequireNpgsql(db);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
        {
            await db.Database.OpenConnectionAsync(ct);
        }

        return new PgScope(db, connection, opened);
    }

    /// <summary>Throws <see cref="NotSupportedException"/> unless the context runs on Npgsql.</summary>
    public static void RequireNpgsql(LearnerDbContext db)
    {
        if (!db.Database.IsNpgsql())
        {
            throw new NotSupportedException("Remote jobs require PostgreSQL; there is no SQLite or InMemory path.");
        }
    }

    /// <summary>A command bound to the context's current transaction (if any).</summary>
    public NpgsqlCommand Command(string sql)
    {
        var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
        return command;
    }

    public async ValueTask DisposeAsync()
    {
        if (_opened && _db.Database.CurrentTransaction is null)
        {
            await _db.Database.CloseConnectionAsync();
        }
    }
}

internal static class RemoteDb
{
    /// <summary>Executes a statement and returns the affected row count.</summary>
    public static async Task<int> ExecuteAsync(
        LearnerDbContext db,
        string sql,
        Action<NpgsqlParameterCollection>? bind,
        CancellationToken ct)
    {
        await using var scope = await PgScope.OpenAsync(db, ct);
        await using var command = scope.Command(sql);
        bind?.Invoke(command.Parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Executes a query and maps every row.</summary>
    public static async Task<List<T>> QueryAsync<T>(
        LearnerDbContext db,
        string sql,
        Action<NpgsqlParameterCollection>? bind,
        Func<NpgsqlDataReader, T> map,
        CancellationToken ct)
    {
        await using var scope = await PgScope.OpenAsync(db, ct);
        await using var command = scope.Command(sql);
        bind?.Invoke(command.Parameters);
        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    /// <summary>Executes a query and returns the first row, or null when there is none.</summary>
    public static async Task<T?> QueryFirstAsync<T>(
        LearnerDbContext db,
        string sql,
        Action<NpgsqlParameterCollection>? bind,
        Func<NpgsqlDataReader, T> map,
        CancellationToken ct)
        where T : class
    {
        await using var scope = await PgScope.OpenAsync(db, ct);
        await using var command = scope.Command(sql);
        bind?.Invoke(command.Parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? map(reader) : default;
    }

    /// <summary>Executes a scalar query.</summary>
    public static async Task<object?> ScalarAsync(
        LearnerDbContext db,
        string sql,
        Action<NpgsqlParameterCollection>? bind,
        CancellationToken ct)
    {
        await using var scope = await PgScope.OpenAsync(db, ct);
        await using var command = scope.Command(sql);
        bind?.Invoke(command.Parameters);
        var value = await command.ExecuteScalarAsync(ct);
        return value is DBNull ? null : value;
    }

    // ── reader helpers (by column name, nullable aware) ──────────────────────

    public static string Str(NpgsqlDataReader r, string name) => r.GetString(r.GetOrdinal(name));

    public static string? StrN(NpgsqlDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }

    public static int Int(NpgsqlDataReader r, string name) => r.GetInt32(r.GetOrdinal(name));

    public static int? IntN(NpgsqlDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? null : r.GetInt32(ordinal);
    }

    public static long Long(NpgsqlDataReader r, string name) => r.GetInt64(r.GetOrdinal(name));

    public static long? LongN(NpgsqlDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? null : r.GetInt64(ordinal);
    }

    public static bool Bool(NpgsqlDataReader r, string name) => r.GetBoolean(r.GetOrdinal(name));

    public static bool? BoolN(NpgsqlDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? null : r.GetBoolean(ordinal);
    }

    public static DateTimeOffset Ts(NpgsqlDataReader r, string name)
        => r.GetFieldValue<DateTimeOffset>(r.GetOrdinal(name));

    public static DateTimeOffset? TsN(NpgsqlDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? null : r.GetFieldValue<DateTimeOffset>(ordinal);
    }

    /// <summary>Reads a smallint column (Weight, Priority) as an int.</summary>
    public static int Small(NpgsqlDataReader r, string name) => r.GetInt16(r.GetOrdinal(name));
}
