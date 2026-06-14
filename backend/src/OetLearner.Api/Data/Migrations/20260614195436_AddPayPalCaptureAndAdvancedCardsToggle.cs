using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayPalCaptureAndAdvancedCardsToggle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DataRetentionAnalyticsEventsDays",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataRetentionAuditEventsDays",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataRetentionBatchSize",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataRetentionNotificationDeliveryAttemptsDays",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataRetentionPaymentWebhookEventsDays",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataRetentionPaymentWebhookPiiNullOutAgeDays",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataRetentionSweepIntervalHours",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpertAutoAssignmentBatchSize",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExpertAutoAssignmentEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpertAutoAssignmentLookbackHoursForLoad",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpertAutoAssignmentMaxActiveAssignmentsPerExpert",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpertAutoAssignmentPollingIntervalSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpertAutoAssignmentSlaEscalationIntervalSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpertAutoAssignmentSlaHoursExpress",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpertAutoAssignmentSlaHoursStandard",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordPolicyBreachApiBaseUrl",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PasswordPolicyBreachApiTimeoutSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PasswordPolicyBreachCheckEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PasswordPolicyMinimumLength",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PasswordPolicyRequireDigit",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PasswordPolicyRequireMixedCase",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PasswordPolicyRequireSymbol",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PayPalAdvancedCardsEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaptureId",
                table: "PaymentTransactions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataRetentionAnalyticsEventsDays",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "DataRetentionAuditEventsDays",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "DataRetentionBatchSize",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "DataRetentionNotificationDeliveryAttemptsDays",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "DataRetentionPaymentWebhookEventsDays",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "DataRetentionPaymentWebhookPiiNullOutAgeDays",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "DataRetentionSweepIntervalHours",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "ExpertAutoAssignmentBatchSize",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "ExpertAutoAssignmentEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "ExpertAutoAssignmentLookbackHoursForLoad",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "ExpertAutoAssignmentMaxActiveAssignmentsPerExpert",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "ExpertAutoAssignmentPollingIntervalSeconds",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "ExpertAutoAssignmentSlaEscalationIntervalSeconds",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "ExpertAutoAssignmentSlaHoursExpress",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "ExpertAutoAssignmentSlaHoursStandard",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PasswordPolicyBreachApiBaseUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PasswordPolicyBreachApiTimeoutSeconds",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PasswordPolicyBreachCheckEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PasswordPolicyMinimumLength",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PasswordPolicyRequireDigit",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PasswordPolicyRequireMixedCase",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PasswordPolicyRequireSymbol",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PayPalAdvancedCardsEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "CaptureId",
                table: "PaymentTransactions");
        }
    }
}
