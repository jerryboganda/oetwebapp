using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Phase 1 of the international billing expansion (UK, Gulf, Egypt). Adds:
    ///
    /// <list type="bullet">
    ///   <item>Nullable region columns on <c>ApplicationUserAccounts</c> — <c>Country</c>, <c>PreferredCurrency</c>, <c>PreferredRegion</c>.</item>
    ///   <item><c>RegionPricings</c> table — per-region price overrides for plans, add-ons, wallet top-up tiers.</item>
    ///   <item><c>GatewayRoutingConfigs</c> table — drives <c>IGatewayRegistry</c> resolution (region, currency, product → gateway, priority).</item>
    ///   <item>Seed rows for the existing Stripe / PayPal gateways so existing checkout flows continue routing through the registry while new gateways come online.</item>
    /// </list>
    ///
    /// Hand-authored to avoid absorbing unrelated EF snapshot drift; follows the
    /// same pattern as <c>20260522100000_AddSpeakingDriftColumns</c>.
    ///
    /// 2026-05-28 — restored the missing [DbContext]/[Migration] attributes so
    /// EF's migration scanner recognises and applies this migration.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260523200000_AddBillingRegionFoundation")]
    public partial class AddBillingRegionFoundation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "ApplicationUserAccounts",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredCurrency",
                table: "ApplicationUserAccounts",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredRegion",
                table: "ApplicationUserAccounts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RegionPricings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PriceAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegionPricings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegionPricings_TargetType_TargetId_Region",
                table: "RegionPricings",
                columns: new[] { "TargetType", "TargetId", "Region" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegionPricings_Region_IsActive",
                table: "RegionPricings",
                columns: new[] { "Region", "IsActive" });

            migrationBuilder.CreateTable(
                name: "GatewayRoutingConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ProductType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GatewayName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayRoutingConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayRoutingConfigs_Region_Currency_ProductType_Priority",
                table: "GatewayRoutingConfigs",
                columns: new[] { "Region", "Currency", "ProductType", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayRoutingConfigs_Region_Currency_ProductType_GatewayName",
                table: "GatewayRoutingConfigs",
                columns: new[] { "Region", "Currency", "ProductType", "GatewayName" },
                unique: true);

            // Seed default ROW routes for the existing gateways so the registry
            // can answer ResolveAsync(ROW, *, *) immediately. Phase 2 will add
            // PayTabs / Paymob / Checkout.com seed rows as their adapters ship.
            var seedTimestamp = "TIMESTAMP '2026-05-22 00:00:00+00'";
            migrationBuilder.Sql($@"
                INSERT INTO ""GatewayRoutingConfigs"" (""Id"", ""Region"", ""Currency"", ""ProductType"", ""GatewayName"", ""Priority"", ""IsEnabled"", ""CreatedAt"", ""UpdatedAt"")
                VALUES
                  ('seed_row_any_stripe',  'ROW', '*', '*', 'stripe', 10, TRUE, {seedTimestamp}, {seedTimestamp}),
                  ('seed_row_any_paypal',  'ROW', '*', '*', 'paypal', 20, TRUE, {seedTimestamp}, {seedTimestamp}),
                  ('seed_uk_any_stripe',   'UK',  '*', '*', 'stripe', 10, TRUE, {seedTimestamp}, {seedTimestamp}),
                  ('seed_uk_any_paypal',   'UK',  '*', '*', 'paypal', 20, TRUE, {seedTimestamp}, {seedTimestamp});
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "GatewayRoutingConfigs");
            migrationBuilder.DropTable(name: "RegionPricings");

            migrationBuilder.DropColumn(name: "PreferredRegion", table: "ApplicationUserAccounts");
            migrationBuilder.DropColumn(name: "PreferredCurrency", table: "ApplicationUserAccounts");
            migrationBuilder.DropColumn(name: "Country", table: "ApplicationUserAccounts");
        }
    }
}
