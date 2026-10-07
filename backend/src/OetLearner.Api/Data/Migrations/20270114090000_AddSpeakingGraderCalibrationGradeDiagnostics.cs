using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Speaking grader calibration (owner request 7 Oct 2026) — why a grade is what it is. One additive nullable column on
    /// <c>SpeakingGraderCalibrationGrades</c>: <c>DiagnosticsJson</c> (the mapping version, Claude's scores before the secondary
    /// review, what the reviewer changed and each card's audio verdict; numbers and codes only). Idempotent; the blue/green
    /// slots overlap and neither the old nor the new slot is affected by a column it never reads.
    ///
    /// HAND-AUTHORED (repo convention): inline <c>[Migration]</c>/<c>[DbContext]</c>, no Designer file;
    /// the matching property is in <c>LearnerDbContextModelSnapshot</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270114090000_AddSpeakingGraderCalibrationGradeDiagnostics")]
    public partial class AddSpeakingGraderCalibrationGradeDiagnostics : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"SpeakingGraderCalibrationGrades\" ADD COLUMN IF NOT EXISTS \"DiagnosticsJson\" text NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"SpeakingGraderCalibrationGrades\" DROP COLUMN IF EXISTS \"DiagnosticsJson\";");
        }
    }
}
