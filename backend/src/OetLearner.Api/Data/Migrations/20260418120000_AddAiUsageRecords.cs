using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Slice 1 of the AI Usage Management subsystem. Creates the single new
    /// table <c>AiUsageRecords</c> that backs every audit, analytics, quota,
    /// and cost-accounting surface in later slices.
    ///
    /// See <c>docs/AI-USAGE-POLICY.md</c> for the policy model. See
    /// <c>AiEntities.cs</c> for the domain entity.
    ///
    /// NOTE: Hand-written on purpose, matching the project's established
    /// migration style (e.g. AddEscalationTables). The EF Core snapshot
    /// diff tool generates noisy, unsafe migrations here because a prior
    /// migration's Designer file was stubbed; ops to fix that are tracked
    /// separately and must not be mixed into this slice.
    /// </remarks>
    public partial class AddAiUsageRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiUsageRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    KeySource = table.Column<int>(type: "integer", nullable: false),
                    RulebookVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PromptTemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SystemPromptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserPromptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: false),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: false),
                    CostEstimateUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    PolicyTrace = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodMonthKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PeriodDayKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiUsageRecords_ApplicationUserAccounts_AuthAccountId",
                        column: x => x.AuthAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_UserId_CreatedAt",
                table: "AiUsageRecords",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_FeatureCode_CreatedAt",
                table: "AiUsageRecords",
                columns: new[] { "FeatureCode", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_ProviderId_CreatedAt",
                table: "AiUsageRecords",
                columns: new[] { "ProviderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_CreatedAt",
                table: "AiUsageRecords",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_AuthAccountId",
                table: "AiUsageRecords",
                column: "AuthAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_PeriodMonthKey_UserId",
                table: "AiUsageRecords",
                columns: new[] { "PeriodMonthKey", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_PeriodDayKey_UserId",
                table: "AiUsageRecords",
                columns: new[] { "PeriodDayKey", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiUsageRecords");
        }
    }
}
