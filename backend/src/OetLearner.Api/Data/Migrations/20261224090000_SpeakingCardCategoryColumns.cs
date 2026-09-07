using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// FINAL 2026-09-06 — candidate-visible Speaking card taxonomy columns on
/// <c>RolePlayCards</c>: <c>PrimaryCategory</c> (the nine brief categories,
/// main catalogue filter, may appear as a learner chip), optional secondary
/// behavioural tags (<c>SecondaryTagsJson</c>), and <c>CategoryNeedsReview</c>
/// for low-confidence Other Cards. Existing rows default to Other Cards +
/// review flag; explicit per-card backfill ships separately.
///
/// <para>Hand-written (columns only): the model snapshot was updated by hand
/// to match, so future scaffolds stay clean for these columns.</para>
///
/// <para>⚠ <b>Postgres-only SQL.</b> The test suite uses SQLite via
/// <c>EnsureCreatedAsync()</c> and builds straight from the model,
/// bypassing migrations entirely.</para>
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20261224090000_SpeakingCardCategoryColumns")]
public partial class SpeakingCardCategoryColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
ALTER TABLE "RolePlayCards" ADD COLUMN IF NOT EXISTS "PrimaryCategory" character varying(64) NOT NULL DEFAULT 'Other Cards';
""");
        migrationBuilder.Sql("""
ALTER TABLE "RolePlayCards" ADD COLUMN IF NOT EXISTS "SecondaryTagsJson" text NOT NULL DEFAULT '[]';
""");
        migrationBuilder.Sql("""
ALTER TABLE "RolePlayCards" ADD COLUMN IF NOT EXISTS "CategoryNeedsReview" boolean NOT NULL DEFAULT FALSE;
""");

        // Backfill the twelve system-seeded cards per the deterministic §8B
        // priority rules (the seeder skips entirely when seed rows already
        // exist, so live databases need this update). Every other existing
        // row keeps Other Cards and is flagged for human review rather than
        // forced into a wrong category.
        Backfill(migrationBuilder, "rpc-seed-nursing-01", "Already Known Patient", "[]");
        Backfill(migrationBuilder, "rpc-seed-nursing-02", "Second Visit / Follow-up", """["Reluctant"]""");
        Backfill(migrationBuilder, "rpc-seed-nursing-03", "Second Visit / Follow-up", "[]");
        Backfill(migrationBuilder, "rpc-seed-nursing-04", "Already Known Patient", "[]");
        Backfill(migrationBuilder, "rpc-seed-nursing-05", "Reluctant Patient", "[]");
        Backfill(migrationBuilder, "rpc-seed-nursing-06", "Already Known Patient", """["Breaking Bad News"]""");
        Backfill(migrationBuilder, "rpc-seed-medicine-01", "Second Visit / Follow-up", "[]");
        Backfill(migrationBuilder, "rpc-seed-medicine-02", "Already Known Patient", "[]");
        Backfill(migrationBuilder, "rpc-seed-medicine-03", "Second Visit / Follow-up", "[]");
        Backfill(migrationBuilder, "rpc-seed-medicine-04", "Reluctant Patient", "[]");
        Backfill(migrationBuilder, "rpc-seed-medicine-05", "Second Visit / Follow-up", "[]");
        Backfill(migrationBuilder, "rpc-seed-medicine-06", "Second Visit / Follow-up", """["Breaking Bad News"]""");

        migrationBuilder.Sql("""
UPDATE "RolePlayCards"
SET "CategoryNeedsReview" = TRUE
WHERE "PrimaryCategory" = 'Other Cards';
""");
    }

    private static void Backfill(MigrationBuilder migrationBuilder, string cardId, string primaryCategory, string secondaryTagsJson)
    {
        var tags = secondaryTagsJson.Replace("'", "''", StringComparison.Ordinal);
        migrationBuilder.Sql($"""
UPDATE "RolePlayCards"
SET "PrimaryCategory" = '{primaryCategory}',
    "SecondaryTagsJson" = '{tags}',
    "CategoryNeedsReview" = FALSE
WHERE "Id" = '{cardId}';
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE "RolePlayCards" DROP COLUMN IF EXISTS "CategoryNeedsReview";""");
        migrationBuilder.Sql("""ALTER TABLE "RolePlayCards" DROP COLUMN IF EXISTS "SecondaryTagsJson";""");
        migrationBuilder.Sql("""ALTER TABLE "RolePlayCards" DROP COLUMN IF EXISTS "PrimaryCategory";""");
    }
}
