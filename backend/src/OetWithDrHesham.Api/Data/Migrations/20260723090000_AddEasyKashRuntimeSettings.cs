using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Adds admin-configurable runtime-settings columns for the EasyKash payment
    /// gateway (Egypt hosted Direct-Pay) so its API key, HMAC secret, payment
    /// methods and currency mode can be managed from the admin UI (DB override
    /// with env fallback) without a redeploy. All columns are nullable — zero
    /// behaviour change until an admin writes a value. Hand-written idempotent
    /// SQL (house style); mirrors the boot-time self-heal DDL.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260723090000_AddEasyKashRuntimeSettings")]
    public partial class AddEasyKashRuntimeSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""EasyKashApiBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""EasyKashApiKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""EasyKashHmacSecretEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""EasyKashPaymentOptionsCsv"" character varying(256);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""EasyKashCurrencyMode"" character varying(32);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""EasyKashSuccessUrl"" character varying(1024);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""EasyKashCancelUrl"" character varying(1024);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""EasyKashApiBaseUrl"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""EasyKashApiKeyEncrypted"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""EasyKashHmacSecretEncrypted"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""EasyKashPaymentOptionsCsv"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""EasyKashCurrencyMode"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""EasyKashSuccessUrl"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""EasyKashCancelUrl"";
");
        }
    }
}
