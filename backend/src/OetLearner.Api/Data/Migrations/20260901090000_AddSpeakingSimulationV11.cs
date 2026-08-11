using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Adds the immutable Speaking simulation v1.1 release, governance, evidence,
/// assessment, and per-turn metric persistence model. This migration follows
/// the idempotent PostgreSQL SQL style used by the existing Speaking migrations;
/// test providers build the schema directly from the EF model.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260901090000_AddSpeakingSimulationV11")]
public partial class AddSpeakingSimulationV11 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS "SpeakingSimulationV11SpecReleases" (
                "Id" character varying(64) NOT NULL,
                "SpecVersion" character varying(64) NOT NULL,
                "ReleaseVersion" character varying(64) NOT NULL,
                "Status" integer NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SpeakingSimulationV11SpecReleases" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "SpeakingSimulationV11RubricReleases" (
                "Id" character varying(64) NOT NULL,
                "RubricVersion" character varying(64) NOT NULL,
                "CalibrationVersion" character varying(64) NOT NULL,
                "Status" integer NOT NULL,
                "CriteriaJson" jsonb NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SpeakingSimulationV11RubricReleases" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "SpeakingSimulationV11OwnerApprovals" (
                "Id" character varying(128) NOT NULL,
                "ApprovalKey" character varying(64) NOT NULL,
                "ScopeKey" character varying(64) NOT NULL,
                "SpecVersion" character varying(64) NULL,
                "RubricVersion" character varying(64) NULL,
                "Status" integer NOT NULL,
                "NumericValue" numeric(18,2) NULL,
                "EvidenceJson" jsonb NULL,
                "ApprovedByUserId" character varying(64) NULL,
                "ApprovedAt" timestamp with time zone NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SpeakingSimulationV11OwnerApprovals" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "SpeakingSimulationV11Assessments" (
                "Id" character varying(64) NOT NULL,
                "ExamSessionId" character varying(64) NULL,
                "SpeakingSessionId" character varying(64) NULL,
                "RolePlayCardId" character varying(64) NULL,
                "ProfessionId" character varying(32) NOT NULL,
                "SpecVersion" character varying(64) NOT NULL,
                "RubricVersion" character varying(64) NOT NULL,
                "CalibrationVersion" character varying(64) NOT NULL,
                "Status" integer NOT NULL,
                "AudioQualityStatus" integer NOT NULL,
                "ConfidenceScore" numeric(5,2) NULL,
                "ConfidenceLabel" character varying(32) NULL,
                "ConfidenceRange" character varying(64) NULL,
                "GraphDisclaimer" text NOT NULL,
                "GeneratedAt" timestamp with time zone NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SpeakingSimulationV11Assessments" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_SpeakingSimulationV11Assessments_SpeakingExamSessions_ExamSessionId"
                    FOREIGN KEY ("ExamSessionId") REFERENCES "SpeakingExamSessions" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SpeakingSimulationV11Assessments_SpeakingSessions_SpeakingSessionId"
                    FOREIGN KEY ("SpeakingSessionId") REFERENCES "SpeakingSessions" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SpeakingSimulationV11Assessments_RolePlayCards_RolePlayCardId"
                    FOREIGN KEY ("RolePlayCardId") REFERENCES "RolePlayCards" ("Id") ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS "SpeakingSimulationV11PersonaSnapshots" (
                "Id" character varying(64) NOT NULL,
                "AssessmentId" character varying(64) NOT NULL,
                "RolePlayCardId" character varying(64) NULL,
                "PersonaRole" character varying(32) NOT NULL,
                "ScenarioTitle" character varying(200) NOT NULL,
                "Setting" character varying(160) NOT NULL,
                "CandidateRole" character varying(256) NOT NULL,
                "InterlocutorRole" character varying(256) NOT NULL,
                "PatientEmotion" character varying(256) NOT NULL,
                "CommunicationGoal" character varying(256) NOT NULL,
                "ClinicalTopic" character varying(256) NOT NULL,
                "PersonaJson" jsonb NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SpeakingSimulationV11PersonaSnapshots" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_SpeakingSimulationV11PersonaSnapshots_SpeakingSimulationV11Assessments_AssessmentId"
                    FOREIGN KEY ("AssessmentId") REFERENCES "SpeakingSimulationV11Assessments" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_SpeakingSimulationV11PersonaSnapshots_RolePlayCards_RolePlayCardId"
                    FOREIGN KEY ("RolePlayCardId") REFERENCES "RolePlayCards" ("Id") ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS "SpeakingSimulationV11EvidenceRows" (
                "Id" character varying(64) NOT NULL,
                "AssessmentId" character varying(64) NOT NULL,
                "PrimaryCriterionCode" character varying(32) NOT NULL,
                "CriterionCode" character varying(32) NOT NULL,
                "EvidenceType" character varying(32) NOT NULL,
                "TurnNumber" integer NULL,
                "SourceReference" character varying(256) NULL,
                "QuoteText" text NOT NULL,
                "StartMs" integer NULL,
                "EndMs" integer NULL,
                "GeneratedAt" timestamp with time zone NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SpeakingSimulationV11EvidenceRows" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_SpeakingSimulationV11EvidenceRows_SpeakingSimulationV11Assessments_AssessmentId"
                    FOREIGN KEY ("AssessmentId") REFERENCES "SpeakingSimulationV11Assessments" ("Id") ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS "SpeakingSimulationV11CriterionScores" (
                "Id" character varying(64) NOT NULL,
                "AssessmentId" character varying(64) NOT NULL,
                "CriterionCode" character varying(32) NOT NULL,
                "Weight" integer NOT NULL,
                "RawScore" numeric(8,2) NOT NULL,
                "WeightedScore" numeric(8,2) NOT NULL,
                "ScoreBand" character varying(32) NULL,
                "Rationale" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SpeakingSimulationV11CriterionScores" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_SpeakingSimulationV11CriterionScores_SpeakingSimulationV11Assessments_AssessmentId"
                    FOREIGN KEY ("AssessmentId") REFERENCES "SpeakingSimulationV11Assessments" ("Id") ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS "SpeakingSimulationV11TurnMetrics" (
                "Id" character varying(64) NOT NULL,
                "AssessmentId" character varying(64) NOT NULL,
                "TurnNumber" integer NOT NULL,
                "ModelName" character varying(128) NOT NULL,
                "PromptLatencyMs" integer NOT NULL,
                "CompletionLatencyMs" integer NOT NULL,
                "TotalLatencyMs" integer NOT NULL,
                "InputTokens" integer NOT NULL,
                "OutputTokens" integer NOT NULL,
                "EstimatedCostUsd" numeric(18,6) NOT NULL,
                "GeneratedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SpeakingSimulationV11TurnMetrics" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_SpeakingSimulationV11TurnMetrics_SpeakingSimulationV11Assessments_AssessmentId"
                    FOREIGN KEY ("AssessmentId") REFERENCES "SpeakingSimulationV11Assessments" ("Id") ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11SpecReleases_SpecVersion_ReleaseVersion"
                ON "SpeakingSimulationV11SpecReleases" ("SpecVersion", "ReleaseVersion");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11SpecReleases_Status"
                ON "SpeakingSimulationV11SpecReleases" ("Status");

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11RubricReleases_RubricVersion_CalibrationVersion"
                ON "SpeakingSimulationV11RubricReleases" ("RubricVersion", "CalibrationVersion");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11RubricReleases_Status"
                ON "SpeakingSimulationV11RubricReleases" ("Status");

            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11OwnerApprovals_ApprovalKey_ScopeKey_Status"
                ON "SpeakingSimulationV11OwnerApprovals" ("ApprovalKey", "ScopeKey", "Status");

            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11Assessments_ExamSessionId_SpeakingSessionId"
                ON "SpeakingSimulationV11Assessments" ("ExamSessionId", "SpeakingSessionId");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11Assessments_SpeakingSessionId"
                ON "SpeakingSimulationV11Assessments" ("SpeakingSessionId");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11Assessments_RolePlayCardId"
                ON "SpeakingSimulationV11Assessments" ("RolePlayCardId");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11Assessments_Status"
                ON "SpeakingSimulationV11Assessments" ("Status");

            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11PersonaSnapshots_AssessmentId_PersonaRole"
                ON "SpeakingSimulationV11PersonaSnapshots" ("AssessmentId", "PersonaRole");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11PersonaSnapshots_RolePlayCardId"
                ON "SpeakingSimulationV11PersonaSnapshots" ("RolePlayCardId");

            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11EvidenceRows_AssessmentId"
                ON "SpeakingSimulationV11EvidenceRows" ("AssessmentId");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11EvidenceRows_PrimaryCriterionCode"
                ON "SpeakingSimulationV11EvidenceRows" ("PrimaryCriterionCode");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11EvidenceRows_GeneratedAt"
                ON "SpeakingSimulationV11EvidenceRows" ("GeneratedAt");

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11CriterionScores_AssessmentId_CriterionCode"
                ON "SpeakingSimulationV11CriterionScores" ("AssessmentId", "CriterionCode");

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11TurnMetrics_AssessmentId_TurnNumber"
                ON "SpeakingSimulationV11TurnMetrics" ("AssessmentId", "TurnNumber");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11TurnMetrics_GeneratedAt"
                ON "SpeakingSimulationV11TurnMetrics" ("GeneratedAt");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS "SpeakingSimulationV11TurnMetrics";
            DROP TABLE IF EXISTS "SpeakingSimulationV11CriterionScores";
            DROP TABLE IF EXISTS "SpeakingSimulationV11EvidenceRows";
            DROP TABLE IF EXISTS "SpeakingSimulationV11PersonaSnapshots";
            DROP TABLE IF EXISTS "SpeakingSimulationV11Assessments";
            DROP TABLE IF EXISTS "SpeakingSimulationV11OwnerApprovals";
            DROP TABLE IF EXISTS "SpeakingSimulationV11RubricReleases";
            DROP TABLE IF EXISTS "SpeakingSimulationV11SpecReleases";
            """);
    }
}
