using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[DbContext(typeof(LearnerDbContext))]
[Migration("20260901120000_AddSpeakingSimulationV11AssessmentReport")]
public partial class AddSpeakingSimulationV11AssessmentReport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "SpeakingSimulationV11Assessments"
                ADD COLUMN IF NOT EXISTS "AssessmentKind" character varying(16) NOT NULL DEFAULT 'card',
                ADD COLUMN IF NOT EXISTS "CardSlot" character varying(16) NOT NULL DEFAULT 'standalone',
                ADD COLUMN IF NOT EXISTS "EstimatedPracticeScore" integer NULL,
                ADD COLUMN IF NOT EXISTS "ScoreRangeLow" integer NULL,
                ADD COLUMN IF NOT EXISTS "ScoreRangeHigh" integer NULL,
                ADD COLUMN IF NOT EXISTS "Provider" character varying(128) NULL,
                ADD COLUMN IF NOT EXISTS "ModelName" character varying(128) NULL,
                ADD COLUMN IF NOT EXISTS "PromptTemplateId" character varying(128) NULL,
                ADD COLUMN IF NOT EXISTS "SourceTranscriptId" character varying(64) NULL,
                ADD COLUMN IF NOT EXISTS "SourceRecordingId" character varying(64) NULL,
                ADD COLUMN IF NOT EXISTS "CardVersion" character varying(64) NULL,
                ADD COLUMN IF NOT EXISTS "TechnicalReviewCode" character varying(64) NULL,
                ADD COLUMN IF NOT EXISTS "ReportJson" jsonb NULL;

            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11Assessments_ExamSessionId_AssessmentKind_CardSlot"
                ON "SpeakingSimulationV11Assessments" ("ExamSessionId", "AssessmentKind", "CardSlot");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_SpeakingSimulationV11Assessments_ExamSessionId_AssessmentKind_CardSlot";
            ALTER TABLE "SpeakingSimulationV11Assessments"
                DROP COLUMN IF EXISTS "ReportJson",
                DROP COLUMN IF EXISTS "TechnicalReviewCode",
                DROP COLUMN IF EXISTS "CardVersion",
                DROP COLUMN IF EXISTS "SourceRecordingId",
                DROP COLUMN IF EXISTS "SourceTranscriptId",
                DROP COLUMN IF EXISTS "PromptTemplateId",
                DROP COLUMN IF EXISTS "ModelName",
                DROP COLUMN IF EXISTS "Provider",
                DROP COLUMN IF EXISTS "ScoreRangeHigh",
                DROP COLUMN IF EXISTS "ScoreRangeLow",
                DROP COLUMN IF EXISTS "EstimatedPracticeScore",
                DROP COLUMN IF EXISTS "CardSlot",
                DROP COLUMN IF EXISTS "AssessmentKind";
            """);
    }
}
