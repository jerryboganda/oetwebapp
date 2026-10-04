using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Speaking grading calibration (owner spec 4 Oct 2026) — which grader produced a score. Adds the
    /// nullable <c>GraderVersion</c> (varchar 128) to <c>SpeakingAiAssessments</c>:
    /// <c>{prompt template}|{raw→reported mapping version}|{audio stage version}</c>. A score is labelled
    /// "provisional" until that version has passed calibration, so existing rows (NULL) stay provisional.
    /// Additive and idempotent; the blue/green slots overlap and the old slot neither reads nor writes it.
    ///
    /// HAND-AUTHORED (repo convention): inline <c>[Migration]</c>/<c>[DbContext]</c>, no Designer file;
    /// the matching property is added to <c>LearnerDbContextModelSnapshot</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270107090000_AddSpeakingAiAssessmentGraderVersion")]
    public partial class AddSpeakingAiAssessmentGraderVersion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"SpeakingAiAssessments\" ADD COLUMN IF NOT EXISTS \"GraderVersion\" character varying(128);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"SpeakingAiAssessments\" DROP COLUMN IF EXISTS \"GraderVersion\";");
        }
    }
}
