using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using OetWithDrHesham.Api.Data.Migrations;
using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Tests;

public sealed class PostgreSqlPerformanceProviderTests
{
    private const string ConnectionVariable = "OET_TEST_POSTGRES_CONNECTION";
    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    [PostgreSqlFact]
    public async Task DateAggregate_GroupsTimestampWithTimeZoneByUtcCalendarDate()
    {
        await using var database = await PostgreSqlTestSchema.CreateAsync();
        await database.ExecuteAsync(
            """
            CREATE TABLE "AnalyticsEvents" (
                "Id" text PRIMARY KEY,
                "OccurredAt" timestamp with time zone NOT NULL
            );
            INSERT INTO "AnalyticsEvents" ("Id", "OccurredAt") VALUES
                ('before-midnight', '2026-07-13T23:59:59Z'),
                ('after-midnight', '2026-07-14T00:00:01Z'),
                ('same-utc-day', '2026-07-14T18:30:00+05:00');
            """);

        await using var command = database.Command(
            """
            SELECT ("OccurredAt" AT TIME ZONE 'UTC')::date AS event_date, count(*)
            FROM "AnalyticsEvents"
            GROUP BY event_date
            ORDER BY event_date;
            """);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(new DateOnly(2026, 7, 13), reader.GetFieldValue<DateOnly>(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.True(await reader.ReadAsync());
        Assert.Equal(new DateOnly(2026, 7, 14), reader.GetFieldValue<DateOnly>(0));
        Assert.Equal(2L, reader.GetInt64(1));
        Assert.False(await reader.ReadAsync());
    }

    [PostgreSqlFact]
    public async Task ILikeSearch_IsCaseInsensitiveAndEscapesLiteralWildcards()
    {
        await using var database = await PostgreSqlTestSchema.CreateAsync();
        await database.ExecuteAsync(
            """
            CREATE TABLE "ContentSearch" (
                "Id" integer PRIMARY KEY,
                "Title" text NOT NULL
            );
            INSERT INTO "ContentSearch" ("Id", "Title") VALUES
                (1, 'Patient 100% guide'),
                (2, 'PATIENT 100% GUIDE'),
                (3, 'Patient 100 percent guide'),
                (4, 'Patient 1000 guide');
            """);

        await using var command = database.Command(
            """
            SELECT "Id"
            FROM "ContentSearch"
            WHERE "Title" ILIKE @pattern ESCAPE '\'
            ORDER BY "Id";
            """);
        command.Parameters.AddWithValue("pattern", "%patient 100\\%%");
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<int>();
        while (await reader.ReadAsync())
            ids.Add(reader.GetInt32(0));

        Assert.Equal([1, 2], ids);
    }

    [PostgreSqlFact]
    public async Task ContentIndexes_ExecuteTwiceAndRemainValidAndReady()
    {
        await using var database = await PostgreSqlTestSchema.CreateAsync();
        await database.ExecuteAsync(
            """
            CREATE TABLE "ContentItems" (
                "Id" text PRIMARY KEY,
                "Status" integer NOT NULL,
                "FreshnessConfidence" text NOT NULL,
                "SubtestCode" text NOT NULL,
                "Title" text NOT NULL,
                "SourceProvenance" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            """);

        var operations = ApplyMigration<AddContentItemBrowseIndexes>();
        await ExecuteOperationsAsync(database, operations);
        await ExecuteOperationsAsync(database, operations);

        await using var command = database.Command(
            """
            SELECT index_class.relname,
                   index_state.indisvalid,
                   index_state.indisready,
                   pg_get_indexdef(index_state.indexrelid),
                   pg_get_expr(index_state.indpred, index_state.indrelid)
            FROM pg_catalog.pg_index AS index_state
            JOIN pg_catalog.pg_class AS index_class
              ON index_class.oid = index_state.indexrelid
            JOIN pg_catalog.pg_namespace AS index_namespace
              ON index_namespace.oid = index_class.relnamespace
            WHERE index_namespace.nspname = current_schema()
              AND index_class.relname IN (
                  'IX_ContentItems_Published_Subtest_Title',
                  'IX_ContentItems_Published_Provenance_Created')
            ORDER BY index_class.relname;
            """);
        await using var reader = await command.ExecuteReaderAsync();
        var indexes = new Dictionary<string, (bool Valid, bool Ready, string Definition, string Predicate)>();
        while (await reader.ReadAsync())
        {
            indexes.Add(
                reader.GetString(0),
                (reader.GetBoolean(1), reader.GetBoolean(2), reader.GetString(3), reader.GetString(4)));
        }

        Assert.Equal(2, indexes.Count);
        Assert.All(indexes.Values, index => Assert.True(index.Valid && index.Ready));

        var browse = indexes["IX_ContentItems_Published_Subtest_Title"];
        Assert.Contains("\"SubtestCode\", \"Title\"", browse.Definition, StringComparison.Ordinal);
        Assert.Contains("\"Status\" = 4", browse.Predicate, StringComparison.Ordinal);
        Assert.Contains("\"FreshnessConfidence\" <> 'superseded'", browse.Predicate, StringComparison.Ordinal);

        var provenance = indexes["IX_ContentItems_Published_Provenance_Created"];
        Assert.Contains("\"SourceProvenance\", \"CreatedAt\" DESC", provenance.Definition, StringComparison.Ordinal);
        Assert.Contains("\"Status\" = 4", provenance.Predicate, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task JobClaim_SkipsRowsLockedByAnotherWorker()
    {
        await using var database = await PostgreSqlTestSchema.CreateAsync();
        await database.ExecuteAsync(
            """
            CREATE TABLE "BackgroundJobs" (
                "Id" text PRIMARY KEY,
                "Type" integer NOT NULL,
                "State" integer NOT NULL,
                "AttemptId" text NULL,
                "ResourceId" text NULL,
                "PayloadJson" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "AvailableAt" timestamp with time zone NOT NULL,
                "LastTransitionAt" timestamp with time zone NOT NULL,
                "StatusReasonCode" text NOT NULL,
                "StatusMessage" text NOT NULL,
                "Retryable" boolean NOT NULL,
                "RetryCount" integer NOT NULL,
                "RetryAfterMs" integer NULL
            );
            INSERT INTO "BackgroundJobs" (
                "Id", "Type", "State", "PayloadJson", "CreatedAt", "AvailableAt",
                "LastTransitionAt", "StatusReasonCode", "StatusMessage", "Retryable", "RetryCount")
            VALUES
                ('first', 0, 1, '{}', '2026-07-14T00:00:00Z', '2026-07-14T00:00:00Z',
                 '2026-07-14T00:00:00Z', 'pending', 'Queued', true, 0),
                ('second', 0, 1, '{}', '2026-07-14T00:00:01Z', '2026-07-14T00:00:01Z',
                 '2026-07-14T00:00:01Z', 'pending', 'Queued', true, 0);
            """);

        await using var locker = await database.OpenSiblingAsync();
        await using var lockTransaction = await locker.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand(
            "SELECT \"Id\" FROM \"BackgroundJobs\" WHERE \"Id\" = 'first' FOR UPDATE;",
            locker,
            lockTransaction))
        {
            Assert.Equal("first", await lockCommand.ExecuteScalarAsync());
        }

        await using var claim = database.Command(BackgroundJobProcessor.PostgresClaimQueuedJobsSql);
        claim.Parameters.AddWithValue("queuedState", 1);
        claim.Parameters.AddWithValue("processingState", 2);
        claim.Parameters.AddWithValue("statusReasonCode", "claimed");
        claim.Parameters.AddWithValue("statusMessage", "Processing");
        claim.Parameters.AddWithValue("now", new DateTimeOffset(2026, 7, 14, 1, 0, 0, TimeSpan.Zero));
        claim.Parameters.AddWithValue("batchSize", 1);
        await using var reader = await claim.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("second", reader.GetString(0));
        Assert.False(await reader.ReadAsync());
        await lockTransaction.RollbackAsync();
    }

    private static IReadOnlyList<SqlOperation> ApplyMigration<TMigration>()
        where TMigration : Migration, new()
    {
        var builder = new MigrationBuilder(NpgsqlProvider);
        var method = typeof(TMigration).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{typeof(TMigration).Name}.Up was not found.");
        method.Invoke(new TMigration(), [builder]);
        return builder.Operations.OfType<SqlOperation>().ToList();
    }

    private static async Task ExecuteOperationsAsync(
        PostgreSqlTestSchema database,
        IEnumerable<SqlOperation> operations)
    {
        foreach (var operation in operations)
        {
            Assert.True(operation.SuppressTransaction);
            await database.ExecuteAsync(operation.Sql);
        }
    }

    private sealed class PostgreSqlTestSchema : IAsyncDisposable
    {
        private readonly string _baseConnectionString;

        private PostgreSqlTestSchema(string baseConnectionString, string schema, NpgsqlConnection connection)
        {
            _baseConnectionString = baseConnectionString;
            Schema = schema;
            Connection = connection;
        }

        public string Schema { get; }
        public NpgsqlConnection Connection { get; }

        public static async Task<PostgreSqlTestSchema> CreateAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionVariable)
                ?? throw new InvalidOperationException($"{ConnectionVariable} is required.");
            var schema = $"performance_{Guid.NewGuid():N}";
            var connection = new NpgsqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\";", connection))
                await create.ExecuteNonQueryAsync();
            await SetSearchPathAsync(connection, schema);
            return new PostgreSqlTestSchema(baseConnectionString, schema, connection);
        }

        public NpgsqlCommand Command(string sql) => new(sql, Connection);

        public async Task ExecuteAsync(string sql)
        {
            await using var command = Command(sql);
            await command.ExecuteNonQueryAsync();
        }

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

    private sealed class PostgreSqlFactAttribute : FactAttribute
    {
        public PostgreSqlFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
                Skip = $"Set {ConnectionVariable} to run PostgreSQL provider coverage.";
        }
    }
}
