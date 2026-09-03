using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261216090000_ReclassifyWritingResponseLetterType")]
    public partial class ReclassifyWritingResponseLetterType : Migration
    {
        // Writing catalogue taxonomy revision (2026-09):
        // - Response (LT-RP) is retired as a valid Writing catalogue letter type.
        // - "Medicine - Ms Isabel Garcia" is explicitly corrected LT-RP -> LT-DG.
        // - Every other remaining Response-classified row falls back to Other
        //   Letters (LT-OT) per the uncertain-case policy: never force an
        //   unclear case into an incorrect known category.
        //
        // Only WritingScenarios.LetterType is touched. Profession, title, task
        // content, case-note sentences, model answers (WritingTaskModelAnswers,
        // keyed by ScenarioId), candidate submissions/history, and all other
        // columns/relations are preserved.
        //
        // Idempotent: both statements only match rows still carrying a
        // Response-like letter type, so reruns affect 0 rows. Reversal is
        // intentionally not automated (the retired code must not be
        // reintroduced); restore from backup if a row was misclassified.

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Explicit business correction: Medicine - Ms Isabel Garcia.
            migrationBuilder.Sql(@"
UPDATE ""WritingScenarios""
SET ""LetterType"" = 'LT-DG'
WHERE UPPER(""LetterType"") IN ('LT-RP', 'RESPONSE', 'UPDATE')
  AND LOWER(""Profession"") = 'medicine'
  AND ""Title"" ILIKE '%isabel%garcia%';");

            // 2. Remaining Response-classified rows -> Other Letters fallback.
            migrationBuilder.Sql(@"
UPDATE ""WritingScenarios""
SET ""LetterType"" = 'LT-OT'
WHERE UPPER(""LetterType"") IN ('LT-RP', 'RESPONSE', 'UPDATE');");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No destructive down needed for data alignment.
        }
    }
}
