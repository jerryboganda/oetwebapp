using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExamFamilyWalletAndWebhookSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentStreak",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastPracticeDate",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LongestStreak",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalPracticeMinutes",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalPracticeSessions",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WeeklyActivityJson",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExamFamilyCode",
                table: "StudyPlans",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamFamilyCode",
                table: "MockAttempts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamFamilyCode",
                table: "Goals",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamFamilyCode",
                table: "DiagnosticSessions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamFamilyCode",
                table: "ContentItems",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PerformanceMetricsJson",
                table: "ContentItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QaReviewedAt",
                table: "ContentItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QaReviewedBy",
                table: "ContentItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QaStatus",
                table: "ContentItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "ContentItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamFamilyCode",
                table: "Attempts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ExamFamilies",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScoringModel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SubtestConfigJson = table.Column<string>(type: "text", nullable: false),
                    CriteriaConfigJson = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamFamilies", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "PaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Gateway = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GatewayTransactionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ProductType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ProductId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Gateway = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GatewayEventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProcessingStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    BalanceAfter = table.Column<int>(type: "integer", nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ReferenceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_ExamFamilyCode",
                table: "ContentItems",
                column: "ExamFamilyCode");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_ExamFamilyCode",
                table: "Attempts",
                column: "ExamFamilyCode");

            migrationBuilder.CreateIndex(
                name: "IX_ExamFamilies_IsActive_SortOrder",
                table: "ExamFamilies",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_GatewayTransactionId",
                table: "PaymentTransactions",
                column: "GatewayTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_LearnerUserId",
                table: "PaymentTransactions",
                column: "LearnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_LearnerUserId_CreatedAt",
                table: "PaymentTransactions",
                columns: new[] { "LearnerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_GatewayEventId",
                table: "PaymentWebhookEvents",
                column: "GatewayEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_ProcessingStatus_ReceivedAt",
                table: "PaymentWebhookEvents",
                columns: new[] { "ProcessingStatus", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId",
                table: "WalletTransactions",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId_CreatedAt",
                table: "WalletTransactions",
                columns: new[] { "WalletId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamFamilies");

            migrationBuilder.DropTable(
                name: "PaymentTransactions");

            migrationBuilder.DropTable(
                name: "PaymentWebhookEvents");

            migrationBuilder.DropTable(
                name: "WalletTransactions");

            migrationBuilder.DropIndex(
                name: "IX_ContentItems_ExamFamilyCode",
                table: "ContentItems");

            migrationBuilder.DropIndex(
                name: "IX_Attempts_ExamFamilyCode",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "CurrentStreak",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastPracticeDate",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LongestStreak",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotalPracticeMinutes",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotalPracticeSessions",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WeeklyActivityJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ExamFamilyCode",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "ExamFamilyCode",
                table: "MockAttempts");

            migrationBuilder.DropColumn(
                name: "ExamFamilyCode",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "ExamFamilyCode",
                table: "DiagnosticSessions");

            migrationBuilder.DropColumn(
                name: "ExamFamilyCode",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "PerformanceMetricsJson",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "QaReviewedAt",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "QaReviewedBy",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "QaStatus",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "ExamFamilyCode",
                table: "Attempts");
        }
    }
}
