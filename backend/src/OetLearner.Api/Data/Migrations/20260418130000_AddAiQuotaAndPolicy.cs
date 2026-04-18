using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Slice 2 of the AI Usage Management subsystem. Adds quota plans,
    /// per-user counters, per-user overrides, and the singleton global
    /// policy row. See <c>docs/AI-USAGE-POLICY.md</c> §3, §4, §7 for the
    /// policy model.
    /// </remarks>
    public partial class AddAiQuotaAndPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── AiQuotaPlans ────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "AiQuotaPlans",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Period = table.Column<int>(type: "integer", nullable: false),
                    MonthlyTokenCap = table.Column<int>(type: "integer", nullable: false),
                    DailyTokenCap = table.Column<int>(type: "integer", nullable: false),
                    MaxConcurrentRequests = table.Column<int>(type: "integer", nullable: false),
                    RolloverPolicy = table.Column<int>(type: "integer", nullable: false),
                    RolloverCapPct = table.Column<int>(type: "integer", nullable: false),
                    OveragePolicy = table.Column<int>(type: "integer", nullable: false),
                    OverageRatePer1kTokens = table.Column<decimal>(type: "numeric", nullable: true),
                    AutoUpgradeTargetPlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DegradeModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AllowedFeaturesCsv = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    AllowedModelsCsv = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiQuotaPlans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiQuotaPlans_Code",
                table: "AiQuotaPlans",
                column: "Code",
                unique: true);

            // ── AiQuotaCounters ─────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "AiQuotaCounters",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PeriodKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TokensUsed = table.Column<int>(type: "integer", nullable: false),
                    RequestsCount = table.Column<int>(type: "integer", nullable: false),
                    CostAccumulatedUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    LastUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiQuotaCounters", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiQuotaCounters_UserId_PeriodKey",
                table: "AiQuotaCounters",
                columns: new[] { "UserId", "PeriodKey" },
                unique: true);

            // ── AiUserQuotaOverrides ────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "AiUserQuotaOverrides",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MonthlyTokenCapOverride = table.Column<int>(type: "integer", nullable: true),
                    DailyTokenCapOverride = table.Column<int>(type: "integer", nullable: true),
                    ForcePlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AiDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    GrantedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUserQuotaOverrides", x => x.UserId);
                });

            // ── AiGlobalPolicies (singleton) ─────────────────────────────
            migrationBuilder.CreateTable(
                name: "AiGlobalPolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    KillSwitchEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    KillSwitchScope = table.Column<int>(type: "integer", nullable: false),
                    KillSwitchReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MonthlyBudgetUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    SoftWarnPct = table.Column<int>(type: "integer", nullable: false),
                    HardKillPct = table.Column<int>(type: "integer", nullable: false),
                    CurrentSpendUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    AllowByokOnScoringFeatures = table.Column<bool>(type: "boolean", nullable: false),
                    AllowByokOnNonScoringFeatures = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultPlatformProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ByokErrorCooldownHours = table.Column<int>(type: "integer", nullable: false),
                    ByokTransientRetryCount = table.Column<int>(type: "integer", nullable: false),
                    AnomalyDetectionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AnomalyMultiplierX = table.Column<decimal>(type: "numeric", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGlobalPolicies", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiGlobalPolicies");
            migrationBuilder.DropTable(name: "AiUserQuotaOverrides");
            migrationBuilder.DropTable(name: "AiQuotaCounters");
            migrationBuilder.DropTable(name: "AiQuotaPlans");
        }
    }
}
