using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Master Catalogue conformance (2026-08-23 spec):
/// 1. Splits the single flexible AI-credit pool into a universal
///    "SharedCredits" bucket (Full Course gift currency; Reading 1,
///    Listening 1, Writing 2, Speaking 2) and a restricted Flexible W/S
///    pool (Quick Check / Exam Prep Pro credits; Writing/Speaking only).
/// 2. Reclassifies existing balances by source package: course gifts and
///    admin grants become Shared; Quick Check / Exam Prep Pro purchases
///    remain restricted. Spend is conserved - at most the granted amount
///    from unrestricted sources moves.
/// 3. Corrects Writing package caps to the catalogue matrix
///    (Starter 3, Standard 8, Pro 15 writing-only submissions; the old
///    seeds granted double). Speaking grantCredits is zeroed so the JSON
///    speaking_only_credits value is the only authority.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260906090000_SplitSharedFlexibleWsAndExactCaps")]
public partial class SplitSharedFlexibleWsAndExactCaps : Migration
{
    private const string RestrictedPackageCodes = "('pkg_quick_check','pkg_exam_prep_pro')";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── Schema: universal Shared bucket on account + ledger ──
        migrationBuilder.Sql("""ALTER TABLE "AiPackageCreditAccounts" ADD COLUMN "SharedCredits" integer NOT NULL DEFAULT 0;""");
        migrationBuilder.Sql("""ALTER TABLE "AiPackageCreditTransactions" ADD COLUMN "SharedCreditsDelta" integer NOT NULL DEFAULT 0;""");

        // ── Reclassify existing balances by source package ──
        // shared_from_other = SUM(positive flex deltas from Purchase(0)/AdminAdjustment(4)
        // rows whose package is NOT a restricted W/S package).
        migrationBuilder.Sql($"""
UPDATE "AiPackageCreditAccounts" AS a
SET "SharedCredits" = LEAST(a."FlexibleCredits", GREATEST(COALESCE(agg.granted_other, 0), 0)),
    "UpdatedAt" = now()
FROM (
    SELECT t."AccountId" AS AccountId, SUM(t."FlexibleCreditsDelta") AS granted_other
    FROM "AiPackageCreditTransactions" AS t
    WHERE t."Reason" IN (0, 4)
      AND t."FlexibleCreditsDelta" > 0
      AND (t."PackageId" IS NULL OR t."PackageId" NOT IN {RestrictedPackageCodes})
    GROUP BY t."AccountId"
) AS agg
WHERE agg.AccountId = a."Id"
  AND a."FlexibleCredits" > 0;
""");

        migrationBuilder.Sql("""
UPDATE "AiPackageCreditAccounts"
SET "FlexibleCredits" = "FlexibleCredits" - "SharedCredits"
WHERE "SharedCredits" > 0;
""");

        // Ledger audit row per adjusted account so history explains the split.
        migrationBuilder.Sql("""
INSERT INTO "AiPackageCreditTransactions" (
    "Id", "UserId", "AccountId", "StripeSessionId", "PackageId", "PackageType",
    "SharedCreditsDelta", "FlexibleCreditsDelta", "WritingOnlyCreditsDelta", "SpeakingOnlyCreditsDelta",
    "ListeningTestsDelta", "ReadingTestsDelta", "MockExamsDelta",
    "Reason", "ReferenceId", "JobId", "Description", "ExpiresAt", "CreatedAt", "CreatedByAdminId")
SELECT 'aipkg-tx-' || replace(gen_random_uuid()::text, '-', ''),
       a."UserId", a."Id", NULL, NULL, 'full',
       a."SharedCredits", -a."SharedCredits", 0, 0,
       0, 0, 0,
       4, 'migration:split-shared-flexws:' || a."Id", NULL,
       'Gifted/admin credits moved to the universal Shared pool (restricted W/S separation).',
       NULL, now(), NULL
FROM "AiPackageCreditAccounts" AS a
WHERE a."SharedCredits" > 0;
""");

        // ── Exact catalogue caps: Writing 3/8/15 writing-only (was doubled),
        // Speaking/JSON stays authoritative with grantCredits zeroed ──
        SetAddOn(migrationBuilder, "pkg_writing_starter", 0, 30,
            """{"package_type":"writing","writing_only_credits":3,"writing_items":3,"listening_tests":0,"reading_tests":0}""");
        SetAddOn(migrationBuilder, "pkg_writing_standard", 0, 90,
            """{"package_type":"writing","writing_only_credits":8,"writing_items":8,"listening_tests":0,"reading_tests":0}""");
        SetAddOn(migrationBuilder, "pkg_writing_pro", 0, 180,
            """{"package_type":"writing","writing_only_credits":15,"writing_items":15,"listening_tests":0,"reading_tests":0}""");
        SetAddOn(migrationBuilder, "pkg_speaking_starter", 0, 30,
            """{"package_type":"speaking","speaking_only_credits":3,"speaking_items":3,"listening_tests":0,"reading_tests":0}""");
        SetAddOn(migrationBuilder, "pkg_speaking_standard", 0, 90,
            """{"package_type":"speaking","speaking_only_credits":8,"speaking_items":8,"listening_tests":0,"reading_tests":0}""");
        SetAddOn(migrationBuilder, "pkg_speaking_pro", 0, 180,
            """{"package_type":"speaking","speaking_only_credits":15,"speaking_items":15,"listening_tests":0,"reading_tests":0}""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Balances cannot be un-moved safely; drop only the new columns.
        migrationBuilder.Sql("""ALTER TABLE "AiPackageCreditTransactions" DROP COLUMN IF EXISTS "SharedCreditsDelta";""");
        migrationBuilder.Sql("""ALTER TABLE "AiPackageCreditAccounts" DROP COLUMN IF EXISTS "SharedCredits";""");
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
