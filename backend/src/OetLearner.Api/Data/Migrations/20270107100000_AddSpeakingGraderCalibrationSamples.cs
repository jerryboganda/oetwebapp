using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Speaking grader calibration (owner spec 4 Oct 2026) — the expert side. One table of ids only:
    /// a finished AI card promoted for the expert to mark blind, with the expert's nine criterion
    /// scores and overall /500 once labelled. Additive and idempotent (the blue/green slots overlap
    /// and neither the old nor the new slot is affected by a table it never reads).
    ///
    /// HAND-AUTHORED (repo convention): inline <c>[Migration]</c>/<c>[DbContext]</c>, no Designer file;
    /// the matching entity is in <c>LearnerDbContextModelSnapshot</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270107100000_AddSpeakingGraderCalibrationSamples")]
    public partial class AddSpeakingGraderCalibrationSamples : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""SpeakingGraderCalibrationSamples"" (
    ""Id"" character varying(64) NOT NULL,
    ""ExcludedReason"" character varying(500) NOT NULL,
    ""ExpertNotes"" character varying(2000) NOT NULL,
    ""ExpertOverallScaled"" integer NULL,
    ""ExpertScoresJson"" text NULL,
    ""HasAudio"" boolean NOT NULL,
    ""LabelledAt"" timestamp with time zone NULL,
    ""LabelledById"" character varying(64) NULL,
    ""ProfessionId"" character varying(32) NOT NULL,
    ""PromotedAt"" timestamp with time zone NOT NULL,
    ""PromotedById"" character varying(64) NOT NULL,
    ""RolePlayCardId"" character varying(64) NOT NULL,
    ""SpeakingSessionId"" character varying(64) NOT NULL,
    ""Status"" integer NOT NULL,
    ""TranscriptId"" character varying(64) NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_SpeakingGraderCalibrationSamples"" PRIMARY KEY (""Id"")
);");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_SpeakingGraderCalibrationSamples_SpeakingSessionId\" " +
                "ON \"SpeakingGraderCalibrationSamples\" (\"SpeakingSessionId\");");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_SpeakingGraderCalibrationSamples_Status\" " +
                "ON \"SpeakingGraderCalibrationSamples\" (\"Status\");");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"SpeakingGraderCalibrationSamples\";");
        }
    }
}
