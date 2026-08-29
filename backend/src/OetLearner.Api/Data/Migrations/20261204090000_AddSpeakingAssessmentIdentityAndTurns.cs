using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// W7 — Speaking assessment identity, patient-turn idempotency, and
    /// bounded conversation summary.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261204090000_AddSpeakingAssessmentIdentityAndTurns")]
    public partial class AddSpeakingAssessmentIdentityAndTurns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "SpeakingAiAssessments" ADD COLUMN IF NOT EXISTS "IdentityHash" character varying(64);
                ALTER TABLE "SpeakingAiAssessments" ADD COLUMN IF NOT EXISTS "TranscriptHash" character varying(64);
                ALTER TABLE "SpeakingAiAssessments" ADD COLUMN IF NOT EXISTS "RubricVersion" character varying(64);
                ALTER TABLE "SpeakingAiAssessments" ADD COLUMN IF NOT EXISTS "CardId" character varying(64);
                ALTER TABLE "SpeakingAiAssessments" ADD COLUMN IF NOT EXISTS "GradeOperationId" character varying(64);
                ALTER TABLE "SpeakingAiAssessments" ADD COLUMN IF NOT EXISTS "ClaimedAt" timestamp with time zone;
                ALTER TABLE "SpeakingAiAssessments" ADD COLUMN IF NOT EXISTS "ClaimOwner" character varying(128);
                ALTER TABLE "SpeakingAiAssessments" ADD COLUMN IF NOT EXISTS "ProviderResultJson" text;
                ALTER TABLE "SpeakingAiAssessments" ADD COLUMN IF NOT EXISTS "IsDuplicate" boolean NOT NULL DEFAULT FALSE;

                ALTER TABLE "SpeakingSimulationV11Assessments" ADD COLUMN IF NOT EXISTS "IdentityHash" character varying(64);
                ALTER TABLE "SpeakingSimulationV11Assessments" ADD COLUMN IF NOT EXISTS "TranscriptHash" character varying(64);
                ALTER TABLE "SpeakingSimulationV11Assessments" ADD COLUMN IF NOT EXISTS "GradeOperationId" character varying(64);
                ALTER TABLE "SpeakingSimulationV11Assessments" ADD COLUMN IF NOT EXISTS "IsDuplicate" boolean NOT NULL DEFAULT FALSE;

                ALTER TABLE "SpeakingSessions" ADD COLUMN IF NOT EXISTS "ConversationSummaryText" text;

                CREATE TABLE IF NOT EXISTS "SpeakingPatientTurns" (
                    "Id" character varying(64) NOT NULL,
                    "SessionId" character varying(64) NOT NULL,
                    "ClientTurnId" character varying(64),
                    "SequenceNumber" integer NOT NULL,
                    "Role" character varying(16) NOT NULL,
                    "Text" text NOT NULL,
                    "ResponseJson" text NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_SpeakingPatientTurns" PRIMARY KEY ("Id")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_SpeakingAiAssessments_IdentityHash"
                    ON "SpeakingAiAssessments" ("IdentityHash")
                    WHERE "IdentityHash" IS NOT NULL;

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_SpeakingSimulationV11Assessments_IdentityHash"
                    ON "SpeakingSimulationV11Assessments" ("IdentityHash")
                    WHERE "IdentityHash" IS NOT NULL;

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_SpeakingPatientTurns_Session_ClientTurnId"
                    ON "SpeakingPatientTurns" ("SessionId", "ClientTurnId")
                    WHERE "ClientTurnId" IS NOT NULL;

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_SpeakingPatientTurns_Session_Sequence"
                    ON "SpeakingPatientTurns" ("SessionId", "SequenceNumber");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "UX_SpeakingPatientTurns_Session_Sequence";
                DROP INDEX IF EXISTS "UX_SpeakingPatientTurns_Session_ClientTurnId";
                DROP INDEX IF EXISTS "UX_SpeakingSimulationV11Assessments_IdentityHash";
                DROP INDEX IF EXISTS "UX_SpeakingAiAssessments_IdentityHash";
                DROP TABLE IF EXISTS "SpeakingPatientTurns";
                ALTER TABLE "SpeakingSessions" DROP COLUMN IF EXISTS "ConversationSummaryText";
                ALTER TABLE "SpeakingSimulationV11Assessments" DROP COLUMN IF EXISTS "IsDuplicate";
                ALTER TABLE "SpeakingSimulationV11Assessments" DROP COLUMN IF EXISTS "GradeOperationId";
                ALTER TABLE "SpeakingSimulationV11Assessments" DROP COLUMN IF EXISTS "TranscriptHash";
                ALTER TABLE "SpeakingSimulationV11Assessments" DROP COLUMN IF EXISTS "IdentityHash";
                ALTER TABLE "SpeakingAiAssessments" DROP COLUMN IF EXISTS "IsDuplicate";
                ALTER TABLE "SpeakingAiAssessments" DROP COLUMN IF EXISTS "ProviderResultJson";
                ALTER TABLE "SpeakingAiAssessments" DROP COLUMN IF EXISTS "ClaimOwner";
                ALTER TABLE "SpeakingAiAssessments" DROP COLUMN IF EXISTS "ClaimedAt";
                ALTER TABLE "SpeakingAiAssessments" DROP COLUMN IF EXISTS "GradeOperationId";
                ALTER TABLE "SpeakingAiAssessments" DROP COLUMN IF EXISTS "CardId";
                ALTER TABLE "SpeakingAiAssessments" DROP COLUMN IF EXISTS "RubricVersion";
                ALTER TABLE "SpeakingAiAssessments" DROP COLUMN IF EXISTS "TranscriptHash";
                ALTER TABLE "SpeakingAiAssessments" DROP COLUMN IF EXISTS "IdentityHash";
                """);
        }
    }
}
