using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>WritingScenario.StimulusPdfMediaAssetId</c> — an optional reference to the
    /// uploaded stimulus PDF (the exam "question paper") shown to learners during the
    /// forced reading window and the writing view. Null = fall back to the case-notes
    /// text viewer. Independent of <c>CaseNotesMarkdown</c>, which the grading pipeline
    /// still reads. Hand-written idempotent SQL (matches the house style of the sibling
    /// reading migrations) so it applies cleanly on both fresh and existing databases and
    /// is safe to re-run.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260624120000_AddWritingScenarioStimulusPdf")]
    public partial class AddWritingScenarioStimulusPdf : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""WritingScenarios"" ADD COLUMN IF NOT EXISTS ""StimulusPdfMediaAssetId"" character varying(64) NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""WritingScenarios"" DROP COLUMN IF EXISTS ""StimulusPdfMediaAssetId"";");
        }
    }
}
