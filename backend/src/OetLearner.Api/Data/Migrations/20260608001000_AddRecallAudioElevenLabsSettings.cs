using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[DbContext(typeof(LearnerDbContext))]
[Migration("20260608001000_AddRecallAudioElevenLabsSettings")]
public partial class AddRecallAudioElevenLabsSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProviderName",
            table: "AudioRegenerationBatches",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "digitalocean-qwen3-tts");

        migrationBuilder.AddColumn<string>(
            name: "ElevenLabsTtsBaseUrl",
            table: "ConversationSettings",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ElevenLabsOutputFormat",
            table: "ConversationSettings",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ElevenLabsPronunciationDictionaryId",
            table: "ConversationSettings",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ElevenLabsPronunciationDictionaryVersionId",
            table: "ConversationSettings",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "ElevenLabsStability",
            table: "ConversationSettings",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "ElevenLabsSimilarityBoost",
            table: "ConversationSettings",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "ElevenLabsStyle",
            table: "ConversationSettings",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "ElevenLabsUseSpeakerBoost",
            table: "ConversationSettings",
            type: "boolean",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ProviderName", table: "AudioRegenerationBatches");
        migrationBuilder.DropColumn(name: "ElevenLabsTtsBaseUrl", table: "ConversationSettings");
        migrationBuilder.DropColumn(name: "ElevenLabsOutputFormat", table: "ConversationSettings");
        migrationBuilder.DropColumn(name: "ElevenLabsPronunciationDictionaryId", table: "ConversationSettings");
        migrationBuilder.DropColumn(name: "ElevenLabsPronunciationDictionaryVersionId", table: "ConversationSettings");
        migrationBuilder.DropColumn(name: "ElevenLabsStability", table: "ConversationSettings");
        migrationBuilder.DropColumn(name: "ElevenLabsSimilarityBoost", table: "ConversationSettings");
        migrationBuilder.DropColumn(name: "ElevenLabsStyle", table: "ConversationSettings");
        migrationBuilder.DropColumn(name: "ElevenLabsUseSpeakerBoost", table: "ConversationSettings");
    }
}
