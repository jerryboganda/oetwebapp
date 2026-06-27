using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>WritingScenario.AnswerSheetPdfMediaAssetId</c> — an optional reference to the
    /// uploaded answer-sheet / model-answer PDF revealed to the learner on the results page
    /// (post-submission only) so they can tally their letter against the official answer.
    /// Mirrors <c>StimulusPdfMediaAssetId</c>. Hand-written idempotent SQL (house style of the
    /// sibling writing/reading migrations) so it applies cleanly on fresh and existing
    /// databases and is safe to re-run.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260628120000_AddWritingScenarioAnswerSheetPdf")]
    public partial class AddWritingScenarioAnswerSheetPdf : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""WritingScenarios"" ADD COLUMN IF NOT EXISTS ""AnswerSheetPdfMediaAssetId"" character varying(64) NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""WritingScenarios"" DROP COLUMN IF EXISTS ""AnswerSheetPdfMediaAssetId"";");
        }
    }
}
