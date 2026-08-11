using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[DbContext(typeof(LearnerDbContext))]
[Migration("20260901140000_AddSpeakingSimulationV11TurnTelemetry")]
public partial class AddSpeakingSimulationV11TurnTelemetry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS "SpeakingSimulationV11TurnTelemetryRows" (
                "Id" character varying(64) NOT NULL,
                "SpeakingSessionId" character varying(64) NOT NULL,
                "SourceTranscriptId" character varying(64) NULL,
                "TurnNumber" integer NOT NULL,
                "Role" character varying(32) NOT NULL,
                "Phase" character varying(16) NOT NULL,
                "AsrProvider" character varying(64) NULL,
                "AsrModel" character varying(128) NULL,
                "AsrLatencyMs" integer NOT NULL,
                "ActorProvider" character varying(128) NULL,
                "ActorModel" character varying(128) NULL,
                "ActorUsageRecordId" character varying(64) NULL,
                "ActorLatencyMs" integer NOT NULL,
                "TtsProvider" character varying(128) NULL,
                "TtsModel" character varying(128) NULL,
                "TtsLatencyMs" integer NOT NULL,
                "TotalLatencyMs" integer NOT NULL,
                "InputTokens" integer NOT NULL,
                "OutputTokens" integer NOT NULL,
                "RetryCount" integer NOT NULL,
                "EstimatedCostUsd" numeric(18,6) NOT NULL,
                "ConcurrencyBucket" character varying(32) NOT NULL,
                "DegradationState" character varying(32) NOT NULL,
                "SpecVersion" character varying(64) NOT NULL,
                "RubricVersion" character varying(64) NOT NULL,
                "CalibrationVersion" character varying(64) NOT NULL,
                "BudgetBreachCode" character varying(64) NULL,
                "TechnicalReviewCode" character varying(64) NULL,
                "TechnicalReviewRequired" boolean NOT NULL,
                "CostComponentsJson" jsonb NOT NULL,
                "StartedAt" timestamp with time zone NOT NULL,
                "CompletedAt" timestamp with time zone NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_SpeakingSimulationV11TurnTelemetryRows" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_SpeakingSimulationV11TurnTelemetryRows_SpeakingSessions_SpeakingSessionId"
                    FOREIGN KEY ("SpeakingSessionId") REFERENCES "SpeakingSessions" ("Id") ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS "IX_SPV11TurnTelemetry_Session_Turn_Role"
                ON "SpeakingSimulationV11TurnTelemetryRows" ("SpeakingSessionId", "TurnNumber", "Role");
            CREATE INDEX IF NOT EXISTS "IX_SPV11TurnTelemetry_CreatedAt"
                ON "SpeakingSimulationV11TurnTelemetryRows" ("CreatedAt");
            CREATE INDEX IF NOT EXISTS "IX_SPV11TurnTelemetry_TechnicalReview_CreatedAt"
                ON "SpeakingSimulationV11TurnTelemetryRows" ("TechnicalReviewRequired", "CreatedAt");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS \"SpeakingSimulationV11TurnTelemetryRows\";");
    }
}
