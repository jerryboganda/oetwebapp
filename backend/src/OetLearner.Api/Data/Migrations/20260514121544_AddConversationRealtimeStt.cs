using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationRealtimeStt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FinalizedAt",
                table: "ConversationTurns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderEventId",
                table: "ConversationTurns",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderName",
                table: "ConversationTurns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TurnClientId",
                table: "ConversationTurns",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ElevenLabsSttApiKeyEncrypted",
                table: "ConversationSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ElevenLabsSttAudioFormat",
                table: "ConversationSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ElevenLabsSttBaseUrl",
                table: "ConversationSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ElevenLabsSttCommitStrategy",
                table: "ConversationSettings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ElevenLabsSttEnableProviderLogging",
                table: "ConversationSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ElevenLabsSttKeytermsCsv",
                table: "ConversationSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ElevenLabsSttLanguage",
                table: "ConversationSettings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ElevenLabsSttModel",
                table: "ConversationSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ElevenLabsSttTokenTtlSeconds",
                table: "ConversationSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RealtimeAsrProvider",
                table: "ConversationSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RealtimeSttConsentVersion",
                table: "ConversationSettings",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RealtimeSttDailyAudioSecondsPerUser",
                table: "ConversationSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RealtimeSttEnabled",
                table: "ConversationSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RealtimeSttFallbackToBatch",
                table: "ConversationSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RealtimeSttMaxAudioSecondsPerSession",
                table: "ConversationSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RealtimeSttMaxChunkBytes",
                table: "ConversationSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RealtimeSttMaxConcurrentStreamsPerUser",
                table: "ConversationSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RealtimeSttMonthlyBudgetCapUsd",
                table: "ConversationSettings",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RealtimeSttPartialMinIntervalMs",
                table: "ConversationSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RealtimeSttRollbackMode",
                table: "ConversationSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RealtimeSttTurnIdleTimeoutSeconds",
                table: "ConversationSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_SessionId_ProviderEventId",
                table: "ConversationTurns",
                columns: new[] { "SessionId", "ProviderEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_SessionId_TurnClientId",
                table: "ConversationTurns",
                columns: new[] { "SessionId", "TurnClientId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConversationTurns_SessionId_ProviderEventId",
                table: "ConversationTurns");

            migrationBuilder.DropIndex(
                name: "IX_ConversationTurns_SessionId_TurnClientId",
                table: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "FinalizedAt",
                table: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "ProviderEventId",
                table: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "ProviderName",
                table: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "TurnClientId",
                table: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "ElevenLabsSttApiKeyEncrypted",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "ElevenLabsSttAudioFormat",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "ElevenLabsSttBaseUrl",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "ElevenLabsSttCommitStrategy",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "ElevenLabsSttEnableProviderLogging",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "ElevenLabsSttKeytermsCsv",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "ElevenLabsSttLanguage",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "ElevenLabsSttModel",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "ElevenLabsSttTokenTtlSeconds",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeAsrProvider",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttConsentVersion",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttDailyAudioSecondsPerUser",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttEnabled",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttFallbackToBatch",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttMaxAudioSecondsPerSession",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttMaxChunkBytes",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttMaxConcurrentStreamsPerUser",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttMonthlyBudgetCapUsd",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttPartialMinIntervalMs",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttRollbackMode",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "RealtimeSttTurnIdleTimeoutSeconds",
                table: "ConversationSettings");
        }
    }
}
