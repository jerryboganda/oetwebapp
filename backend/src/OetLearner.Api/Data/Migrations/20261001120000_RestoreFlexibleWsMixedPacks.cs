using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Master Catalogue products 30/31 (Quick Check, Exam Prep Pro) grant the
/// restricted Flexible W/S pool, not universal Shared credits. Migration
/// 20260925120000 incorrectly aligned them to shared_credits; this restores
/// the canonical flexible_credits entitlements and reclassifies remaining
/// balances and purchase-ledger deltas for those two packages so the W/S pool
/// can no longer be spent on Reading or Listening. Reading/Listening
/// allowances (3/3 and 6/6) and validity (30/90 days) are unchanged.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20261001120000_RestoreFlexibleWsMixedPacks")]
public partial class RestoreFlexibleWsMixedPacks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        SetAddOn(migrationBuilder, "pkg_quick_check", 30,
            """{"package_type":"full","flexible_credits":5,"listening_tests":3,"reading_tests":3}""");
        SetAddOn(migrationBuilder, "pkg_exam_prep_pro", 90,
            """{"package_type":"full","flexible_credits":15,"listening_tests":6,"reading_tests":6}""");

        migrationBuilder.Sql(
            """
            WITH conv AS (
                SELECT l."AccountId", SUM(l."SharedCredits") AS shared_to_move
                FROM "AiPackageCreditLots" l
                WHERE l."PackageId" IN ('pkg_quick_check', 'pkg_exam_prep_pro')
                  AND l."SharedCredits" <> 0
                GROUP BY l."AccountId"
            )
            UPDATE "AiPackageCreditAccounts" AS a
            SET "SharedCredits" = a."SharedCredits" - LEAST(a."SharedCredits", conv.shared_to_move),
                "FlexibleCredits" = a."FlexibleCredits" + LEAST(a."SharedCredits", conv.shared_to_move),
                "UpdatedAt" = now()
            FROM conv
            WHERE a."Id" = conv."AccountId";

            UPDATE "AiPackageCreditLots"
            SET "FlexibleCredits" = "FlexibleCredits" + "SharedCredits",
                "SharedCredits" = 0
            WHERE "PackageId" IN ('pkg_quick_check', 'pkg_exam_prep_pro')
              AND "SharedCredits" <> 0;

            UPDATE "AiPackageCreditTransactions"
            SET "FlexibleCreditsDelta" = "FlexibleCreditsDelta" + "SharedCreditsDelta",
                "SharedCreditsDelta" = 0
            WHERE "PackageId" IN ('pkg_quick_check', 'pkg_exam_prep_pro')
              AND "SharedCreditsDelta" <> 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Catalogue entitlements are canonical and must not be reversed.
    }

    private static void SetAddOn(MigrationBuilder migrationBuilder, string code, int durationDays, string json)
    {
        var escaped = json.Replace("'", "''", StringComparison.Ordinal);
        migrationBuilder.Sql($"""
UPDATE "BillingAddOns"
SET "GrantCredits" = 0,
    "DurationDays" = {durationDays},
    "GrantEntitlementsJson" = '{escaped}',
    "UpdatedAt" = now()
WHERE "Code" = '{code}';

UPDATE "BillingAddOnVersions" AS v
SET "GrantCredits" = 0,
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
