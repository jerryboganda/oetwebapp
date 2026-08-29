using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// W9 — <c>VocabularyWords.NormalizedWord</c> with unique index (Postgres
/// CONCURRENTLY), Reading Q&amp;A turns, and <c>reading.passage_qna.v1</c> policy seed.
/// Duplicate NormalizedWord rows are reported into
/// <c>_W9VocabularyNormalizedDuplicates</c>; merge is deferred to W11.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20261206090000_AddVocabularyNormalizedKey")]
public partial class AddVocabularyNormalizedKey : Migration
{
    private const string LookupIndex = "IX_VocabularyWords_NormalizedWord";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (IsPostgres(migrationBuilder))
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "VocabularyWords"
                    ADD COLUMN IF NOT EXISTS "NormalizedWord" character varying(128) NOT NULL DEFAULT '';

                UPDATE "VocabularyWords"
                    SET "NormalizedWord" = lower(btrim("Word"))
                    WHERE "NormalizedWord" = '' OR "NormalizedWord" IS NULL;

                CREATE TABLE IF NOT EXISTS "_W9VocabularyNormalizedDuplicates" (
                    "NormalizedWord" character varying(128) NOT NULL,
                    "DuplicateCount" integer NOT NULL,
                    "Ids" text NOT NULL,
                    "ReportedAt" timestamp with time zone NOT NULL
                );

                INSERT INTO "_W9VocabularyNormalizedDuplicates" ("NormalizedWord", "DuplicateCount", "Ids", "ReportedAt")
                SELECT "NormalizedWord", COUNT(*)::int, string_agg("Id"::text, ','), NOW()
                FROM "VocabularyWords"
                WHERE "NormalizedWord" <> ''
                GROUP BY "NormalizedWord"
                HAVING COUNT(*) > 1;
                """);

            QueueConcurrentLookupIndex(migrationBuilder);

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "ReadingQnaTurns" (
                    "Id" character varying(64) NOT NULL,
                    "SessionId" character varying(128) NOT NULL,
                    "ClientTurnId" character varying(64) NOT NULL,
                    "UserId" character varying(64) NOT NULL,
                    "AttemptId" character varying(64) NOT NULL,
                    "PassageId" character varying(64) NOT NULL,
                    "Message" text NOT NULL,
                    "Reply" text NOT NULL,
                    "AiOperationId" character varying(64),
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_ReadingQnaTurns" PRIMARY KEY ("Id")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_ReadingQnaTurns_Session_ClientTurn"
                    ON "ReadingQnaTurns" ("SessionId", "ClientTurnId");

                CREATE INDEX IF NOT EXISTS "IX_ReadingQnaTurns_User_Attempt_Passage"
                    ON "ReadingQnaTurns" ("UserId", "AttemptId", "PassageId");

                INSERT INTO "AiFeaturePolicies" (
                    "Id", "FeatureCode", "Module", "OperationClass", "IsActive", "PolicyVersion",
                    "RequiresGrounding", "CacheDimensions", "EffectiveFrom", "EffectiveTo",
                    "CreatedBy", "CreatedAt", "UpdatedAt"
                )
                SELECT
                    'aifp_reading_passage_qna_v1', 'reading.passage_qna.v1', 'reading', 1, true, 1,
                    true, NULL,
                    TIMESTAMPTZ '2020-01-01T00:00:00Z', NULL,
                    'system:w9-seed', TIMESTAMPTZ '2026-12-06T09:00:00Z', TIMESTAMPTZ '2026-12-06T09:00:00Z'
                WHERE NOT EXISTS (
                    SELECT 1 FROM "AiFeaturePolicies" existing
                    WHERE existing."FeatureCode" = 'reading.passage_qna.v1' AND existing."PolicyVersion" = 1
                );
                """);
            return;
        }

        migrationBuilder.AddColumn<string>(
            name: "NormalizedWord",
            table: "VocabularyWords",
            type: "TEXT",
            maxLength: 128,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateTable(
            name: "ReadingQnaTurns",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                SessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ClientTurnId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                AttemptId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                PassageId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Message = table.Column<string>(type: "TEXT", nullable: false),
                Reply = table.Column<string>(type: "TEXT", nullable: false),
                AiOperationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReadingQnaTurns", x => x.Id);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (IsPostgres(migrationBuilder))
        {
            DropConcurrentIndex(migrationBuilder, LookupIndex);
            DropConcurrentIndex(migrationBuilder, $"{LookupIndex}_invalid");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "ReadingQnaTurns";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "_W9VocabularyNormalizedDuplicates";""");
            migrationBuilder.Sql("""ALTER TABLE "VocabularyWords" DROP COLUMN IF EXISTS "NormalizedWord";""");
            migrationBuilder.Sql(
                """DELETE FROM "AiFeaturePolicies" WHERE "Id" = 'aifp_reading_passage_qna_v1';""");
            return;
        }

        migrationBuilder.DropTable(name: "ReadingQnaTurns");
        migrationBuilder.DropColumn(name: "NormalizedWord", table: "VocabularyWords");
    }

    private static bool IsPostgres(MigrationBuilder migrationBuilder)
        => migrationBuilder.ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);

    private static void QueueConcurrentLookupIndex(MigrationBuilder migrationBuilder)
    {
        var invalidIndexName = $"{LookupIndex}_invalid";
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
                      AND index_class.relname = '{{LookupIndex}}'
                      AND NOT index_state.indisvalid
                ) THEN
                    EXECUTE format(
                        'ALTER INDEX %I.%I RENAME TO %I',
                        current_schema(),
                        '{{LookupIndex}}',
                        '{{invalidIndexName}}');
                END IF;
            END
            $migration$;
            """,
            suppressTransaction: true);
        DropConcurrentIndex(migrationBuilder, invalidIndexName);

        // Unique promotion is W11, after duplicate merge. W9 still indexes
        // NormalizedWord for lookup and reports collisions.
        migrationBuilder.Sql(
            $"""
            CREATE INDEX CONCURRENTLY IF NOT EXISTS "{LookupIndex}"
            ON "VocabularyWords" ("NormalizedWord")
            WHERE "NormalizedWord" <> '';
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
