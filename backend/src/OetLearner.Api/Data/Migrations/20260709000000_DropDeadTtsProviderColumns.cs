using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropDeadTtsProviderColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AzureTtsDefaultVoice",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "ChatTtsApiKeyEncrypted",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "ChatTtsBaseUrl",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "ChatTtsDefaultVoice",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "CosyVoiceApiKeyEncrypted",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "CosyVoiceBaseUrl",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "CosyVoiceDefaultVoice",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "GptSoVitsApiKeyEncrypted",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "GptSoVitsBaseUrl",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "GptSoVitsDefaultVoice",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "Qwen3Emotion",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "Qwen3ModelVariant",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "Qwen3Pitch",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "Qwen3Speed",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "Qwen3VoiceId",
                table: "ConversationSettings");

            migrationBuilder.DropColumn(
                name: "Qwen3VoiceInstructions",
                table: "ConversationSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AzureTtsDefaultVoice",
                table: "ConversationSettings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChatTtsApiKeyEncrypted",
                table: "ConversationSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChatTtsBaseUrl",
                table: "ConversationSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChatTtsDefaultVoice",
                table: "ConversationSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CosyVoiceApiKeyEncrypted",
                table: "ConversationSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CosyVoiceBaseUrl",
                table: "ConversationSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CosyVoiceDefaultVoice",
                table: "ConversationSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GptSoVitsApiKeyEncrypted",
                table: "ConversationSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GptSoVitsBaseUrl",
                table: "ConversationSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GptSoVitsDefaultVoice",
                table: "ConversationSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Qwen3Emotion",
                table: "ConversationSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Qwen3ModelVariant",
                table: "ConversationSettings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Qwen3Pitch",
                table: "ConversationSettings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Qwen3Speed",
                table: "ConversationSettings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Qwen3VoiceId",
                table: "ConversationSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Qwen3VoiceInstructions",
                table: "ConversationSettings",
                type: "text",
                nullable: true);
        }
    }
}
