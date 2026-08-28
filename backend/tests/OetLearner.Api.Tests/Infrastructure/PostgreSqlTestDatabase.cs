using Npgsql;

namespace OetLearner.Api.Tests.Infrastructure;

/// <summary>
/// Shared PostgreSQL integration-test harness: creates a uniquely named
/// schema on the server pointed to by <c>OET_TEST_POSTGRES_CONNECTION</c>,
/// runs the test against it via <see cref="Command"/> / <see cref="ExecuteAsync"/>,
/// and drops the schema (cascading) on dispose.
///
/// <para>
/// Promoted out of <c>PostgreSqlPerformanceProviderTests</c> (the original
/// precedent) so every real-PostgreSQL-only test class (migration/schema
/// coverage, provider-specific SQL, partition-safety checks, …) shares one
/// harness instead of re-implementing schema isolation. Each test gets its
/// own schema — no shared state, no cross-test locking — via
/// <see cref="CreateAsync"/>. Use <see cref="OpenSiblingAsync"/> when a test
/// needs a second connection into the same schema (e.g. to hold a row lock
/// on one connection while asserting behaviour on another).
/// </para>
/// </summary>
public sealed class PostgreSqlTestDatabase : IAsyncDisposable
{
    /// <summary>Environment variable read by <see cref="PostgreSqlFactAttribute"/>
    /// and <see cref="CreateAsync"/>. Tests that need real PostgreSQL skip
    /// (never fail) when this is unset.</summary>
    public const string ConnectionVariable = "OET_TEST_POSTGRES_CONNECTION";

    private readonly string _baseConnectionString;

    private PostgreSqlTestDatabase(string baseConnectionString, string schema, NpgsqlConnection connection)
    {
        _baseConnectionString = baseConnectionString;
        Schema = schema;
        Connection = connection;
    }

    public string Schema { get; }

    public NpgsqlConnection Connection { get; }

    /// <summary>
    /// Opens a connection to <c>OET_TEST_POSTGRES_CONNECTION</c>, creates a
    /// fresh schema named <c>test_{guid}</c>, and points the returned
    /// connection's <c>search_path</c> at it.
    /// </summary>
    public static async Task<PostgreSqlTestDatabase> CreateAsync()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"{ConnectionVariable} is required.");
        var schema = $"test_{Guid.NewGuid():N}";
        var connection = new NpgsqlConnection(baseConnectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\";", connection))
            await create.ExecuteNonQueryAsync();
        await SetSearchPathAsync(connection, schema);
        return new PostgreSqlTestDatabase(baseConnectionString, schema, connection);
    }

    public NpgsqlCommand Command(string sql) => new(sql, Connection);

    /// <summary>
    /// Connection string that resolves unqualified names to this test's
    /// isolated schema, for callers that need their own connection pool (e.g.
    /// an EF Core <c>DbContext</c>) rather than a raw sibling connection.
    /// </summary>
    public string SchemaConnectionString
        => new NpgsqlConnectionStringBuilder(_baseConnectionString) { SearchPath = Schema }.ConnectionString;

    public async Task ExecuteAsync(string sql)
    {
        await using var command = Command(sql);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Opens a second, independent connection into the same test schema.
    /// Use for scenarios that need two concurrent sessions (row locking,
    /// advisory locks, concurrent-writer races, …) against the same data.
    /// Caller owns disposal.
    /// </summary>
    public async Task<NpgsqlConnection> OpenSiblingAsync()
    {
        var connection = new NpgsqlConnection(_baseConnectionString);
        await connection.OpenAsync();
        await SetSearchPathAsync(connection, Schema);
        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.CloseAsync();
        await using var cleanup = new NpgsqlConnection(_baseConnectionString);
        await cleanup.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE;", cleanup);
        await drop.ExecuteNonQueryAsync();
        await Connection.DisposeAsync();
    }

    private static async Task SetSearchPathAsync(NpgsqlConnection connection, string schema)
    {
        await using var command = new NpgsqlCommand($"SET search_path TO \"{schema}\";", connection);
        await command.ExecuteNonQueryAsync();
    }
}
