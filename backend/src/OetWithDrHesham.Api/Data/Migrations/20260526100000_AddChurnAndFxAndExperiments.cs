using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// AI churn prediction, AI usage forecast, FX rates, and pricing experiments.
    /// Hand-authored to avoid absorbing EF snapshot drift; mirrors the pattern
    /// used by 20260524100000_AddBillingFullExpansion.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260526100000_AddChurnAndFxAndExperiments")]
    public partial class AddChurnAndFxAndExperiments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChurnRiskSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RiskScore = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    RiskBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "low"),
                    FactorsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false, defaultValue: "{}"),
                    RecommendedAction = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActionDispatched = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_ChurnRiskSnapshots", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_ChurnRiskSnapshots_UserId_SnapshotDate", table: "ChurnRiskSnapshots", columns: new[] { "UserId", "SnapshotDate" });
            migrationBuilder.CreateIndex(name: "IX_ChurnRiskSnapshots_SnapshotDate_RiskBand", table: "ChurnRiskSnapshots", columns: new[] { "SnapshotDate", "RiskBand" });
            migrationBuilder.CreateIndex(name: "IX_ChurnRiskSnapshots_RiskBand_RiskScore", table: "ChurnRiskSnapshots", columns: new[] { "RiskBand", "RiskScore" });

            migrationBuilder.CreateTable(
                name: "UsageForecastSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "*"),
                    WindowDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    ForecastCalls = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ForecastCredits = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ForecastCostUsd = table.Column<decimal>(type: "numeric(12,4)", nullable: false, defaultValue: 0m),
                    Ema30DailyCalls = table.Column<decimal>(type: "numeric(12,3)", nullable: false, defaultValue: 0m),
                    PerFeatureJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    SuggestedTopUpCredits = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_UsageForecastSnapshots", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_UsageForecastSnapshots_UserId_SnapshotDate", table: "UsageForecastSnapshots", columns: new[] { "UserId", "SnapshotDate" });
            migrationBuilder.CreateIndex(name: "IX_UsageForecastSnapshots_FeatureCode_SnapshotDate", table: "UsageForecastSnapshots", columns: new[] { "FeatureCode", "SnapshotDate" });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FromCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ToCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "manual"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_ExchangeRates", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_ExchangeRates_From_To_EffectiveFrom", table: "ExchangeRates", columns: new[] { "FromCurrency", "ToCurrency", "EffectiveFrom" });
            migrationBuilder.CreateIndex(name: "IX_ExchangeRates_EffectiveFrom", table: "ExchangeRates", column: "EffectiveFrom");

            migrationBuilder.CreateTable(
                name: "PricingExperiments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "*"),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "draft"),
                    RolloutPercent = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    VariantsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false, defaultValue: "[]"),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                },
                constraints: table => table.PrimaryKey("PK_PricingExperiments", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_PricingExperiments_Status", table: "PricingExperiments", column: "Status");
            migrationBuilder.CreateIndex(name: "IX_PricingExperiments_TargetType_TargetId", table: "PricingExperiments", columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateTable(
                name: "PricingExperimentAssignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExperimentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VariantCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Converted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ConvertedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConvertedAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_PricingExperimentAssignments", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_PricingExperimentAssignments_ExperimentId_UserId", table: "PricingExperimentAssignments", columns: new[] { "ExperimentId", "UserId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_PricingExperimentAssignments_ExperimentId_VariantCode", table: "PricingExperimentAssignments", columns: new[] { "ExperimentId", "VariantCode" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PricingExperimentAssignments");
            migrationBuilder.DropTable(name: "PricingExperiments");
            migrationBuilder.DropTable(name: "ExchangeRates");
            migrationBuilder.DropTable(name: "UsageForecastSnapshots");
            migrationBuilder.DropTable(name: "ChurnRiskSnapshots");
        }
    }
}
