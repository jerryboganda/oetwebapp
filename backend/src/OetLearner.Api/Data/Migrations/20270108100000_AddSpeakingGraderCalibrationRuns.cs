using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Speaking grader calibration (owner spec 4 Oct 2026) — the harness. A run grades every expert-labelled performance
    /// several times with the current grader and keeps only numbers (scores, source, provider, error code): no learner
    /// text, no assessment row, no credit. Two additive, idempotent tables; the blue/green slots overlap and neither the
    /// old nor the new slot is affected by a table it never reads.
    ///
    /// HAND-AUTHORED (repo convention): inline <c>[Migration]</c>/<c>[DbContext]</c>, no Designer file;
    /// the matching entities are in <c>LearnerDbContextModelSnapshot</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270108100000_AddSpeakingGraderCalibrationRuns")]
    public partial class AddSpeakingGraderCalibrationRuns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""SpeakingGraderCalibrationRuns"" (
    ""Id"" character varying(64) NOT NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""CreatedById"" character varying(64) NOT NULL,
    ""FinalizedAt"" timestamp with time zone NULL,
    ""GraderVersion"" character varying(160) NOT NULL,
    ""ReportJson"" text NULL,
    ""Repeats"" integer NOT NULL,
    ""Status"" integer NOT NULL,
    ""UseAudio"" boolean NOT NULL,
    CONSTRAINT ""PK_SpeakingGraderCalibrationRuns"" PRIMARY KEY (""Id"")
);");
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""SpeakingGraderCalibrationGrades"" (
    ""Id"" character varying(64) NOT NULL,
    ""Attempts"" integer NOT NULL,
    ""CompletedAt"" timestamp with time zone NULL,
    ""Error"" character varying(64) NULL,
    ""GraderVersion"" character varying(160) NULL,
    ""IntelligibilitySource"" character varying(16) NULL,
    ""ModelId"" character varying(96) NULL,
    ""OperationId"" character varying(64) NULL,
    ""Provider"" character varying(32) NULL,
    ""QueuedAt"" timestamp with time zone NULL,
    ""RawTotal"" integer NULL,
    ""Repeat"" integer NOT NULL,
    ""ReportedScaled"" integer NULL,
    ""RunId"" character varying(64) NOT NULL,
    ""SampleId"" character varying(64) NOT NULL,
    ""ScoresJson"" text NULL,
    ""Status"" integer NOT NULL,
    CONSTRAINT ""PK_SpeakingGraderCalibrationGrades"" PRIMARY KEY (""Id"")
);");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_SpeakingGraderCalibrationGrades_RunId_SampleId_Repeat\" " +
                "ON \"SpeakingGraderCalibrationGrades\" (\"RunId\", \"SampleId\", \"Repeat\");");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"SpeakingGraderCalibrationGrades\";");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"SpeakingGraderCalibrationRuns\";");
        }
    }
}
