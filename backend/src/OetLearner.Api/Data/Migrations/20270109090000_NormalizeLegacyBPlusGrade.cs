using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270109090000_NormalizeLegacyBPlusGrade")]
    public partial class NormalizeLegacyBPlusGrade : Migration
    {
        // Owner decision 2026-10-05: OET has no "B+" grade. Candidate-facing grades are only
        // A / B / C+ / C / D / E. Older rows (Writing target bands, buddy matches, Writing grade and
        // readiness labels, the legacy evaluation grade range) can still carry a "B+" label; it is
        // read as "B" (the grade the old B+ band sat inside). No schema change.
        //
        // Idempotent: every statement matches only rows still carrying the legacy label, so a rerun
        // affects 0 rows. Down is intentionally a no-op (the retired label must not be reintroduced).
        // Blue/green overlap: the service layer also coerces "B+" -> "B" on write.

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Plain varchar band labels.
            string[] bandColumns =
            [
                "LearnerWritingProfiles|TargetBand",
                "LearnerReadingProfiles|TargetBand",
                "LearnerListeningProfiles|TargetBand",
                "WritingBuddyPairs|MatchedAtBand",
                "WritingGrades|BandLabel",
                "WritingReadinessScores|PredictedBandLabel",
                "StudyPlanTemplates|TargetBand",
            ];
            foreach (var entry in bandColumns)
            {
                var parts = entry.Split('|');
                migrationBuilder.Sql(
                    $"UPDATE \"{parts[0]}\" SET \"{parts[1]}\" = 'B' WHERE \"{parts[1]}\" = 'B+';");
            }

            // Legacy evaluation grade range ("B+" and the demo-seed range "B-B+").
            migrationBuilder.Sql(@"
UPDATE ""Evaluations"" SET ""GradeRange"" = 'C+-B' WHERE ""GradeRange"" = 'B-B+';");
            migrationBuilder.Sql(@"
UPDATE ""Evaluations"" SET ""GradeRange"" = 'B' WHERE ""GradeRange"" = 'B+';");

            // Admin-only Writing calibration JSON (jsonb) carrying a bandLabel.
            migrationBuilder.Sql(@"
UPDATE ""WritingCalibrationLetters""
SET ""DrAhmedGradeJson"" = jsonb_set(""DrAhmedGradeJson"", '{bandLabel}', '""B""')
WHERE ""DrAhmedGradeJson""->>'bandLabel' = 'B+';");
            migrationBuilder.Sql(@"
UPDATE ""WritingCalibrationResults""
SET ""AiGradeJson"" = jsonb_set(""AiGradeJson"", '{bandLabel}', '""B""')
WHERE ""AiGradeJson""->>'bandLabel' = 'B+';");

            // Free-text overall goal: only an exact "B+" / "Grade B+"; any other narrative is left alone.
            migrationBuilder.Sql(@"
UPDATE ""Goals"" SET ""OverallGoal"" = 'B'
WHERE UPPER(BTRIM(""OverallGoal"")) IN ('B+', 'GRADE B+');");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data alignment only; the retired label is never restored.
        }
    }
}
