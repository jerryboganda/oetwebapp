using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[DbContext(typeof(LearnerDbContext))]
[Migration("20260901130000_AddSpeakingSimulationV11TutorOverrides")]
public partial class AddSpeakingSimulationV11TutorOverrides : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS "SpeakingSimulationV11TutorOverrides" (
                "Id" character varying(64) NOT NULL,
                "AssessmentId" character varying(64) NOT NULL,
                "SpeakingSessionId" character varying(64) NOT NULL,
                "TutorId" character varying(64) NOT NULL,
                "EstimatedPracticeScore" integer NOT NULL,
                "ScoreRangeLow" integer NOT NULL,
                "ScoreRangeHigh" integer NOT NULL,
                "Reason" text NOT NULL,
                "OriginalReportJson" jsonb NOT NULL,
                "OverrideReportJson" jsonb NOT NULL,
                "OriginalSpecVersion" character varying(64) NOT NULL,
                "OriginalRubricVersion" character varying(64) NOT NULL,
                "OriginalCalibrationVersion" character varying(64) NOT NULL,
                "OriginalProvider" character varying(128) NULL,
                "OriginalModelName" character varying(128) NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SpeakingSimulationV11TutorOverrides" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_SPV11TutorOverrides_Assessment"
                    FOREIGN KEY ("AssessmentId") REFERENCES "SpeakingSimulationV11Assessments" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SPV11TutorOverrides_Session"
                    FOREIGN KEY ("SpeakingSessionId") REFERENCES "SpeakingSessions" ("Id") ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS "IX_SPV11TutorOverrides_Assessment"
                ON "SpeakingSimulationV11TutorOverrides" ("AssessmentId", "CreatedAt");
            CREATE INDEX IF NOT EXISTS "IX_SPV11TutorOverrides_Session"
                ON "SpeakingSimulationV11TutorOverrides" ("SpeakingSessionId", "CreatedAt");
            CREATE INDEX IF NOT EXISTS "IX_SPV11TutorOverrides_Tutor"
                ON "SpeakingSimulationV11TutorOverrides" ("TutorId", "CreatedAt");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS "SpeakingSimulationV11TutorOverrides";
            """);
    }
}
