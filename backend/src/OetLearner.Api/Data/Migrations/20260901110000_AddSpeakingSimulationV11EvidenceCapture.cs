using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[DbContext(typeof(LearnerDbContext))]
[Migration("20260901110000_AddSpeakingSimulationV11EvidenceCapture")]
public partial class AddSpeakingSimulationV11EvidenceCapture : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "SpeakingSimulationV11EvidenceRows"
                ADD COLUMN IF NOT EXISTS "EvidenceStatus" character varying(32) NOT NULL DEFAULT 'supported',
                ADD COLUMN IF NOT EXISTS "FindingText" text NULL,
                ADD COLUMN IF NOT EXISTS "ActionSuggestion" text NULL,
                ADD COLUMN IF NOT EXISTS "ConfidenceLabel" character varying(32) NULL,
                ADD COLUMN IF NOT EXISTS "ConfidenceScore" numeric(5,2) NULL,
                ADD COLUMN IF NOT EXISTS "IsPrimary" boolean NOT NULL DEFAULT TRUE,
                ADD COLUMN IF NOT EXISTS "SourceTranscriptId" character varying(64) NULL,
                ADD COLUMN IF NOT EXISTS "SourceRecordingId" character varying(64) NULL,
                ADD COLUMN IF NOT EXISTS "CardVersion" character varying(64) NULL;

            CREATE TABLE IF NOT EXISTS "SpeakingSimulationV11TurnEvidenceRows" (
                "Id" character varying(64) NOT NULL,
                "SpeakingSessionId" character varying(64) NOT NULL,
                "AssessmentId" character varying(64) NULL,
                "SourceTranscriptId" character varying(64) NOT NULL,
                "SourceRecordingId" character varying(64) NULL,
                "CardVersion" character varying(64) NOT NULL,
                "TurnNumber" integer NOT NULL,
                "Speaker" character varying(32) NOT NULL,
                "StartMs" bigint NOT NULL,
                "EndMs" bigint NOT NULL,
                "Text" text NOT NULL,
                "WordConfidenceJson" jsonb NOT NULL,
                "AsrProvider" character varying(32) NOT NULL,
                "IsInterrupted" boolean NOT NULL,
                "IsOverlap" boolean NOT NULL,
                "IsMonologue" boolean NOT NULL,
                "FillerCount" integer NOT NULL,
                "PauseCount" integer NOT NULL,
                "FalseStartCount" integer NOT NULL,
                "RepetitionCount" integer NOT NULL,
                "JargonCount" integer NOT NULL,
                "CapturedAt" timestamp with time zone NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SpeakingSimulationV11TurnEvidenceRows" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_SpeakingSimulationV11TurnEvidenceRows_SpeakingSessions_SpeakingSessionId"
                    FOREIGN KEY ("SpeakingSessionId") REFERENCES "SpeakingSessions" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_SpeakingSimulationV11TurnEvidenceRows_SpeakingSimulationV11Assessments_AssessmentId"
                    FOREIGN KEY ("AssessmentId") REFERENCES "SpeakingSimulationV11Assessments" ("Id") ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS "SpeakingSimulationV11AudioQualityChecks" (
                "Id" character varying(64) NOT NULL,
                "SpeakingSessionId" character varying(64) NOT NULL,
                "AssessmentId" character varying(64) NULL,
                "SourceRecordingId" character varying(64) NULL,
                "SourceMediaAssetId" character varying(64) NULL,
                "Status" integer NOT NULL,
                "OriginalSha256" character varying(64) NULL,
                "MimeType" character varying(96) NULL,
                "SizeBytes" bigint NULL,
                "DurationSeconds" integer NULL,
                "SampleRateHz" integer NULL,
                "Channels" integer NULL,
                "Codec" character varying(64) NULL,
                "IssueCode" character varying(64) NULL,
                "DetailsJson" jsonb NOT NULL,
                "CheckedAt" timestamp with time zone NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SpeakingSimulationV11AudioQualityChecks" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_SpeakingSimulationV11AudioQualityChecks_SpeakingSessions_SpeakingSessionId"
                    FOREIGN KEY ("SpeakingSessionId") REFERENCES "SpeakingSessions" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_SpeakingSimulationV11AudioQualityChecks_SpeakingSimulationV11Assessments_AssessmentId"
                    FOREIGN KEY ("AssessmentId") REFERENCES "SpeakingSimulationV11Assessments" ("Id") ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS "SpeakingSimulationV11CardTimingSnapshots" (
                "Id" character varying(64) NOT NULL,
                "ExamSessionId" character varying(64) NULL,
                "SpeakingSessionId" character varying(64) NOT NULL,
                "CardSlot" character varying(2) NOT NULL,
                "PrepStartedAt" timestamp with time zone NULL,
                "ActiveStartedAt" timestamp with time zone NULL,
                "EndedAt" timestamp with time zone NULL,
                "PrepSeconds" integer NOT NULL,
                "RolePlaySeconds" integer NOT NULL,
                "PrepDeadlineAt" timestamp with time zone NULL,
                "RolePlayDeadlineAt" timestamp with time zone NULL,
                "ServerElapsedSeconds" integer NULL,
                "ServerAuthoritative" boolean NOT NULL,
                "SourceCardVersion" character varying(64) NULL,
                "CapturedAt" timestamp with time zone NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SpeakingSimulationV11CardTimingSnapshots" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_SpeakingSimulationV11CardTimingSnapshots_SpeakingSessions_SpeakingSessionId"
                    FOREIGN KEY ("SpeakingSessionId") REFERENCES "SpeakingSessions" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_SpeakingSimulationV11CardTimingSnapshots_SpeakingExamSessions_ExamSessionId"
                    FOREIGN KEY ("ExamSessionId") REFERENCES "SpeakingExamSessions" ("Id") ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11EvidenceRows_SourceTranscriptId"
                ON "SpeakingSimulationV11EvidenceRows" ("SourceTranscriptId");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11EvidenceRows_SourceRecordingId"
                ON "SpeakingSimulationV11EvidenceRows" ("SourceRecordingId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11TurnEvidenceRows_Session_Transcript_Turn"
                ON "SpeakingSimulationV11TurnEvidenceRows" ("SpeakingSessionId", "SourceTranscriptId", "TurnNumber");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11TurnEvidenceRows_AssessmentId"
                ON "SpeakingSimulationV11TurnEvidenceRows" ("AssessmentId");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11TurnEvidenceRows_SourceRecordingId"
                ON "SpeakingSimulationV11TurnEvidenceRows" ("SourceRecordingId");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11AudioQualityChecks_Session_CheckedAt"
                ON "SpeakingSimulationV11AudioQualityChecks" ("SpeakingSessionId", "CheckedAt");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11AudioQualityChecks_Status"
                ON "SpeakingSimulationV11AudioQualityChecks" ("Status");
            CREATE INDEX IF NOT EXISTS "IX_SpeakingSimulationV11CardTimingSnapshots_Session_CapturedAt"
                ON "SpeakingSimulationV11CardTimingSnapshots" ("SpeakingSessionId", "CapturedAt");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS "SpeakingSimulationV11CardTimingSnapshots";
            DROP TABLE IF EXISTS "SpeakingSimulationV11AudioQualityChecks";
            DROP TABLE IF EXISTS "SpeakingSimulationV11TurnEvidenceRows";
            ALTER TABLE "SpeakingSimulationV11EvidenceRows"
                DROP COLUMN IF EXISTS "CardVersion",
                DROP COLUMN IF EXISTS "SourceRecordingId",
                DROP COLUMN IF EXISTS "SourceTranscriptId",
                DROP COLUMN IF EXISTS "IsPrimary",
                DROP COLUMN IF EXISTS "ConfidenceScore",
                DROP COLUMN IF EXISTS "ConfidenceLabel",
                DROP COLUMN IF EXISTS "ActionSuggestion",
                DROP COLUMN IF EXISTS "FindingText",
                DROP COLUMN IF EXISTS "EvidenceStatus";
            """);
    }
}
