using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[Migration("20260523200000_AddVoiceDesignBatchTracking")]
public partial class AddVoiceDesignBatchTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. AudioRegenerationBatches table
        migrationBuilder.CreateTable(
            name: "AudioRegenerationBatches",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AudioType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                TotalItems = table.Column<int>(type: "integer", nullable: false),
                CompletedItems = table.Column<int>(type: "integer", nullable: false),
                FailedItems = table.Column<int>(type: "integer", nullable: false),
                VoiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ModelVariant = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Speed = table.Column<double>(type: "double precision", nullable: false, defaultValue: 1.0),
                Pitch = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                Emotion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, defaultValue: ""),
                Instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                RequestedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AudioRegenerationBatches", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AudioRegenerationBatches_Status_StartedAt",
            table: "AudioRegenerationBatches",
            columns: new[] { "Status", "StartedAt" });

        // 2. ConversationSettings — add speed/pitch/emotion columns
        migrationBuilder.AddColumn<double>(
            name: "Qwen3Speed",
            table: "ConversationSettings",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "Qwen3Pitch",
            table: "ConversationSettings",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Qwen3Emotion",
            table: "ConversationSettings",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        // 3. ListeningTtsJobs — add batch tracking + voice override columns
        migrationBuilder.AddColumn<string>(
            name: "BatchId",
            table: "ListeningTtsJobs",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "VoiceOverride",
            table: "ListeningTtsJobs",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ModelVariantOverride",
            table: "ListeningTtsJobs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "InstructionsOverride",
            table: "ListeningTtsJobs",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "SpeedOverride",
            table: "ListeningTtsJobs",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "PitchOverride",
            table: "ListeningTtsJobs",
            type: "double precision",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_ListeningTtsJobs_BatchId",
            table: "ListeningTtsJobs",
            column: "BatchId");

        // 4. VocabularyTerms — add batch tracking
        migrationBuilder.AddColumn<string>(
            name: "AudioBatchId",
            table: "VocabularyTerms",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        // 5. ListeningExtracts — add TTS voice tracking
        migrationBuilder.AddColumn<string>(
            name: "TtsVoice",
            table: "ListeningExtracts",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TtsModelVariant",
            table: "ListeningExtracts",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        // 6. ConversationTemplates — add TTS voice tracking
        migrationBuilder.AddColumn<string>(
            name: "TtsVoice",
            table: "ConversationTemplates",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TtsModelVariant",
            table: "ConversationTemplates",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OpeningAudioSha",
            table: "ConversationTemplates",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AudioRegenerationBatches");

        migrationBuilder.DropColumn(name: "Qwen3Speed", table: "ConversationSettings");
        migrationBuilder.DropColumn(name: "Qwen3Pitch", table: "ConversationSettings");
        migrationBuilder.DropColumn(name: "Qwen3Emotion", table: "ConversationSettings");

        migrationBuilder.DropIndex(name: "IX_ListeningTtsJobs_BatchId", table: "ListeningTtsJobs");
        migrationBuilder.DropColumn(name: "BatchId", table: "ListeningTtsJobs");
        migrationBuilder.DropColumn(name: "VoiceOverride", table: "ListeningTtsJobs");
        migrationBuilder.DropColumn(name: "ModelVariantOverride", table: "ListeningTtsJobs");
        migrationBuilder.DropColumn(name: "InstructionsOverride", table: "ListeningTtsJobs");
        migrationBuilder.DropColumn(name: "SpeedOverride", table: "ListeningTtsJobs");
        migrationBuilder.DropColumn(name: "PitchOverride", table: "ListeningTtsJobs");

        migrationBuilder.DropColumn(name: "AudioBatchId", table: "VocabularyTerms");

        migrationBuilder.DropColumn(name: "TtsVoice", table: "ListeningExtracts");
        migrationBuilder.DropColumn(name: "TtsModelVariant", table: "ListeningExtracts");

        migrationBuilder.DropColumn(name: "TtsVoice", table: "ConversationTemplates");
        migrationBuilder.DropColumn(name: "TtsModelVariant", table: "ConversationTemplates");
        migrationBuilder.DropColumn(name: "OpeningAudioSha", table: "ConversationTemplates");
    }
}
