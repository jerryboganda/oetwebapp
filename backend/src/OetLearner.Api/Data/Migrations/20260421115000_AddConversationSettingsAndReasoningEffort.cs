using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationSettingsAndReasoningEffort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── ConversationSettings (runtime admin override singleton) ───────
            migrationBuilder.CreateTable(
                name: "ConversationSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: true),
                    AsrProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TtsProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AzureSpeechKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    AzureSpeechRegion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AzureLocale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AzureTtsDefaultVoice = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    WhisperBaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    WhisperApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    WhisperModel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeepgramApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    DeepgramModel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeepgramLanguage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ElevenLabsApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    ElevenLabsDefaultVoiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ElevenLabsModel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CosyVoiceBaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CosyVoiceApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    CosyVoiceDefaultVoice = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ChatTtsBaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ChatTtsApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    ChatTtsDefaultVoice = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    GptSoVitsBaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    GptSoVitsApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    GptSoVitsDefaultVoice = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MaxAudioBytes = table.Column<long>(type: "bigint", nullable: true),
                    AudioRetentionDays = table.Column<int>(type: "integer", nullable: true),
                    PrepDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    MaxSessionDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    MaxTurnDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    EnabledTaskTypesCsv = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FreeTierSessionsLimit = table.Column<int>(type: "integer", nullable: true),
                    FreeTierWindowDays = table.Column<int>(type: "integer", nullable: true),
                    ReplyModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EvaluationModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReplyTemperature = table.Column<double>(type: "double precision", nullable: true),
                    EvaluationTemperature = table.Column<double>(type: "double precision", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByUserName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationSettings", x => x.Id);
                });

            // ── AiProvider: ReasoningEffort per-provider column ──────────────
            migrationBuilder.AddColumn<string>(
                name: "ReasoningEffort",
                table: "AiProviders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ConversationSettings");
            migrationBuilder.DropColumn(name: "ReasoningEffort", table: "AiProviders");
        }
    }
}
