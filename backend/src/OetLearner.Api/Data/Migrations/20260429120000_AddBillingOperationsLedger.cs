using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingOperationsLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingOperations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OperationType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CreditDelta = table.Column<int>(type: "integer", nullable: true),
                    PaymentTransactionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    InvoiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    QuoteId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Gateway = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    GatewayReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EvidenceUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    AdminNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ResolutionNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResolvedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingOperations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingOperations_GatewayReference",
                table: "BillingOperations",
                column: "GatewayReference");

            migrationBuilder.CreateIndex(
                name: "IX_BillingOperations_InvoiceId",
                table: "BillingOperations",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingOperations_OperationType_Status_CreatedAt",
                table: "BillingOperations",
                columns: new[] { "OperationType", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingOperations_PaymentTransactionId",
                table: "BillingOperations",
                column: "PaymentTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingOperations_UserId_Status_CreatedAt",
                table: "BillingOperations",
                columns: new[] { "UserId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingOperations");
        }
    }
}