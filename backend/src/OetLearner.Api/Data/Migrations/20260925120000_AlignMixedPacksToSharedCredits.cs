using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Mixed packs (Quick Check / Exam Prep Pro) grant Shared AI credits, not
/// Flexible Writing/Speaking. Production JSON seeder is disabled, so live
/// BillingAddOns and leftover Flexible balances from those packs are aligned
/// here. Writing 3/8/15 was already applied by SplitSharedAndCreditLots.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260925120000_AlignMixedPacksToSharedCredits")]
public partial class AlignMixedPacksToSharedCredits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        SetAddOn(migrationBuilder, "pkg_quick_check", 5, 30,
            """{"package_type":"full","shared_credits":5,"listening_tests":3,"reading_tests":3}""");
        SetAddOn(migrationBuilder, "pkg_exam_prep_pro", 15, 90,
            """{"package_type":"full","shared_credits":15,"listening_tests":6,"reading_tests":6}""");
        SetAddOn(migrationBuilder, "pkg_writing_starter", 3, 30,
            """{"package_type":"writing","writing_only_credits":3,"writing_items":3,"listening_tests":0,"reading_tests":0}""");
        SetAddOn(migrationBuilder, "pkg_writing_standard", 8, 90,
            """{"package_type":"writing","writing_only_credits":8,"writing_items":8,"listening_tests":0,"reading_tests":0}""");
        SetAddOn(migrationBuilder, "pkg_writing_pro", 15, 180,
            """{"package_type":"writing","writing_only_credits":15,"writing_items":15,"listening_tests":0,"reading_tests":0}""");

        migrationBuilder.Sql(
            """
            UPDATE "AiPackageCreditTransactions"
            SET "SharedCreditsDelta" = "SharedCreditsDelta" + "FlexibleCreditsDelta",
                "FlexibleCreditsDelta" = 0
            WHERE "PackageId" IN ('pkg_quick_check', 'pkg_exam_prep_pro')
              AND "FlexibleCreditsDelta" <> 0;

            WITH mixed AS (
                SELECT t."UserId",
                       GREATEST(0, SUM(t."FlexibleCreditsDelta")) AS leftover_flex
                FROM "AiPackageCreditTransactions" t
                WHERE t."PackageId" IN ('pkg_quick_check', 'pkg_exam_prep_pro')
                GROUP BY t."UserId"
            )
            UPDATE "AiPackageCreditAccounts" AS a
            SET "SharedCredits" = a."SharedCredits" + LEAST(a."FlexibleCredits", mixed.leftover_flex),
                "FlexibleCredits" = a."FlexibleCredits" - LEAST(a."FlexibleCredits", mixed.leftover_flex)
            FROM mixed
            WHERE a."UserId" = mixed."UserId"
              AND mixed.leftover_flex > 0
              AND a."FlexibleCredits" > 0;

            UPDATE "AiPackageCreditLots"
            SET "SharedCredits" = "SharedCredits" + "FlexibleCredits",
                "FlexibleCredits" = 0
            WHERE "PackageId" IN ('pkg_quick_check', 'pkg_exam_prep_pro')
              AND "FlexibleCredits" <> 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Catalogue entitlements are canonical and must not be reversed.
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
