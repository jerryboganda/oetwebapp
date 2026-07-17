using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations;

/// <summary>
/// Adds the two measured ContentItems ordering indexes without taking a
/// blocking table lock in PostgreSQL. Other providers intentionally receive no
/// operations; their small local datasets use the runtime model indexes.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260728091000_AddContentItemBrowseIndexes")]
public partial class AddContentItemBrowseIndexes : Migration
{
    private const string BrowseIndex = "IX_ContentItems_Published_Subtest_Title";
    private const string ProvenanceIndex = "IX_ContentItems_Published_Provenance_Created";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!IsPostgres(migrationBuilder))
            return;

        QueueConcurrentIndex(
            migrationBuilder,
            BrowseIndex,
            @"""SubtestCode"", ""Title""",
            @"""Status"" = 4 AND ""FreshnessConfidence"" <> 'superseded'");

        QueueConcurrentIndex(
            migrationBuilder,
            ProvenanceIndex,
            @"""SourceProvenance"", ""CreatedAt"" DESC",
            @"""Status"" = 4");

        // Deliberately measurement-gated: the current PostgreSQL search is an
        // OR of ILIKE predicates over the raw Title and DetailJson columns.
        // lower(...) expression indexes cannot serve that SQL shape without
        // changing it, and indexing only Title would leave the OR branch unable
        // to use a complete bitmap-or plan. Do not add pg_trgm or a title-only
        // index until an EXPLAIN-backed change can preserve exact substring and
        // escape semantics for both columns.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (!IsPostgres(migrationBuilder))
            return;

        DropConcurrentIndex(migrationBuilder, ProvenanceIndex);
        DropConcurrentIndex(migrationBuilder, $"{ProvenanceIndex}_invalid");
        DropConcurrentIndex(migrationBuilder, BrowseIndex);
        DropConcurrentIndex(migrationBuilder, $"{BrowseIndex}_invalid");
    }

    private static bool IsPostgres(MigrationBuilder migrationBuilder)
        => migrationBuilder.ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);

    private static void QueueConcurrentIndex(
        MigrationBuilder migrationBuilder,
        string indexName,
        string columns,
        string predicate)
    {
        var invalidIndexName = $"{indexName}_invalid";

        // A failed CREATE INDEX CONCURRENTLY leaves an invalid catalog entry.
        // Clear a stale recovery name, rename only an invalid target, then drop
        // that renamed entry concurrently before attempting the build again.
        DropConcurrentIndex(migrationBuilder, invalidIndexName);
        migrationBuilder.Sql(
            $$"""
            DO $migration$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM pg_catalog.pg_class AS index_class
                    JOIN pg_catalog.pg_index AS index_state
                      ON index_state.indexrelid = index_class.oid
                    JOIN pg_catalog.pg_namespace AS index_namespace
                      ON index_namespace.oid = index_class.relnamespace
                    WHERE index_namespace.nspname = current_schema()
                      AND index_class.relname = '{{indexName}}'
                      AND NOT index_state.indisvalid
                ) THEN
                    EXECUTE format(
                        'ALTER INDEX %I.%I RENAME TO %I',
                        current_schema(),
                        '{{indexName}}',
                        '{{invalidIndexName}}');
                END IF;
            END
            $migration$;
            """,
            suppressTransaction: true);
        DropConcurrentIndex(migrationBuilder, invalidIndexName);

        migrationBuilder.Sql(
            $"""
            CREATE INDEX CONCURRENTLY IF NOT EXISTS "{indexName}"
            ON "ContentItems" ({columns})
            WHERE {predicate};
            """,
            suppressTransaction: true);
    }

    private static void DropConcurrentIndex(MigrationBuilder migrationBuilder, string indexName)
    {
        migrationBuilder.Sql(
            $"""DROP INDEX CONCURRENTLY IF EXISTS "{indexName}";""",
            suppressTransaction: true);
    }
}
