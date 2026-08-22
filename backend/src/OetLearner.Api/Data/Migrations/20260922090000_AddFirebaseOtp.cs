using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Firebase Phone Auth is an SMS OTP transport only — never the login or
/// session authority. Adds runtime-settings columns and EmailOtpChallenge
/// delivery metadata. Keep in sync with RuntimeSettingsSchemaSelfHeal.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260922090000_AddFirebaseOtp")]
public partial class AddFirebaseOtp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "FirebaseOtpEnabled" boolean;
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "FirebaseOtpSmsEnabled" boolean;
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "FirebaseOtpEmailLinksEnabled" boolean;
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "FirebaseOtpFallbackToBrevo" boolean;
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "FirebaseOtpProjectId" character varying(128);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "FirebaseOtpAuthDomain" character varying(256);
            ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "FirebaseOtpWebApiKeyEncrypted" text;

            ALTER TABLE "EmailOtpChallenges" ADD COLUMN IF NOT EXISTS "Provider" character varying(32);
            ALTER TABLE "EmailOtpChallenges" ADD COLUMN IF NOT EXISTS "DeliveryChannel" character varying(16);
            ALTER TABLE "EmailOtpChallenges" ADD COLUMN IF NOT EXISTS "DestinationHint" character varying(64);
            ALTER TABLE "EmailOtpChallenges" ADD COLUMN IF NOT EXISTS "ExternalSessionInfoEncrypted" text;
            UPDATE "EmailOtpChallenges" SET "Provider" = COALESCE("Provider", 'brevo_email');
            UPDATE "EmailOtpChallenges" SET "DeliveryChannel" = COALESCE("DeliveryChannel", 'email');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "FirebaseOtpEnabled";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "FirebaseOtpSmsEnabled";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "FirebaseOtpEmailLinksEnabled";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "FirebaseOtpFallbackToBrevo";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "FirebaseOtpProjectId";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "FirebaseOtpAuthDomain";
            ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "FirebaseOtpWebApiKeyEncrypted";
            ALTER TABLE "EmailOtpChallenges" DROP COLUMN IF EXISTS "Provider";
            ALTER TABLE "EmailOtpChallenges" DROP COLUMN IF EXISTS "DeliveryChannel";
            ALTER TABLE "EmailOtpChallenges" DROP COLUMN IF EXISTS "DestinationHint";
            ALTER TABLE "EmailOtpChallenges" DROP COLUMN IF EXISTS "ExternalSessionInfoEncrypted";
            """);
    }
}
