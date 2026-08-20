using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Adds Whop / Fawaterak runtime-settings columns and the admin payment-gateway
/// catalog. Credentials stay in RuntimeSettings; this table only stores ON/OFF,
/// order, and labels. Keep in sync with RuntimeSettingsSchemaSelfHeal.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260905090000_AddWhopFawaterakPaymentGateways")]
public partial class AddWhopFawaterakPaymentGateways : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "WhopApiBaseUrl" character varying(512);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "WhopApiKeyEncrypted" text;
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "WhopCompanyId" character varying(128);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "WhopWebhookSecretEncrypted" text;
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "WhopSuccessUrl" character varying(1024);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "WhopCancelUrl" character varying(1024);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "FawaterakApiBaseUrl" character varying(512);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "FawaterakHashApiKeyEncrypted" text;
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "FawaterakProviderKey" character varying(128);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "FawaterakSuccessUrl" character varying(1024);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "FawaterakFailUrl" character varying(1024);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "FawaterakPendingUrl" character varying(1024);

            CREATE TABLE IF NOT EXISTS "PaymentGatewayToggles" (
              "Id" character varying(64) NOT NULL,
              "Name" character varying(32) NOT NULL,
              "Label" character varying(128) NOT NULL,
              "CandidateLabel" character varying(128) NOT NULL,
              "Region" character varying(16) NOT NULL,
              "Mode" character varying(16) NOT NULL,
              "IconName" character varying(64) NOT NULL,
              "IsEnabled" boolean NOT NULL,
              "IsPrimary" boolean NOT NULL,
              "DisplayOrder" integer NOT NULL,
              "CreatedAt" timestamp with time zone NOT NULL,
              "UpdatedAt" timestamp with time zone NOT NULL,
              "UpdatedByAdminId" character varying(64),
              CONSTRAINT "PK_PaymentGatewayToggles" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PaymentGatewayToggles_Name" ON "PaymentGatewayToggles" ("Name");
            CREATE INDEX IF NOT EXISTS "IX_PaymentGatewayToggles_Region_IsEnabled_DisplayOrder" ON "PaymentGatewayToggles" ("Region", "IsEnabled", "DisplayOrder");

            INSERT INTO "PaymentGatewayToggles" (
              "Id", "Name", "Label", "CandidateLabel", "Region", "Mode", "IconName",
              "IsEnabled", "IsPrimary", "DisplayOrder", "CreatedAt", "UpdatedAt"
            )
            SELECT v."Id", v."Name", v."Label", v."CandidateLabel", v."Region", v."Mode", v."IconName",
                   v."IsEnabled", v."IsPrimary", v."DisplayOrder", NOW(), NOW()
            FROM (VALUES
              ('pgt-whop', 'whop', 'Whop', 'Pay with Whop — MAIN', 'global', 'embedded', 'credit-card', TRUE, TRUE, 10),
              ('pgt-fawaterak', 'fawaterak', 'Fawaterak', 'Pay with Fawaterak', 'global', 'iframe', 'wallet', TRUE, FALSE, 20),
              ('pgt-stripe', 'stripe', 'Stripe', 'Pay with Stripe', 'global', 'redirect', 'credit-card', FALSE, FALSE, 30),
              ('pgt-paypal', 'paypal', 'PayPal', 'Pay with PayPal', 'global', 'embedded', 'paypal', FALSE, FALSE, 40),
              ('pgt-checkoutcom', 'checkoutcom', 'Checkout.com', 'Pay with Checkout.com', 'egypt', 'redirect', 'credit-card', FALSE, FALSE, 45),
              ('pgt-paymob', 'paymob', 'Paymob', 'Pay with Paymob', 'egypt', 'redirect', 'wallet', FALSE, FALSE, 50),
              ('pgt-paytabs', 'paytabs', 'PayTabs', 'Pay with PayTabs', 'egypt', 'redirect', 'credit-card', FALSE, FALSE, 60),
              ('pgt-easykash', 'easykash', 'Easy Cash', 'Pay with Easy Cash', 'egypt', 'redirect', 'wallet', FALSE, FALSE, 70)
            ) AS v("Id", "Name", "Label", "CandidateLabel", "Region", "Mode", "IconName", "IsEnabled", "IsPrimary", "DisplayOrder")
            WHERE NOT EXISTS (
              SELECT 1 FROM "PaymentGatewayToggles" existing WHERE existing."Name" = v."Name"
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS "PaymentGatewayToggles";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "WhopApiBaseUrl";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "WhopApiKeyEncrypted";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "WhopCompanyId";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "WhopWebhookSecretEncrypted";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "WhopSuccessUrl";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "WhopCancelUrl";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "FawaterakApiBaseUrl";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "FawaterakHashApiKeyEncrypted";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "FawaterakProviderKey";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "FawaterakSuccessUrl";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "FawaterakFailUrl";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "FawaterakPendingUrl";
            """);
    }
}
