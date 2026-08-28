using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OetLearner.Api.Data.Migrations;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// W1 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01).
///
/// Real-PostgreSQL-gated half of <c>AiControlPlaneSchemaTests</c> (split out
/// only to keep both files under the repo's per-file line cap — see that
/// class for the no-database enum/model/migration-shape coverage). Applies
/// <see cref="ExtendAiUsageRecordProvenance"/>'s SQL/DDL against an isolated
/// per-test schema and asserts:
/// <list type="bullet">
///   <item>the partition-safe unique index is created for a plain table;</item>
///   <item>it is skipped (not created, no error) for a partitioned parent and
///   for a missing table;</item>
///   <item>both the index guard and the historic backfill are rerunnable —
///   idempotent across repeated executions;</item>
///   <item>the resulting partial unique index actually enforces uniqueness.</item>
/// </list>
/// <see cref="PostgreSqlFactAttribute"/> skips (never fails) this whole class
/// when <c>OET_TEST_POSTGRES_CONNECTION</c> is unset.
/// </summary>
public sealed class AiControlPlaneSchemaPostgreSqlTests
{
    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    [PostgreSqlFact]
    public async Task ConditionalUniqueIndex_IsCreatedAndRerunnableWhenAiUsageRecordsIsAPlainTable()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(
            """
            CREATE TABLE "AiUsageRecords" (
                "Id" text PRIMARY KEY,
                "ProviderId" text NULL,
                "PricingVersion" text NULL,
                "ProviderInvoked" boolean NULL,
                "OperationId" text NULL,
                "AttemptNumber" integer NULL
            );
            INSERT INTO "AiUsageRecords" ("Id", "ProviderId") VALUES
                ('with-provider', 'provider-a'),
                ('without-provider', NULL);
            """);

        var indexSql = ApplyMigrationUp<ExtendAiUsageRecordProvenance>()
            .OfType<SqlOperation>()
            .Last()
            .Sql;

        // Rerunnable/idempotent: executing the guarded index-creation block
        // twice must not fail or duplicate the index.
        await database.ExecuteAsync(indexSql);
        await database.ExecuteAsync(indexSql);

        await using var command = database.Command(
            """
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = current_schema()
              AND tablename = 'AiUsageRecords'
              AND indexname = 'UX_AiUsageRecords_Operation_Attempt';
            """);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Contains("UNIQUE", reader.GetString(1), StringComparison.Ordinal);
        Assert.False(await reader.ReadAsync());
    }

    [PostgreSqlFact]
    public async Task ConditionalUniqueIndex_EnforcesUniquenessOnOperationAndAttemptNumber()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(
            """
            CREATE TABLE "AiUsageRecords" (
                "Id" text PRIMARY KEY,
                "ProviderId" text NULL,
                "PricingVersion" text NULL,
                "ProviderInvoked" boolean NULL,
                "OperationId" text NULL,
                "AttemptNumber" integer NULL
            );
            """);
        var indexSql = ApplyMigrationUp<ExtendAiUsageRecordProvenance>().OfType<SqlOperation>().Last().Sql;
        await database.ExecuteAsync(indexSql);

        await database.ExecuteAsync(
            """
            INSERT INTO "AiUsageRecords" ("Id", "OperationId", "AttemptNumber")
            VALUES ('first', 'op-1', 1);
            """);

        var duplicate = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.ExecuteAsync(
            """
            INSERT INTO "AiUsageRecords" ("Id", "OperationId", "AttemptNumber")
            VALUES ('second', 'op-1', 1);
            """));
        Assert.Equal("23505", duplicate.SqlState);

        // A second, unrelated attempt row and any number of NULL-keyed rows
        // (the partial-index predicate) must still be allowed.
        await database.ExecuteAsync(
            """
            INSERT INTO "AiUsageRecords" ("Id", "OperationId", "AttemptNumber") VALUES ('third', 'op-1', 2);
            INSERT INTO "AiUsageRecords" ("Id") VALUES ('no-operation-a');
            INSERT INTO "AiUsageRecords" ("Id") VALUES ('no-operation-b');
            """);
    }

    [PostgreSqlFact]
    public async Task ConditionalUniqueIndex_IsSkippedAndRerunnableWhenAiUsageRecordsIsPartitioned()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(
            """
            CREATE TABLE "AiUsageRecords" (
                "Id" text NOT NULL,
                "ProviderId" text NULL,
                "PricingVersion" text NULL,
                "ProviderInvoked" boolean NULL,
                "OperationId" text NULL,
                "AttemptNumber" integer NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                PRIMARY KEY ("Id", "CreatedAt")
            ) PARTITION BY RANGE ("CreatedAt");
            CREATE TABLE "AiUsageRecords_default" PARTITION OF "AiUsageRecords" DEFAULT;
            """);

        var indexSql = ApplyMigrationUp<ExtendAiUsageRecordProvenance>().OfType<SqlOperation>().Last().Sql;

        // Rerunnable/idempotent for the partitioned shape too: the guard
        // must skip cleanly both times, never raising and never creating the
        // index (which would require the partition key to be part of it).
        await database.ExecuteAsync(indexSql);
        await database.ExecuteAsync(indexSql);

        await using var command = database.Command(
            """
            SELECT count(*)
            FROM pg_indexes
            WHERE schemaname = current_schema()
              AND indexname = 'UX_AiUsageRecords_Operation_Attempt';
            """);

        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [PostgreSqlFact]
    public async Task ConditionalUniqueIndex_IsSkippedAndRerunnableWhenAiUsageRecordsIsMissing()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var indexSql = ApplyMigrationUp<ExtendAiUsageRecordProvenance>().OfType<SqlOperation>().Last().Sql;

        // No AiUsageRecords table at all in this schema — the guard must
        // still no-op cleanly (never throw) rather than assume the table
        // exists.
        await database.ExecuteAsync(indexSql);
        await database.ExecuteAsync(indexSql);
    }

    [PostgreSqlFact]
    public async Task Backfill_IsIdempotentAcrossRepeatedRuns()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(
            """
            CREATE TABLE "AiUsageRecords" (
                "Id" text PRIMARY KEY,
                "ProviderId" text NULL,
                "PricingVersion" text NULL,
                "ProviderInvoked" boolean NULL
            );
            INSERT INTO "AiUsageRecords" ("Id", "ProviderId", "PricingVersion", "ProviderInvoked") VALUES
                ('already-priced', 'provider-a', 'current-2026-11', true),
                ('legacy-with-provider', 'provider-b', NULL, NULL),
                ('legacy-without-provider', NULL, NULL, NULL);
            """);

        var backfillStatements = ApplyMigrationUp<ExtendAiUsageRecordProvenance>()
            .OfType<SqlOperation>()
            .Take(2)
            .Select(o => o.Sql)
            .ToList();

        foreach (var sql in backfillStatements)
            await database.ExecuteAsync(sql);
        // Re-run: must be a no-op, not re-stamp already-recalculated rows.
        foreach (var sql in backfillStatements)
            await database.ExecuteAsync(sql);

        await using var command = database.Command(
            """
            SELECT "Id", "PricingVersion", "ProviderInvoked"
            FROM "AiUsageRecords"
            ORDER BY "Id";
            """);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new Dictionary<string, (string PricingVersion, bool ProviderInvoked)>();
        while (await reader.ReadAsync())
            rows.Add(reader.GetString(0), (reader.GetString(1), reader.GetBoolean(2)));

        Assert.Equal("current-2026-11", rows["already-priced"].PricingVersion);
        Assert.True(rows["already-priced"].ProviderInvoked);
        Assert.Equal("legacy-pre-remediation", rows["legacy-with-provider"].PricingVersion);
        Assert.True(rows["legacy-with-provider"].ProviderInvoked);
        Assert.Equal("legacy-pre-remediation", rows["legacy-without-provider"].PricingVersion);
        Assert.False(rows["legacy-without-provider"].ProviderInvoked);
    }

    private static IReadOnlyList<MigrationOperation> ApplyMigrationUp<TMigration>()
        where TMigration : Migration, new()
    {
        var builder = new MigrationBuilder(NpgsqlProvider);
        var method = typeof(TMigration).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{typeof(TMigration).Name}.Up was not found.");
        method.Invoke(new TMigration(), [builder]);
        return builder.Operations;
    }
}
