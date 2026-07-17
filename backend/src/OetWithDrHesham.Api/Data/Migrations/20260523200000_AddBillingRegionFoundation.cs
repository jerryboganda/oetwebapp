using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
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
            // 2026-05-28 — fully idempotent rewrite. In some environments this
            // schema was created out-of-band (EnsureCreated) without recording
            // the migration, so a plain AddColumn/CreateTable would collide on
            // re-run. Every statement is now IF NOT EXISTS / ON CONFLICT, so the
            // migration is a no-op where the schema already exists (existing
            // local/dev DBs) and a full create on a fresh/production database.
            // Column types, PK names and index names match the original EF
            // scaffold exactly so the model snapshot stays consistent.
            migrationBuilder.Sql(@"
                ALTER TABLE ""ApplicationUserAccounts"" ADD COLUMN IF NOT EXISTS ""Country"" character varying(2);
                ALTER TABLE ""ApplicationUserAccounts"" ADD COLUMN IF NOT EXISTS ""PreferredCurrency"" character varying(3);
                ALTER TABLE ""ApplicationUserAccounts"" ADD COLUMN IF NOT EXISTS ""PreferredRegion"" character varying(16);

                CREATE TABLE IF NOT EXISTS ""RegionPricings"" (
                    ""Id"" character varying(64) NOT NULL,
                    ""TargetType"" character varying(32) NOT NULL,
                    ""TargetId"" character varying(64) NOT NULL,
                    ""Region"" character varying(16) NOT NULL,
                    ""Currency"" character varying(8) NOT NULL,
                    ""PriceAmount"" numeric(12,2) NOT NULL,
                    ""IsActive"" boolean NOT NULL DEFAULT TRUE,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    ""CreatedByAdminId"" character varying(64) NULL,
                    ""UpdatedByAdminId"" character varying(64) NULL,
                    CONSTRAINT ""PK_RegionPricings"" PRIMARY KEY (""Id"")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RegionPricings_TargetType_TargetId_Region"" ON ""RegionPricings"" (""TargetType"", ""TargetId"", ""Region"");
                CREATE INDEX IF NOT EXISTS ""IX_RegionPricings_Region_IsActive"" ON ""RegionPricings"" (""Region"", ""IsActive"");

                CREATE TABLE IF NOT EXISTS ""GatewayRoutingConfigs"" (
                    ""Id"" character varying(64) NOT NULL,
                    ""Region"" character varying(16) NOT NULL,
                    ""Currency"" character varying(8) NOT NULL,
                    ""ProductType"" character varying(32) NOT NULL,
                    ""GatewayName"" character varying(32) NOT NULL,
                    ""Priority"" integer NOT NULL,
                    ""IsEnabled"" boolean NOT NULL DEFAULT TRUE,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedByAdminId"" character varying(64) NULL,
                    CONSTRAINT ""PK_GatewayRoutingConfigs"" PRIMARY KEY (""Id"")
                );
                CREATE INDEX IF NOT EXISTS ""IX_GatewayRoutingConfigs_Region_Currency_ProductType_Priority"" ON ""GatewayRoutingConfigs"" (""Region"", ""Currency"", ""ProductType"", ""Priority"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_GatewayRoutingConfigs_Region_Currency_ProductType_GatewayName"" ON ""GatewayRoutingConfigs"" (""Region"", ""Currency"", ""ProductType"", ""GatewayName"");

                INSERT INTO ""GatewayRoutingConfigs"" (""Id"", ""Region"", ""Currency"", ""ProductType"", ""GatewayName"", ""Priority"", ""IsEnabled"", ""CreatedAt"", ""UpdatedAt"")
                VALUES
                  ('seed_row_any_stripe',  'ROW', '*', '*', 'stripe', 10, TRUE, TIMESTAMP '2026-05-22 00:00:00+00', TIMESTAMP '2026-05-22 00:00:00+00'),
                  ('seed_row_any_paypal',  'ROW', '*', '*', 'paypal', 20, TRUE, TIMESTAMP '2026-05-22 00:00:00+00', TIMESTAMP '2026-05-22 00:00:00+00'),
                  ('seed_uk_any_stripe',   'UK',  '*', '*', 'stripe', 10, TRUE, TIMESTAMP '2026-05-22 00:00:00+00', TIMESTAMP '2026-05-22 00:00:00+00'),
                  ('seed_uk_any_paypal',   'UK',  '*', '*', 'paypal', 20, TRUE, TIMESTAMP '2026-05-22 00:00:00+00', TIMESTAMP '2026-05-22 00:00:00+00')
                ON CONFLICT (""Id"") DO NOTHING;
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
