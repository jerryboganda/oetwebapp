using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <inheritdoc />
public partial class AddRefundDispute : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OrderRefunds",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                PaymentTransactionId = table.Column<string>(maxLength: 256, nullable: false),
                LearnerUserId = table.Column<string>(maxLength: 64, nullable: false),
                Gateway = table.Column<string>(maxLength: 16, nullable: false),
                GatewayRefundId = table.Column<string>(maxLength: 256, nullable: false),
                IdempotencyKey = table.Column<string>(maxLength: 64, nullable: false),
                RefundType = table.Column<string>(maxLength: 16, nullable: false, defaultValue: "partial"),
                Amount = table.Column<decimal>(nullable: false),
                Currency = table.Column<string>(maxLength: 8, nullable: false, defaultValue: "AUD"),
                Status = table.Column<string>(maxLength: 32, nullable: false, defaultValue: "pending"),
                Reason = table.Column<string>(maxLength: 64, nullable: true),
                AdminNote = table.Column<string>(maxLength: 1024, nullable: true),
                RequestedByAdminId = table.Column<string>(maxLength: 64, nullable: true),
                RequestedByAdminName = table.Column<string>(maxLength: 128, nullable: true),
                ReversedWalletCredits = table.Column<bool>(nullable: false, defaultValue: false),
                ReversedEntitlements = table.Column<bool>(nullable: false, defaultValue: false),
                CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_OrderRefunds", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_OrderRefunds_PaymentTransactionId",
            table: "OrderRefunds",
            column: "PaymentTransactionId");

        migrationBuilder.CreateIndex(
            name: "IX_OrderRefunds_Gateway_GatewayRefundId",
            table: "OrderRefunds",
            columns: new[] { "Gateway", "GatewayRefundId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrderRefunds_IdempotencyKey",
            table: "OrderRefunds",
            column: "IdempotencyKey",
            unique: true);

        migrationBuilder.CreateTable(
            name: "PaymentDisputes",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                PaymentTransactionId = table.Column<string>(maxLength: 256, nullable: false),
                LearnerUserId = table.Column<string>(maxLength: 64, nullable: false),
                SubscriptionId = table.Column<string>(maxLength: 64, nullable: true),
                Gateway = table.Column<string>(maxLength: 16, nullable: false),
                GatewayDisputeId = table.Column<string>(maxLength: 256, nullable: false),
                Status = table.Column<string>(maxLength: 32, nullable: false, defaultValue: "opened"),
                Reason = table.Column<string>(maxLength: 64, nullable: true),
                AmountDisputed = table.Column<decimal>(nullable: false),
                Currency = table.Column<string>(maxLength: 8, nullable: false, defaultValue: "AUD"),
                EntitlementsFrozen = table.Column<bool>(nullable: false, defaultValue: false),
                OpenedAt = table.Column<DateTimeOffset>(nullable: false),
                FundsWithdrawnAt = table.Column<DateTimeOffset>(nullable: true),
                ResolvedAt = table.Column<DateTimeOffset>(nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PaymentDisputes", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_PaymentDisputes_PaymentTransactionId",
            table: "PaymentDisputes",
            column: "PaymentTransactionId");

        migrationBuilder.CreateIndex(
            name: "IX_PaymentDisputes_Gateway_GatewayDisputeId",
            table: "PaymentDisputes",
            columns: new[] { "Gateway", "GatewayDisputeId" },
            unique: true);

        // Dead-letter status surface is reused via the existing PaymentWebhookEvents.ProcessingStatus
        // string column (no schema change required) and surfaced through the
        // /v1/admin/billing/provider-lifecycle-signals endpoint already wired by AdminService.
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "OrderRefunds");
        migrationBuilder.DropTable(name: "PaymentDisputes");
    }
}
