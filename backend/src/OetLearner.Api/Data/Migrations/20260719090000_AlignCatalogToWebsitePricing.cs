using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260719090000_AlignCatalogToWebsitePricing")]
    public partial class AlignCatalogToWebsitePricing : Migration
    {
        // Aligns the live billing catalog 1:1 with the public website pricing page
        // (https://oetwithdrhesham.co.uk/pricing.html) — the single source of truth.
        //
        // Scope (data only, no schema change):
        //   1. Rename plans/add-ons whose names deviated from the website.
        //   2. Drop promotional "was £X" strike-throughs the website does NOT show
        //      (kept only on the two TutorBook items, which the website shows as "was £60").
        //   3. Reprice + re-entitle every AI grading package and mock package to the
        //      website values (QC/EPP gain 3+3 / 6+6 Listening&Reading; OET Mastery is
        //      capped at 40 flexible credits with unlimited L&R kept; Listening/Reading
        //      Starter/Standard test allowances corrected to 3 / 6).
        //   4. Archive app-only products the website does not list (the deprecated
        //      "AI Speaking Credits" 4/10/20 packs and the "Extend Access — 90 days" add-on).
        //
        // Entitlement contract (verified against LearnerService.ReadAiPackageExtras and
        // AiPackageCreditService.AiPackageGrant.FromAddOn):
        //   flexible credits = GrantEntitlementsJson.flexible_credits ?? GrantCredits column
        //   listening/reading allowance = listening_tests / reading_tests (absent/null = UNLIMITED, 0 = none, N = N tests)
        //   mocks = mock_exams ?? mockFull    priority queue = priority_queue
        // Every UPDATE mirrors both the live row and its BillingAddOnVersions/BillingPlanVersions
        // snapshot (kept in sync by the seeder's CopyAddOn/CopyPlanIntoVersion).
        //
        // The Oet2026CatalogSeeder is DISABLED in production (Content:Oet2026Catalog:Enabled=false),
        // so the JSON manifest never reaches the live DB — catalog changes ship as this migration.
        // Data/Seeds/oet-2026-catalog.json is updated in the same commit for dev/test reseed parity.
        //
        // BillingAddOnStatus enum (Domain/Enums.cs): Draft=0, Active=1, Inactive=2, Archived=3.
        // Every statement is scoped by Code and safe to re-run (idempotent UPDATE).

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Plan renames to match the website exactly ──
            SetPlanName(migrationBuilder, "full-condensed-medicine", "Full Condensed Recorded OET Course - Medicine");
            SetPlanName(migrationBuilder, "full-condensed-medicine-tbook", "Full Condensed Recorded Course + TutorBook");
            SetPlanName(migrationBuilder, "full-pharmacy", "Full Pharmacy OET Course");
            SetPlanName(migrationBuilder, "full-physiotherapy", "Full Physiotherapy OET Course");
            SetPlanName(migrationBuilder, "full-allied-health", "Full Allied Health Profession OET Course");
            SetPlanName(migrationBuilder, "basic-english", "Basic English Course - Preparation for OET");
            SetPlanName(migrationBuilder, "crash-course", "Full Crash Course - General OET");
            SetPlanName(migrationBuilder, "double-special", "Double Special Package - Writing + Speaking");
            SetPlanName(migrationBuilder, "tutor-book", "TutorBook - First Edition 2026");

            // ── 2. Add-on renames to match the website exactly ──
            SetAddOnName(migrationBuilder, "addon-3-letters", "3 Writing Letter Assessments - Add-on");
            SetAddOnName(migrationBuilder, "addon-5-letters", "5 Writing Letter Assessments - Add-on");
            SetAddOnName(migrationBuilder, "addon-7-letters", "7 Writing Letter Assessments - Add-on");
            SetAddOnName(migrationBuilder, "addon-10-letters", "10 Writing Letter Assessments - Add-on");
            SetAddOnName(migrationBuilder, "tutor-book-addon", "TutorBook - Add-on for Enrolled Students");

            // ── 3. Remove promotional "was £X" strike-throughs the website does not show ──
            ClearPlanOriginalPrice(migrationBuilder, "writing-crash");
            ClearPlanOriginalPrice(migrationBuilder, "writing-crash-2");
            ClearPlanOriginalPrice(migrationBuilder, "writing-crash-3");
            ClearPlanOriginalPrice(migrationBuilder, "writing-crash-5");
            ClearPlanOriginalPrice(migrationBuilder, "writing-crash-7");
            ClearPlanOriginalPrice(migrationBuilder, "writing-crash-10");
            ClearPlanOriginalPrice(migrationBuilder, "double-special");
            ClearPlanOriginalPrice(migrationBuilder, "mega-special");
            ClearAddOnOriginalPrice(migrationBuilder, "addon-3-letters");
            ClearAddOnOriginalPrice(migrationBuilder, "addon-5-letters");
            ClearAddOnOriginalPrice(migrationBuilder, "addon-7-letters");
            ClearAddOnOriginalPrice(migrationBuilder, "addon-10-letters");
            // tutor-book (was £60) and tutor-book-addon (was £60) intentionally KEPT — the website shows them.

            // ── 4. AI "full" packages — price + entitlements ──
            SetAiPackage(migrationBuilder, "pkg_quick_check", price: 15,
                grants: "{\"ai_credits\":5,\"listening_tests\":3,\"reading_tests\":3}");
            SetAiPackage(migrationBuilder, "pkg_exam_prep_pro", price: 32,
                grants: "{\"ai_credits\":15,\"listening_tests\":6,\"reading_tests\":6}");
            SetAiPackage(migrationBuilder, "pkg_oet_mastery", price: 75, grantCredits: 40,
                grants: "{\"ai_credits\":40,\"priority_queue\":true}");

            // ── 5. AI Listening / Reading practice packs — price + test allowance (Pro stays unlimited) ──
            SetAiPackage(migrationBuilder, "pkg_listening_starter", price: 4, grants: "{\"listening_tests\":3}");
            SetAiPackage(migrationBuilder, "pkg_listening_standard", price: 9, grants: "{\"listening_tests\":6}");
            SetAddOnPrice(migrationBuilder, "pkg_listening_pro", 15);
            SetAiPackage(migrationBuilder, "pkg_reading_starter", price: 4, grants: "{\"reading_tests\":3}");
            SetAiPackage(migrationBuilder, "pkg_reading_standard", price: 9, grants: "{\"reading_tests\":6}");
            SetAddOnPrice(migrationBuilder, "pkg_reading_pro", 15);

            // ── 6. AI Writing / Speaking grading packs — price only (credit counts unchanged) ──
            SetAddOnPrice(migrationBuilder, "pkg_writing_starter", 9);
            SetAddOnPrice(migrationBuilder, "pkg_writing_standard", 19);
            SetAddOnPrice(migrationBuilder, "pkg_writing_pro", 32);
            SetAddOnPrice(migrationBuilder, "pkg_speaking_starter", 12);
            SetAddOnPrice(migrationBuilder, "pkg_speaking_standard", 24);
            SetAddOnPrice(migrationBuilder, "pkg_speaking_pro", 42);

            // ── 7. Mock packages — price only (mock counts unchanged) ──
            SetAddOnPrice(migrationBuilder, "pkg_mock_1", 19);
            SetAddOnPrice(migrationBuilder, "pkg_mock_3", 45);
            SetAddOnPrice(migrationBuilder, "pkg_mock_5", 67);

            // ── 8. Archive app-only products absent from the website ──
            ArchiveAddOn(migrationBuilder, "pkg_speaking_ai_starter");
            ArchiveAddOn(migrationBuilder, "pkg_speaking_ai_standard");
            ArchiveAddOn(migrationBuilder, "pkg_speaking_ai_pro");
            ArchiveAddOn(migrationBuilder, "addon-extend-90");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ── Restore prior names ──
            SetPlanName(migrationBuilder, "full-condensed-medicine", "Full Condensed Recorded OET Course — Medicine");
            SetPlanName(migrationBuilder, "full-condensed-medicine-tbook", "Full Condensed Recorded Course + The Tutor Book");
            SetPlanName(migrationBuilder, "full-pharmacy", "Pharmacy Full Course");
            SetPlanName(migrationBuilder, "full-physiotherapy", "Physiotherapy Full Course");
            SetPlanName(migrationBuilder, "full-allied-health", "Allied Health Profession Full Course");
            SetPlanName(migrationBuilder, "basic-english", "Basic English Course — Preparation for OET");
            SetPlanName(migrationBuilder, "crash-course", "Full Crash Course — General OET");
            SetPlanName(migrationBuilder, "double-special", "Double Special Package — Writing + Speaking");
            SetPlanName(migrationBuilder, "tutor-book", "The Tutor Book — First Edition 2026");
            SetAddOnName(migrationBuilder, "addon-3-letters", "3 Writing Letter Assessments — Add-on");
            SetAddOnName(migrationBuilder, "addon-5-letters", "5 Writing Letter Assessments — Add-on");
            SetAddOnName(migrationBuilder, "addon-7-letters", "7 Writing Letter Assessments — Add-on");
            SetAddOnName(migrationBuilder, "addon-10-letters", "10 Writing Letter Assessments — Add-on");
            SetAddOnName(migrationBuilder, "tutor-book-addon", "The Tutor Book — Add-on Price (Enrolled Students)");

            // ── Restore prior promotional original prices ──
            SetPlanOriginalPrice(migrationBuilder, "writing-crash", 50);
            SetPlanOriginalPrice(migrationBuilder, "writing-crash-2", 55);
            SetPlanOriginalPrice(migrationBuilder, "writing-crash-3", 65);
            SetPlanOriginalPrice(migrationBuilder, "writing-crash-5", 85);
            SetPlanOriginalPrice(migrationBuilder, "writing-crash-7", 105);
            SetPlanOriginalPrice(migrationBuilder, "writing-crash-10", 135);
            SetPlanOriginalPrice(migrationBuilder, "double-special", 70);
            SetPlanOriginalPrice(migrationBuilder, "mega-special", 120);
            SetAddOnOriginalPrice(migrationBuilder, "addon-3-letters", 40);
            SetAddOnOriginalPrice(migrationBuilder, "addon-5-letters", 60);
            SetAddOnOriginalPrice(migrationBuilder, "addon-7-letters", 75);
            SetAddOnOriginalPrice(migrationBuilder, "addon-10-letters", 100);

            // ── Restore prior AI prices + entitlements (pre-alignment seeder shape) ──
            SetAiPackage(migrationBuilder, "pkg_quick_check", 19, "{\"ai_credits\":5,\"listening_tests\":0,\"reading_tests\":0}");
            SetAiPackage(migrationBuilder, "pkg_exam_prep_pro", 42, "{\"ai_credits\":15,\"listening_tests\":0,\"reading_tests\":0}");
            SetAiPackage(migrationBuilder, "pkg_oet_mastery", 100, "{\"ai_credits\":30,\"priority_queue\":true}", grantCredits: 30);
            SetAiPackage(migrationBuilder, "pkg_listening_starter", 5, "{}");
            SetAiPackage(migrationBuilder, "pkg_listening_standard", 12, "{}");
            SetAddOnPrice(migrationBuilder, "pkg_listening_pro", 19);
            SetAiPackage(migrationBuilder, "pkg_reading_starter", 5, "{}");
            SetAiPackage(migrationBuilder, "pkg_reading_standard", 12, "{}");
            SetAddOnPrice(migrationBuilder, "pkg_reading_pro", 19);
            SetAddOnPrice(migrationBuilder, "pkg_writing_starter", 12);
            SetAddOnPrice(migrationBuilder, "pkg_writing_standard", 25);
            SetAddOnPrice(migrationBuilder, "pkg_writing_pro", 42);
            SetAddOnPrice(migrationBuilder, "pkg_speaking_starter", 15);
            SetAddOnPrice(migrationBuilder, "pkg_speaking_standard", 32);
            SetAddOnPrice(migrationBuilder, "pkg_speaking_pro", 55);
            SetAddOnPrice(migrationBuilder, "pkg_mock_1", 25);
            SetAddOnPrice(migrationBuilder, "pkg_mock_3", 59);
            SetAddOnPrice(migrationBuilder, "pkg_mock_5", 89);

            // ── Un-archive the app-only products (BillingAddOnStatus.Active = 1) ──
            UnarchiveAddOn(migrationBuilder, "pkg_speaking_ai_starter");
            UnarchiveAddOn(migrationBuilder, "pkg_speaking_ai_standard");
            UnarchiveAddOn(migrationBuilder, "pkg_speaking_ai_pro");
            UnarchiveAddOn(migrationBuilder, "addon-extend-90");
        }

        // ── Helpers ── each writes the live row and its immutable version snapshot ──

        private static void SetPlanName(MigrationBuilder mb, string code, string name)
        {
            var n = name.Replace("'", "''");
            mb.Sql($@"UPDATE ""BillingPlans"" SET ""Name"" = '{n}', ""UpdatedAt"" = now() WHERE ""Code"" = '{code}';");
            mb.Sql($@"UPDATE ""BillingPlanVersions"" SET ""Name"" = '{n}' WHERE ""Code"" = '{code}';");
        }

        private static void SetAddOnName(MigrationBuilder mb, string code, string name)
        {
            var n = name.Replace("'", "''");
            mb.Sql($@"UPDATE ""BillingAddOns"" SET ""Name"" = '{n}', ""UpdatedAt"" = now() WHERE ""Code"" = '{code}';");
            mb.Sql($@"UPDATE ""BillingAddOnVersions"" SET ""Name"" = '{n}' WHERE ""Code"" = '{code}';");
        }

        private static void ClearPlanOriginalPrice(MigrationBuilder mb, string code)
        {
            mb.Sql($@"UPDATE ""BillingPlans"" SET ""OriginalPriceGbp"" = NULL, ""UpdatedAt"" = now() WHERE ""Code"" = '{code}';");
            mb.Sql($@"UPDATE ""BillingPlanVersions"" SET ""OriginalPriceGbp"" = NULL WHERE ""Code"" = '{code}';");
        }

        private static void SetPlanOriginalPrice(MigrationBuilder mb, string code, int original)
        {
            mb.Sql($@"UPDATE ""BillingPlans"" SET ""OriginalPriceGbp"" = {original}, ""UpdatedAt"" = now() WHERE ""Code"" = '{code}';");
            mb.Sql($@"UPDATE ""BillingPlanVersions"" SET ""OriginalPriceGbp"" = {original} WHERE ""Code"" = '{code}';");
        }

        private static void ClearAddOnOriginalPrice(MigrationBuilder mb, string code)
        {
            mb.Sql($@"UPDATE ""BillingAddOns"" SET ""OriginalPriceGbp"" = NULL, ""UpdatedAt"" = now() WHERE ""Code"" = '{code}';");
            mb.Sql($@"UPDATE ""BillingAddOnVersions"" SET ""OriginalPriceGbp"" = NULL WHERE ""Code"" = '{code}';");
        }

        private static void SetAddOnOriginalPrice(MigrationBuilder mb, string code, int original)
        {
            mb.Sql($@"UPDATE ""BillingAddOns"" SET ""OriginalPriceGbp"" = {original}, ""UpdatedAt"" = now() WHERE ""Code"" = '{code}';");
            mb.Sql($@"UPDATE ""BillingAddOnVersions"" SET ""OriginalPriceGbp"" = {original} WHERE ""Code"" = '{code}';");
        }

        private static void SetAddOnPrice(MigrationBuilder mb, string code, int price)
        {
            mb.Sql($@"UPDATE ""BillingAddOns"" SET ""Price"" = {price}, ""UpdatedAt"" = now() WHERE ""Code"" = '{code}';");
            mb.Sql($@"UPDATE ""BillingAddOnVersions"" SET ""Price"" = {price} WHERE ""Code"" = '{code}';");
        }

        private static void SetAiPackage(MigrationBuilder mb, string code, int price, string grants, int? grantCredits = null)
        {
            var g = grants.Replace("'", "''");
            var creditClause = grantCredits.HasValue ? $@", ""GrantCredits"" = {grantCredits.Value}" : string.Empty;
            mb.Sql($@"UPDATE ""BillingAddOns"" SET ""Price"" = {price}, ""GrantEntitlementsJson"" = '{g}'{creditClause}, ""UpdatedAt"" = now() WHERE ""Code"" = '{code}';");
            mb.Sql($@"UPDATE ""BillingAddOnVersions"" SET ""Price"" = {price}, ""GrantEntitlementsJson"" = '{g}'{creditClause} WHERE ""Code"" = '{code}';");
        }

        private static void ArchiveAddOn(MigrationBuilder mb, string code)
        {
            // BillingAddOnStatus.Archived = 3 — drops the row from GetAiPackagesAsync (filters Status == Active).
            mb.Sql($@"UPDATE ""BillingAddOns"" SET ""Status"" = 3, ""UpdatedAt"" = now() WHERE ""Code"" = '{code}';");
        }

        private static void UnarchiveAddOn(MigrationBuilder mb, string code)
        {
            // BillingAddOnStatus.Active = 1
            mb.Sql($@"UPDATE ""BillingAddOns"" SET ""Status"" = 1, ""UpdatedAt"" = now() WHERE ""Code"" = '{code}';");
        }
    }
}
