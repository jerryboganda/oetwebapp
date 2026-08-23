using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Dedicated From addresses per mail lane plus OTP delivery-status
/// columns. Keep in sync with RuntimeSettingsSchemaSelfHeal.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260923120000_AddEmailLaneSendersAndOtpDelivery")]
public partial class AddEmailLaneSendersAndOtpDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "AuthFromAddress" character varying(256);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "AuthFromName" character varying(256);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "MarketingFromAddress" character varying(256);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "MarketingFromName" character varying(256);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "ProductFromAddress" character varying(256);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "ProductFromName" character varying(256);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "SupportFromAddress" character varying(256);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "SupportFromName" character varying(256);

            ALTER TABLE "EmailOtpChallenges" ADD COLUMN IF NOT EXISTS "DeliveryStatus" character varying(32);
            ALTER TABLE "EmailOtpChallenges" ADD COLUMN IF NOT EXISTS "DeliveryReason" character varying(512);
            ALTER TABLE "EmailOtpChallenges" ADD COLUMN IF NOT EXISTS "DeliveryUpdatedAt" timestamp with time zone;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "AuthFromAddress";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "AuthFromName";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "MarketingFromAddress";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "MarketingFromName";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "ProductFromAddress";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "ProductFromName";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "SupportFromAddress";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "SupportFromName";
            ALTER TABLE "EmailOtpChallenges" DROP COLUMN IF EXISTS "DeliveryStatus";
            ALTER TABLE "EmailOtpChallenges" DROP COLUMN IF EXISTS "DeliveryReason";
            ALTER TABLE "EmailOtpChallenges" DROP COLUMN IF EXISTS "DeliveryUpdatedAt";
            """);
    }
}
