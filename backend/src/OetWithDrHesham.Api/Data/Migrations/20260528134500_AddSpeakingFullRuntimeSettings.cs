using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260528134500_AddSpeakingFullRuntimeSettings")]
    public partial class AddSpeakingFullRuntimeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Speaking LiveKit ───────────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "SpeakingLiveKitProvider",
                table: "RuntimeSettings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakingLiveKitApiKeyEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakingLiveKitApiSecretEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakingLiveKitWssUrl",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakingLiveKitWebhookSigningSecretEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakingLiveKitEgressBucket",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpeakingLiveKitDefaultMaxDurationSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SpeakingLiveKitEgressEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            // ── Speaking AI (Anthropic) ───────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "SpeakingAnthropicApiKeyEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            // ── Speaking ElevenLabs ───────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "SpeakingElevenLabsApiKeyEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            // ── Speaking AWS S3 ──────────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "SpeakingAwsAccessKeyId",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakingAwsSecretAccessKeyEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakingAwsRegion",
                table: "RuntimeSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakingAwsBucket",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // ── Speaking Compliance ──────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "SpeakingComplianceCurrentConsentVersion",
                table: "RuntimeSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakingComplianceCurrentLiveVideoConsentVersion",
                table: "RuntimeSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpeakingComplianceRetentionDaysDefault",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpeakingComplianceRetentionDaysWhenTutorReviewed",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpeakingComplianceAuditLogRetentionDays",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            // ── Speaking Feature Flag ────────────────────────────────────
            migrationBuilder.AddColumn<bool>(
                name: "SpeakingV2Enabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SpeakingLiveKitProvider", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingLiveKitApiKeyEncrypted", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingLiveKitApiSecretEncrypted", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingLiveKitWssUrl", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingLiveKitWebhookSigningSecretEncrypted", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingLiveKitEgressBucket", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingLiveKitDefaultMaxDurationSeconds", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingLiveKitEgressEnabled", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingAnthropicApiKeyEncrypted", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingElevenLabsApiKeyEncrypted", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingAwsAccessKeyId", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingAwsSecretAccessKeyEncrypted", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingAwsRegion", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingAwsBucket", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingComplianceCurrentConsentVersion", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingComplianceCurrentLiveVideoConsentVersion", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingComplianceRetentionDaysDefault", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingComplianceRetentionDaysWhenTutorReviewed", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingComplianceAuditLogRetentionDays", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingV2Enabled", table: "RuntimeSettings");
        }
    }
}
