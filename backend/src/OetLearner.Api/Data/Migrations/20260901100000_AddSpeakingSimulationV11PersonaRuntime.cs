using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[DbContext(typeof(LearnerDbContext))]
[Migration("20260901100000_AddSpeakingSimulationV11PersonaRuntime")]
public partial class AddSpeakingSimulationV11PersonaRuntime : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "InterlocutorScripts"
                ADD COLUMN IF NOT EXISTS "AllowsSecondVisit" boolean NOT NULL DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS "SecondVisitIndicator" character varying(500) NULL,
                ADD COLUMN IF NOT EXISTS "SecondVisitCarryFactsJson" jsonb NOT NULL DEFAULT '[]'::jsonb;

            CREATE TABLE IF NOT EXISTS "SpeakingSimulationV11PersonaRuntimeSnapshots" (
                "Id" character varying(64) NOT NULL,
                "ExamSessionId" character varying(64) NULL,
                "SpeakingSessionId" character varying(64) NOT NULL,
                "RolePlayCardId" character varying(64) NOT NULL,
                "CardSlot" character varying(2) NOT NULL,
                "ProfessionId" character varying(32) NOT NULL,
                "SpecVersion" character varying(64) NOT NULL,
                "PersonaVersion" character varying(64) NOT NULL,
                "CardVersion" character varying(64) NOT NULL,
                "MemoryScopeKey" character varying(128) NOT NULL,
                "ScenarioTitle" character varying(200) NOT NULL,
                "Setting" character varying(160) NOT NULL,
                "CandidateRole" character varying(256) NOT NULL,
                "InterlocutorRole" character varying(256) NOT NULL,
                "PatientEmotion" character varying(256) NOT NULL,
                "CommunicationGoal" character varying(256) NOT NULL,
                "ClinicalTopic" character varying(256) NOT NULL,
                "PersonaRole" character varying(32) NOT NULL,
                "AllowedFactsJson" jsonb NOT NULL,
                "ApprovedCarryFactKeysJson" jsonb NOT NULL,
                "ProhibitedFactsJson" jsonb NOT NULL,
                "RevealConditionsJson" jsonb NOT NULL,
                "ExplicitSecondVisitIndicator" character varying(500) NULL,
                "CarriedFactsJson" jsonb NOT NULL,
                "FollowUpEligible" boolean NOT NULL,
                "FollowUpActivated" boolean NOT NULL,
                "FollowUpActivatedAt" timestamp with time zone NULL,
                "PersonaJson" jsonb NOT NULL,
                "CapturedAt" timestamp with time zone NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SpeakingSimulationV11PersonaRuntimeSnapshots" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_SpeakingSimulationV11PersonaRuntimeSnapshots_SpeakingExamSessions_ExamSessionId"
                    FOREIGN KEY ("ExamSessionId") REFERENCES "SpeakingExamSessions" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SpeakingSimulationV11PersonaRuntimeSnapshots_SpeakingSessions_SpeakingSessionId"
                    FOREIGN KEY ("SpeakingSessionId") REFERENCES "SpeakingSessions" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_SpeakingSimulationV11PersonaRuntimeSnapshots_RolePlayCards_RolePlayCardId"
                    FOREIGN KEY ("RolePlayCardId") REFERENCES "RolePlayCards" ("Id") ON DELETE RESTRICT
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11PersonaRuntimeSnapshots_SpeakingSessionId"
                ON "SpeakingSimulationV11PersonaRuntimeSnapshots" ("SpeakingSessionId");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11PersonaRuntimeSnapshots_ExamSessionId_CardSlot"
                ON "SpeakingSimulationV11PersonaRuntimeSnapshots" ("ExamSessionId", "CardSlot");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11PersonaRuntimeSnapshots_MemoryScopeKey"
                ON "SpeakingSimulationV11PersonaRuntimeSnapshots" ("MemoryScopeKey");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS "SpeakingSimulationV11PersonaRuntimeSnapshots";
            ALTER TABLE "InterlocutorScripts"
                DROP COLUMN IF EXISTS "SecondVisitCarryFactsJson",
                DROP COLUMN IF EXISTS "SecondVisitIndicator",
                DROP COLUMN IF EXISTS "AllowsSecondVisit";
            """);
    }
}
