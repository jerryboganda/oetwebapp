using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Speaking grader calibration (owner request 7 Oct 2026) — the Full Mock sample kind and pilot runs.
    /// One additive table: <c>SpeakingGraderCalibrationMockSamples</c>, the expert's ONE blind mark of a whole
    /// two-card test, against which the combined grader (speaking.score.v3-combined) is compared. Two additive
    /// columns on <c>SpeakingGraderCalibrationRuns</c>: <c>Scope</c> (card | mock; existing rows read as card)
    /// and <c>Pilot</c> (an informational owner-pilot run whose verdict can never pass). All idempotent; the
    /// blue/green slots overlap and neither the old nor the new slot is affected by a table it never reads.
    ///
    /// HAND-AUTHORED (repo convention): inline <c>[Migration]</c>/<c>[DbContext]</c>, no Designer file;
    /// the matching entities are in <c>LearnerDbContextModelSnapshot</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270112090000_AddSpeakingGraderCalibrationMocksAndPilot")]
    public partial class AddSpeakingGraderCalibrationMocksAndPilot : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""SpeakingGraderCalibrationMockSamples"" (
    ""Id"" character varying(64) NOT NULL,
    ""CardAId"" character varying(64) NOT NULL,
    ""CardBId"" character varying(64) NOT NULL,
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
    ""SessionAId"" character varying(64) NOT NULL,
    ""SessionBId"" character varying(64) NOT NULL,
    ""SpeakingExamId"" character varying(64) NOT NULL,
    ""Status"" integer NOT NULL,
    ""TranscriptAId"" character varying(64) NOT NULL,
    ""TranscriptBId"" character varying(64) NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_SpeakingGraderCalibrationMockSamples"" PRIMARY KEY (""Id"")
);");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_SpeakingGraderCalibrationMockSamples_SpeakingExamId\" " +
                "ON \"SpeakingGraderCalibrationMockSamples\" (\"SpeakingExamId\");");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_SpeakingGraderCalibrationMockSamples_Status\" " +
                "ON \"SpeakingGraderCalibrationMockSamples\" (\"Status\");");
            migrationBuilder.Sql(@"
ALTER TABLE ""SpeakingGraderCalibrationRuns"" ADD COLUMN IF NOT EXISTS ""Scope"" character varying(8) NOT NULL DEFAULT 'card';
ALTER TABLE ""SpeakingGraderCalibrationRuns"" ADD COLUMN IF NOT EXISTS ""Pilot"" boolean NOT NULL DEFAULT false;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"SpeakingGraderCalibrationMockSamples\";");
            migrationBuilder.Sql("ALTER TABLE \"SpeakingGraderCalibrationRuns\" DROP COLUMN IF EXISTS \"Scope\";");
            migrationBuilder.Sql("ALTER TABLE \"SpeakingGraderCalibrationRuns\" DROP COLUMN IF EXISTS \"Pilot\";");
        }
    }
}
