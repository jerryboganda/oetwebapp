using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Production JSON seeder is disabled. Live BillingAddOns still carried the old
/// OET Mastery flexible_credits=30 grant, so Access showed a generic remaining
/// balance instead of Writing/Speaking Unlimited. This writes the current
/// catalogue entitlements: skill-separated writing/speaking packs, exact
/// Quick Check / Exam Prep Pro pools, and Mastery unlimited grading.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260905100000_AlignAiPackageSkillEntitlements")]
public partial class AlignAiPackageSkillEntitlements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        SetAddOn(migrationBuilder, "pkg_quick_check", 5, 30,
            """{"package_type":"full","flexible_credits":5,"listening_tests":3,"reading_tests":3}""");
        SetAddOn(migrationBuilder, "pkg_exam_prep_pro", 15, 90,
            """{"package_type":"full","flexible_credits":15,"listening_tests":6,"reading_tests":6}""");
        SetAddOn(migrationBuilder, "pkg_oet_mastery", 0, 180,
            """{"package_type":"full","unlimited_grading":true,"listening_tests":null,"reading_tests":null,"priority_queue":true}""");
        SetAddOn(migrationBuilder, "pkg_writing_starter", 3, 30,
            """{"package_type":"writing","writing_only_credits":6,"writing_items":3,"listening_tests":0,"reading_tests":0}""");
        SetAddOn(migrationBuilder, "pkg_writing_standard", 8, 90,
            """{"package_type":"writing","writing_only_credits":16,"writing_items":8,"listening_tests":0,"reading_tests":0}""");
        SetAddOn(migrationBuilder, "pkg_writing_pro", 15, 180,
            """{"package_type":"writing","writing_only_credits":30,"writing_items":15,"listening_tests":0,"reading_tests":0}""");
        SetAddOn(migrationBuilder, "pkg_speaking_starter", 3, 30,
            """{"package_type":"speaking","speaking_only_credits":3,"speaking_items":3,"listening_tests":0,"reading_tests":0}""");
        SetAddOn(migrationBuilder, "pkg_speaking_standard", 8, 90,
            """{"package_type":"speaking","speaking_only_credits":8,"speaking_items":8,"listening_tests":0,"reading_tests":0}""");
        SetAddOn(migrationBuilder, "pkg_speaking_pro", 15, 180,
            """{"package_type":"speaking","speaking_only_credits":15,"speaking_items":15,"listening_tests":0,"reading_tests":0}""");
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
