using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260517120000_AddRuntimeSettingsOperationalOverrides")]
    public partial class AddRuntimeSettingsOperationalOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BrevoEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrevoAdminInviteTemplateId",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrevoMfaEnabledTemplateId",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrevoPasswordChangedTemplateId",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrevoReviewCompletedTemplateId",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrevoSecurityAlertTemplateId",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrevoWelcomeTemplateId",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmtpEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmtpEnableSsl",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GoogleEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FacebookEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LinkedInEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedInClientId",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedInClientSecretEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WebPushEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebPushSubject",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebPushPublicKey",
                table: "RuntimeSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebPushPrivateKeyEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BrevoEnabled", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "BrevoAdminInviteTemplateId", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "BrevoMfaEnabledTemplateId", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "BrevoPasswordChangedTemplateId", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "BrevoReviewCompletedTemplateId", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "BrevoSecurityAlertTemplateId", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "BrevoWelcomeTemplateId", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SmtpEnabled", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SmtpEnableSsl", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "GoogleEnabled", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "FacebookEnabled", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "LinkedInEnabled", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "LinkedInClientId", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "LinkedInClientSecretEncrypted", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "WebPushEnabled", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "WebPushSubject", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "WebPushPublicKey", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "WebPushPrivateKeyEncrypted", table: "RuntimeSettings");
        }
    }
}
