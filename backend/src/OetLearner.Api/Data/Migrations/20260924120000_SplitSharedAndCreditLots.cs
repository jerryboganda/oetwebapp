using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Split Shared AI credits from Flexible Writing/Speaking, persist spend
/// allocations, and introduce per-package credit lots so one expired pack
/// cannot wipe another. Production JSON seeder is disabled — live catalogue
/// writing packs are also aligned to 1 unit per letter (3/8/15).
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260924120000_SplitSharedAndCreditLots")]
public partial class SplitSharedAndCreditLots : Migration
{
    private const string GiftPackageIds = """
        'full-condensed-medicine',
        'full-condensed-medicine-tbook',
        'full-physiotherapy',
        'full-allied-health',
        'full-nursing',
        'full-nursing-assessment',
        'full-nursing-premium',
        'full-pharmacy',
        'crash-course',
        'crash-3letters',
        'crash-5letters'
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "AiPackageCreditAccounts"
                ADD COLUMN IF NOT EXISTS "SharedCredits" integer NOT NULL DEFAULT 0;

            ALTER TABLE "AiPackageCreditTransactions"
                ADD COLUMN IF NOT EXISTS "SharedCreditsDelta" integer NOT NULL DEFAULT 0;

            ALTER TABLE "AiPackageCreditTransactions"
                ADD COLUMN IF NOT EXISTS "AllocationJson" character varying(4000);

            CREATE TABLE IF NOT EXISTS "AiPackageCreditLots" (
                "Id" character varying(64) NOT NULL,
                "UserId" character varying(64) NOT NULL,
                "AccountId" character varying(64) NOT NULL,
                "PackageId" character varying(64),
                "PackageType" character varying(32),
                "SharedCredits" integer NOT NULL DEFAULT 0,
                "FlexibleCredits" integer NOT NULL DEFAULT 0,
                "WritingOnlyCredits" integer NOT NULL DEFAULT 0,
                "SpeakingOnlyCredits" integer NOT NULL DEFAULT 0,
                "ListeningTestsRemaining" integer,
                "ReadingTestsRemaining" integer,
                "MockExamsRemaining" integer NOT NULL DEFAULT 0,
                "UnlimitedGrading" boolean NOT NULL DEFAULT FALSE,
                "UnlimitedListening" boolean NOT NULL DEFAULT FALSE,
                "UnlimitedReading" boolean NOT NULL DEFAULT FALSE,
                "ExpiresAt" timestamp with time zone,
                "SourceReferenceId" character varying(128),
                "CreatedAt" timestamp with time zone NOT NULL,
                "Expired" boolean NOT NULL DEFAULT FALSE,
                "ExpiredAt" timestamp with time zone,
                CONSTRAINT "PK_AiPackageCreditLots" PRIMARY KEY ("Id")
            );

            CREATE INDEX IF NOT EXISTS "IX_AiPackageCreditLots_UserId_Expired_ExpiresAt"
                ON "AiPackageCreditLots" ("UserId", "Expired", "ExpiresAt");

            CREATE INDEX IF NOT EXISTS "IX_AiPackageCreditLots_AccountId_Expired"
                ON "AiPackageCreditLots" ("AccountId", "Expired");

            CREATE INDEX IF NOT EXISTS "IX_AiPackageCreditLots_SourceReferenceId"
                ON "AiPackageCreditLots" ("SourceReferenceId");
            """);

        migrationBuilder.Sql($"""
            UPDATE "AiPackageCreditTransactions"
            SET "SharedCreditsDelta" = "FlexibleCreditsDelta",
                "FlexibleCreditsDelta" = 0
            WHERE "Reason" IN (0, 9)
              AND "PackageId" IN ({GiftPackageIds})
              AND "FlexibleCreditsDelta" <> 0
              AND "SharedCreditsDelta" = 0;

            WITH gift AS (
                SELECT t."UserId",
                       GREATEST(0, SUM(t."SharedCreditsDelta")) AS gift_shared
                FROM "AiPackageCreditTransactions" t
                WHERE t."Reason" IN (0, 9)
                  AND t."PackageId" IN ({GiftPackageIds})
                GROUP BY t."UserId"
            )
            UPDATE "AiPackageCreditAccounts" AS a
            SET "SharedCredits" = a."SharedCredits" + LEAST(a."FlexibleCredits", gift.gift_shared),
                "FlexibleCredits" = a."FlexibleCredits" - LEAST(a."FlexibleCredits", gift.gift_shared)
            FROM gift
            WHERE a."UserId" = gift."UserId"
              AND gift.gift_shared > 0
              AND a."FlexibleCredits" > 0;

            UPDATE "AiPackageCreditTransactions"
            SET "SharedCreditsDelta" = "FlexibleCreditsDelta",
                "FlexibleCreditsDelta" = 0
            WHERE "Reason" IN (0, 9)
              AND "FlexibleCreditsDelta" <> 0
              AND "SharedCreditsDelta" = 0
              AND COALESCE("PackageId", '') NOT IN ('pkg_quick_check', 'pkg_exam_prep_pro')
              AND COALESCE("PackageId", '') NOT LIKE 'pkg_oet_mastery%';

            WITH qc AS (
                SELECT t."UserId",
                       GREATEST(0, SUM(t."FlexibleCreditsDelta")) AS qc_flex
                FROM "AiPackageCreditTransactions" t
                WHERE t."Reason" IN (0, 9)
                  AND t."PackageId" IN ('pkg_quick_check', 'pkg_exam_prep_pro')
                GROUP BY t."UserId"
            )
            UPDATE "AiPackageCreditAccounts" AS a
            SET "SharedCredits" = a."SharedCredits" + GREATEST(0, a."FlexibleCredits" - COALESCE(qc.qc_flex, 0)),
                "FlexibleCredits" = LEAST(a."FlexibleCredits", COALESCE(qc.qc_flex, 0))
            FROM (SELECT DISTINCT "UserId" FROM "AiPackageCreditAccounts") AS users
            LEFT JOIN qc ON qc."UserId" = users."UserId"
            WHERE a."UserId" = users."UserId"
              AND a."FlexibleCredits" > COALESCE(qc.qc_flex, 0);

            WITH writing_legacy AS (
                SELECT DISTINCT t."UserId"
                FROM "AiPackageCreditTransactions" t
                WHERE t."Reason" = 0
                  AND t."WritingOnlyCreditsDelta" IN (6, 16, 30)
            )
            UPDATE "AiPackageCreditAccounts" AS a
            SET "WritingOnlyCredits" = a."WritingOnlyCredits" / 2
            FROM writing_legacy w
            WHERE a."UserId" = w."UserId"
              AND a."WritingOnlyCredits" > 1;

            INSERT INTO "AiPackageCreditLots" (
                "Id", "UserId", "AccountId", "PackageId", "PackageType",
                "SharedCredits", "FlexibleCredits", "WritingOnlyCredits", "SpeakingOnlyCredits",
                "ListeningTestsRemaining", "ReadingTestsRemaining", "MockExamsRemaining",
                "UnlimitedGrading", "UnlimitedListening", "UnlimitedReading",
                "ExpiresAt", "SourceReferenceId", "CreatedAt", "Expired", "ExpiredAt")
            SELECT
                LEFT('aipkg-lot-bf-' || a."Id", 64),
                a."UserId",
                a."Id",
                'legacy-backfill',
                'full',
                a."SharedCredits",
                a."FlexibleCredits",
                a."WritingOnlyCredits",
                a."SpeakingOnlyCredits",
                a."ListeningTestsRemaining",
                a."ReadingTestsRemaining",
                a."MockExamsRemaining",
                FALSE,
                a."ListeningTestsRemaining" IS NULL,
                a."ReadingTestsRemaining" IS NULL,
                a."ExpiresAt",
                LEFT('legacy-backfill:' || a."Id", 128),
                NOW(),
                FALSE,
                NULL
            FROM "AiPackageCreditAccounts" a
            WHERE NOT EXISTS (
                SELECT 1 FROM "AiPackageCreditLots" l WHERE l."AccountId" = a."Id")
              AND (
                    a."SharedCredits" <> 0
                 OR a."FlexibleCredits" <> 0
                 OR a."WritingOnlyCredits" <> 0
                 OR a."SpeakingOnlyCredits" <> 0
                 OR COALESCE(a."ListeningTestsRemaining", 1) <> 0
                 OR COALESCE(a."ReadingTestsRemaining", 1) <> 0
                 OR a."MockExamsRemaining" <> 0
                 OR a."ListeningTestsRemaining" IS NULL
                 OR a."ReadingTestsRemaining" IS NULL
              );
            """);

        SetAddOn(migrationBuilder, "pkg_quick_check", 5, 30,
            """{"package_type":"full","shared_credits":0,"flexible_credits":5,"listening_tests":3,"reading_tests":3}""");
        SetAddOn(migrationBuilder, "pkg_exam_prep_pro", 15, 90,
            """{"package_type":"full","shared_credits":0,"flexible_credits":15,"listening_tests":6,"reading_tests":6}""");
        SetAddOn(migrationBuilder, "pkg_writing_starter", 3, 30,
            """{"package_type":"writing","writing_only_credits":3,"writing_items":3,"listening_tests":0,"reading_tests":0}""");
        SetAddOn(migrationBuilder, "pkg_writing_standard", 8, 90,
            """{"package_type":"writing","writing_only_credits":8,"writing_items":8,"listening_tests":0,"reading_tests":0}""");
        SetAddOn(migrationBuilder, "pkg_writing_pro", 15, 180,
            """{"package_type":"writing","writing_only_credits":15,"writing_items":15,"listening_tests":0,"reading_tests":0}""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS "AiPackageCreditLots";
            ALTER TABLE "AiPackageCreditTransactions" DROP COLUMN IF EXISTS "AllocationJson";
            ALTER TABLE "AiPackageCreditTransactions" DROP COLUMN IF EXISTS "SharedCreditsDelta";
            ALTER TABLE "AiPackageCreditAccounts" DROP COLUMN IF EXISTS "SharedCredits";
            """);
    }

    private static void SetAddOn(MigrationBuilder migrationBuilder, string code, int grantCredits, int durationDays, string json)
    {
        var escaped = json.Replace("'", "''", StringComparison.Ordinal);
        migrationBuilder.Sql($"""
UPDATE "BillingAddOns"
SET "GrantCredits" = {grantCredits},
    "DurationDays" = {durationDays},
    "GrantEntitlementsJson" = '{escaped}',
    "UpdatedAt" = now()
WHERE "Code" = '{code}';

UPDATE "BillingAddOnVersions" AS v
SET "GrantCredits" = {grantCredits},
    "DurationDays" = {durationDays},
    "GrantEntitlementsJson" = '{escaped}'
FROM "BillingAddOns" AS a
WHERE v."AddOnId" = a."Id"
  AND a."Code" = '{code}'
  AND (
      v."Status" = 1
      OR v."Id" = a."ActiveVersionId"
      OR v."Id" = a."LatestVersionId"
  );
""");
    }
}
