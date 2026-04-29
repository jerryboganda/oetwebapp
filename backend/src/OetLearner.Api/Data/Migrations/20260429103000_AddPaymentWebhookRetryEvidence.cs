using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260429103000_AddPaymentWebhookRetryEvidence")]
    public partial class AddPaymentWebhookRetryEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "PaymentWebhookEvents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "GatewayTransactionId",
                table: "PaymentWebhookEvents",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAttemptedAt",
                table: "PaymentWebhookEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRetriedByAdminId",
                table: "PaymentWebhookEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRetriedByAdminName",
                table: "PaymentWebhookEvents",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRetriedAt",
                table: "PaymentWebhookEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedStatus",
                table: "PaymentWebhookEvents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParserVersion",
                table: "PaymentWebhookEvents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayloadSha256",
                table: "PaymentWebhookEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "PaymentWebhookEvents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VerifiedAt",
                table: "PaymentWebhookEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "PaymentWebhookEvents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_VerificationStatus_ProcessingStatus",
                table: "PaymentWebhookEvents",
                columns: new[] { "VerificationStatus", "ProcessingStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId_TransactionType_ReferenceType_ReferenceId",
                table: "WalletTransactions",
                columns: new[] { "WalletId", "TransactionType", "ReferenceType", "ReferenceId" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND ((\"TransactionType\" = 'top_up' AND \"ReferenceType\" = 'payment') OR (\"TransactionType\" = 'plan_grant' AND \"ReferenceType\" = 'subscription') OR (\"TransactionType\" = 'credit_purchase' AND \"ReferenceType\" = 'addon'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentWebhookEvents_VerificationStatus_ProcessingStatus",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_WalletTransactions_WalletId_TransactionType_ReferenceType_ReferenceId",
                table: "WalletTransactions");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "GatewayTransactionId",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "LastAttemptedAt",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "LastRetriedByAdminId",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "LastRetriedByAdminName",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "LastRetriedAt",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "NormalizedStatus",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "ParserVersion",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "PayloadSha256",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "PaymentWebhookEvents");
        }
    }
}