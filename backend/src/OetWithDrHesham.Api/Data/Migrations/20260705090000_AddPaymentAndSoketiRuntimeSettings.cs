using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Adds admin-configurable runtime-settings columns for the CheckoutCom,
    /// Paymob, and PayTabs payment gateways and the Soketi realtime push server,
    /// so their keys can be rotated from the admin UI (DB override with env
    /// fallback) without a redeploy. All columns are nullable — zero behaviour
    /// change until an admin writes a value. Hand-written idempotent SQL
    /// (house style).
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260705090000_AddPaymentAndSoketiRuntimeSettings")]
    public partial class AddPaymentAndSoketiRuntimeSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CheckoutComApiBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CheckoutComSecretKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CheckoutComPublicKey"" character varying(256);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CheckoutComProcessingChannelId"" character varying(128);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CheckoutComWebhookSecretEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CheckoutComSuccessUrl"" character varying(1024);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CheckoutComCancelUrl"" character varying(1024);

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobApiBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobApiKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobMerchantId"" character varying(128);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobHmacSecretEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobIntegrationIdsJson"" character varying(1024);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobIframeId"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobSuccessUrl"" character varying(1024);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobCancelUrl"" character varying(1024);

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PayTabsApiBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PayTabsServerKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PayTabsProfileId"" character varying(128);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PayTabsWebhookSecretEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PayTabsSuccessUrl"" character varying(1024);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PayTabsCancelUrl"" character varying(1024);

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SoketiHost"" character varying(256);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SoketiPort"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SoketiAppId"" character varying(128);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SoketiAppKey"" character varying(256);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SoketiAppSecretEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SoketiUseTls"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SoketiEnabled"" boolean;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""CheckoutComApiBaseUrl"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""CheckoutComSecretKeyEncrypted"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""CheckoutComPublicKey"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""CheckoutComProcessingChannelId"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""CheckoutComWebhookSecretEncrypted"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""CheckoutComSuccessUrl"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""CheckoutComCancelUrl"";

ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""PaymobApiBaseUrl"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""PaymobApiKeyEncrypted"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""PaymobMerchantId"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""PaymobHmacSecretEncrypted"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""PaymobIntegrationIdsJson"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""PaymobIframeId"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""PaymobSuccessUrl"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""PaymobCancelUrl"";

ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""PayTabsApiBaseUrl"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""PayTabsServerKeyEncrypted"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""PayTabsProfileId"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""PayTabsWebhookSecretEncrypted"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""PayTabsSuccessUrl"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""PayTabsCancelUrl"";

ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""SoketiHost"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""SoketiPort"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""SoketiAppId"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""SoketiAppKey"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""SoketiAppSecretEncrypted"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""SoketiUseTls"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""SoketiEnabled"";
");
        }
    }
}
