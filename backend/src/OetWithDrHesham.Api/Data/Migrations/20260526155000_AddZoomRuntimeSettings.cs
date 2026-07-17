using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260526155000_AddZoomRuntimeSettings")]
    public partial class AddZoomRuntimeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LiveClassesAiRecordingProcessingEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerPortalConfigurationId",
                table: "RuntimeSettings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeRadarBlockEmailDomainsCsv",
                table: "RuntimeSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StripeRadarHighRiskCountryAllowReview",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StripeTaxAutomaticEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeTaxRegistrationsCsv",
                table: "RuntimeSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomAccountId",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ZoomAllowSandboxFallback",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomApiBaseUrl",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomClientId",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomClientSecretEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ZoomEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomHostUserId",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomMeetingSdkKey",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomMeetingSdkSecretEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomTokenUrl",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ZoomWebhookRetryToleranceSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomWebhookSecretTokenEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LiveClassesAiRecordingProcessingEnabled", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "StripeCustomerPortalConfigurationId", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "StripeRadarBlockEmailDomainsCsv", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "StripeRadarHighRiskCountryAllowReview", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "StripeTaxAutomaticEnabled", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "StripeTaxRegistrationsCsv", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "ZoomAccountId", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "ZoomAllowSandboxFallback", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "ZoomApiBaseUrl", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "ZoomClientId", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "ZoomClientSecretEncrypted", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "ZoomEnabled", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "ZoomHostUserId", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "ZoomMeetingSdkKey", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "ZoomMeetingSdkSecretEncrypted", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "ZoomTokenUrl", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "ZoomWebhookRetryToleranceSeconds", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "ZoomWebhookSecretTokenEncrypted", table: "RuntimeSettings");
        }
    }
}