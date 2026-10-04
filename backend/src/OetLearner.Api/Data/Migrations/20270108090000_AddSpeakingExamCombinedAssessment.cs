using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Speaking Full Mock (owner spec 4 Oct 2026) — one combined judgement for the two cards. Adds the nullable
    /// <c>CombinedAssessmentJson</c> (text) to <c>SpeakingExamSessions</c>: the single nine-criterion result for the
    /// whole test, stored in the shape of a card's assessment row. Existing rows (NULL) keep their averaged
    /// snapshot. Additive and idempotent; the blue/green slots overlap and the old slot neither reads nor writes it.
    ///
    /// HAND-AUTHORED (repo convention): inline <c>[Migration]</c>/<c>[DbContext]</c>, no Designer file;
    /// the matching property is added to <c>LearnerDbContextModelSnapshot</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270108090000_AddSpeakingExamCombinedAssessment")]
    public partial class AddSpeakingExamCombinedAssessment : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"SpeakingExamSessions\" ADD COLUMN IF NOT EXISTS \"CombinedAssessmentJson\" text;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"SpeakingExamSessions\" DROP COLUMN IF EXISTS \"CombinedAssessmentJson\";");
        }
    }
}
