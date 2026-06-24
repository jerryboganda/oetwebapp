using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260712090000_DropFullPackageMocksAndUnlimitedLr")]
    public partial class DropFullPackageMocksAndUnlimitedLr : Migration
    {
        // Reworks the three "full" AI grading packages so they no longer advertise
        // or grant entitlements they shouldn't:
        //
        //   pkg_quick_check    drop the implicit "Unlimited L&R" perk (no mocks already)
        //   pkg_exam_prep_pro  drop "Unlimited L&R" AND the 2 bundled full mocks
        //   pkg_oet_mastery    drop the 5 bundled full mocks (KEEP unlimited L&R)
        //
        // Source of truth: BillingAddOn.GrantEntitlementsJson. It drives the AI-package
        // storefront display (LearnerService.GetAiPackagesAsync -> ReadAiPackageExtras),
        // the one-time purchase grant (AiPackageCreditService.AiPackageGrant.FromAddOn) and
        // the computed mock balance (MockEntitlementService). The readers treat an
        // absent/null listening_tests/reading_tests as UNLIMITED and a numeric 0 as
        // "none included"; mocks come from mock_exams ?? mockFull. So removing the
        // mockFull key zeroes mocks, and setting listening_tests/reading_tests to 0
        // removes the unlimited perk (Quick Check & Exam Prep Pro only).
        //
        // The boot/admin reseeder (Oet2026CatalogSeeder) is DISABLED in production, so the
        // JSON manifest edit alone does not touch an existing prod database. This migration
        // writes exactly what the (now-updated) seeder would produce, so a future reseed
        // converges to the same rows. BillingAddOnVersions mirrors the live row (kept in
        // sync by the seeder's CopyAddOnIntoVersion) and is updated for consistency.
        //
        // GrantCredits columns (5 / 15 / 30) and prices are intentionally left untouched.
        // Already-granted mocks on past purchases are not retroactively revoked.

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Quick Check — remove implicit unlimited L&R (0 = none); mocks already 0 ──
            migrationBuilder.Sql(@"
UPDATE ""BillingAddOns""
SET ""GrantEntitlementsJson"" = '{""ai_credits"":5,""listening_tests"":0,""reading_tests"":0}',
    ""UpdatedAt"" = now()
WHERE ""Code"" = 'pkg_quick_check';
");
            migrationBuilder.Sql(@"
UPDATE ""BillingAddOnVersions""
SET ""GrantEntitlementsJson"" = '{""ai_credits"":5,""listening_tests"":0,""reading_tests"":0}'
WHERE ""Code"" = 'pkg_quick_check';
");

            // ── Exam Prep Pro — remove unlimited L&R and the 2 bundled mocks ──
            migrationBuilder.Sql(@"
UPDATE ""BillingAddOns""
SET ""GrantEntitlementsJson"" = '{""ai_credits"":15,""listening_tests"":0,""reading_tests"":0}',
    ""Description"" = 'One-off exam preparation package. Grants 15 grading credits and 90-day validity.',
    ""UpdatedAt"" = now()
WHERE ""Code"" = 'pkg_exam_prep_pro';
");
            migrationBuilder.Sql(@"
UPDATE ""BillingAddOnVersions""
SET ""GrantEntitlementsJson"" = '{""ai_credits"":15,""listening_tests"":0,""reading_tests"":0}',
    ""Description"" = 'One-off exam preparation package. Grants 15 grading credits and 90-day validity.'
WHERE ""Code"" = 'pkg_exam_prep_pro';
");

            // ── OET Mastery — drop the 5 bundled mocks; KEEP unlimited L&R (no L/R keys) ──
            migrationBuilder.Sql(@"
UPDATE ""BillingAddOns""
SET ""GrantEntitlementsJson"" = '{""ai_credits"":30,""priority_queue"":true}',
    ""Description"" = 'One-off mastery package. Grants 30 grading credits, 180-day validity, and priority queue access.',
    ""UpdatedAt"" = now()
WHERE ""Code"" = 'pkg_oet_mastery';
");
            migrationBuilder.Sql(@"
UPDATE ""BillingAddOnVersions""
SET ""GrantEntitlementsJson"" = '{""ai_credits"":30,""priority_queue"":true}',
    ""Description"" = 'One-off mastery package. Grants 30 grading credits, 180-day validity, and priority queue access.'
WHERE ""Code"" = 'pkg_oet_mastery';
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the prior grants + descriptions (pre-rework seeder shape).
            migrationBuilder.Sql(@"
UPDATE ""BillingAddOns""
SET ""GrantEntitlementsJson"" = '{""ai_credits"":5}',
    ""UpdatedAt"" = now()
WHERE ""Code"" = 'pkg_quick_check';
");
            migrationBuilder.Sql(@"
UPDATE ""BillingAddOnVersions""
SET ""GrantEntitlementsJson"" = '{""ai_credits"":5}'
WHERE ""Code"" = 'pkg_quick_check';
");

            migrationBuilder.Sql(@"
UPDATE ""BillingAddOns""
SET ""GrantEntitlementsJson"" = '{""ai_credits"":15,""mockFull"":2}',
    ""Description"" = 'One-off exam preparation package. Grants 15 grading credits, 2 mock entitlements, and 90-day validity.',
    ""UpdatedAt"" = now()
WHERE ""Code"" = 'pkg_exam_prep_pro';
");
            migrationBuilder.Sql(@"
UPDATE ""BillingAddOnVersions""
SET ""GrantEntitlementsJson"" = '{""ai_credits"":15,""mockFull"":2}',
    ""Description"" = 'One-off exam preparation package. Grants 15 grading credits, 2 mock entitlements, and 90-day validity.'
WHERE ""Code"" = 'pkg_exam_prep_pro';
");

            migrationBuilder.Sql(@"
UPDATE ""BillingAddOns""
SET ""GrantEntitlementsJson"" = '{""ai_credits"":30,""mockFull"":5,""priority_queue"":true}',
    ""Description"" = 'One-off mastery package. Grants 30 grading credits, 5 mock entitlements, 180-day validity, and priority queue access.',
    ""UpdatedAt"" = now()
WHERE ""Code"" = 'pkg_oet_mastery';
");
            migrationBuilder.Sql(@"
UPDATE ""BillingAddOnVersions""
SET ""GrantEntitlementsJson"" = '{""ai_credits"":30,""mockFull"":5,""priority_queue"":true}',
    ""Description"" = 'One-off mastery package. Grants 30 grading credits, 5 mock entitlements, 180-day validity, and priority queue access.'
WHERE ""Code"" = 'pkg_oet_mastery';
");
        }
    }
}
