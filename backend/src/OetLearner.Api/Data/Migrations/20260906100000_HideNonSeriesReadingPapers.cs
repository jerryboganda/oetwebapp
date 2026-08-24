using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Master Catalogue §5 candidate content cleanup:
/// 1. Adds the explicit CandidateVisible publish flag (candidate surfaces and
///    start routes must filter on visibility, not just frontend omission).
/// 2. Hides the test-only Reading "Other papers" series: every published
///    Reading paper that does not belong to one of the five official book
///    series (Anna Hartford, Atlas Practice Series, Jayden Book, Nova
///    Practice Series, VERY DIFFICULT) becomes candidate-invisible. It stays
///    published in admin for development history but cannot be started
///    through any route. Listening test/demo/staging rows are handled by the
///    same flag (admins flip it off); no listening data change is required
///    today because those papers are already unpublished.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260906100000_HideNonSeriesReadingPapers")]
public partial class HideNonSeriesReadingPapers : Migration
{
    public override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE "ContentPapers" ADD COLUMN IF NOT EXISTS "CandidateVisible" boolean NOT NULL DEFAULT true;""");

        // Haystack mirrors lib/reading-exam-categories.ts paperHaystack():
        // lower(tags + slug + title) with underscores folded to dashes. The
        // expression is computed in a sub-select because Postgres does not
        // allow referencing a SELECT alias inside the same statement's WHERE.
        migrationBuilder.Sql("""
WITH flagged AS (
    SELECT "Id"
    FROM (
        SELECT "Id",
               lower(replace(concat_ws(' ', "TagsCsv", "Slug", "Title"), '_', '-')) AS haystack
        FROM "ContentPapers"
        WHERE "SubtestCode" = 'reading'
          AND "Status" = 4
          AND "CandidateVisible"
    ) papers
    WHERE position('anna-hartford' in haystack) = 0
      AND position('anna hartford' in haystack) = 0
      AND position('atlas-practice-series' in haystack) = 0
      AND position('atlas practice series' in haystack) = 0
      AND position('atlas-practice' in haystack) = 0
      AND position('jayden-book' in haystack) = 0
      AND position('jayden book' in haystack) = 0
      AND position('nova-practice-series' in haystack) = 0
      AND position('nova practice series' in haystack) = 0
      AND position('nova-practice' in haystack) = 0
      AND position('very-difficult-reading-exams' in haystack) = 0
      AND position('very difficult reading exams' in haystack) = 0
      AND position('very-difficult' in haystack) = 0
)
UPDATE "ContentPapers" AS p
SET "CandidateVisible" = false,
    "UpdatedAt" = now()
FROM flagged
WHERE p."Id" = flagged."Id";
""");
    }

    public override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE "ContentPapers" DROP COLUMN IF EXISTS "CandidateVisible";""");
    }
}
