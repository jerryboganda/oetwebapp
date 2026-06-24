using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260711090000_PublishDisciplineFullCourses")]
    public partial class PublishDisciplineFullCourses : Migration
    {
        // Activates three discipline full courses on the public catalogue so they
        // appear as their own storefront tabs (Pharmacy / Physiotherapy / Allied
        // health professions) and are purchasable:
        //
        //   full-pharmacy        Pharmacy Full Course                 £100  (re-publish + reprice + rename existing draft)
        //   full-physiotherapy   Physiotherapy Full Course            £75   (new)
        //   full-allied-health   Allied Health Profession Full Course £75   (new)
        //
        // The boot/admin reseeder (Oet2026CatalogSeeder) is DISABLED by default in
        // production, so the JSON manifest changes alone do not update an existing
        // production database. This migration applies exactly what that seeder would
        // write for a base full-course SKU (mirroring `full-nursing`), scoped to
        // these three codes only.
        //
        // Storefront display (/v1/catalog/pricing), plan_purchase quoting and PayPal
        // fulfilment all resolve against BillingPlans by Code, so only BillingPlans
        // (+ its immutable BillingPlanVersions snapshot) are load-bearing here.
        // ContentPackages is CMS-only metadata and is NOT required for the tab, card,
        // price or checkout; the existing pharmacy ContentPackage row is re-published
        // for consistency, and the two new SKUs' ContentPackage rows (if ever needed)
        // are created by the full seeder.
        //
        // Enum values verified against Domain/Enums.cs:
        //   BillingPlanStatus.Active = 1
        //   ContentStatus.Published  = 4  (Draft = 0)
        // NOT-NULL columns + C# defaults verified against LearnerDbContextModelSnapshot.cs
        // and Domain/AdminEntities.cs (DiagnosticMockEntitlement='one_per_lifetime',
        // IncludedSubtestsJson='[]', EntitlementsJson='{}'). There are no FK constraints
        // between BillingPlans and BillingPlanVersions, so insert order is unconstrained.
        // Each statement is idempotent (ON CONFLICT / scoped UPDATE) and safe to re-run.

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Publish, reprice and rename the existing `full-pharmacy` draft ──
            migrationBuilder.Sql(@"
UPDATE ""BillingPlans""
SET ""Name"" = 'Pharmacy Full Course',
    ""Price"" = 100,
    ""IsDraft"" = false,
    ""IsVisible"" = true,
    ""UpdatedAt"" = now()
WHERE ""Code"" = 'full-pharmacy';
");
            migrationBuilder.Sql(@"
UPDATE ""BillingPlanVersions""
SET ""Name"" = 'Pharmacy Full Course',
    ""Price"" = 100,
    ""IsDraft"" = false,
    ""IsVisible"" = true
WHERE ""Code"" = 'full-pharmacy';
");
            // ContentStatus.Published = 4
            migrationBuilder.Sql(@"
UPDATE ""ContentPackages""
SET ""Title"" = 'Pharmacy Full Course',
    ""Status"" = 4,
    ""UpdatedAt"" = now()
WHERE ""Code"" = 'full-pharmacy';
");

            // ── 2. Insert the two new base full-course plans ──────────────────────
            migrationBuilder.Sql(@"
INSERT INTO ""BillingPlans""
    (""Id"", ""Code"", ""Name"", ""Description"", ""Price"", ""OriginalPriceGbp"", ""Currency"", ""Interval"",
     ""DurationMonths"", ""AccessDurationDays"", ""IsVisible"", ""IsRenewable"", ""IsDraft"", ""TrialDays"",
     ""DisplayOrder"", ""IncludedCredits"", ""DiagnosticMockEntitlement"", ""IncludedSubtestsJson"",
     ""EntitlementsJson"", ""DashboardModulesJson"", ""ActiveVersionId"", ""LatestVersionId"",
     ""ActiveSubscribers"", ""Status"", ""ProductCategory"", ""Profession"", ""WritingAddonsEnabled"",
     ""SpeakingAddonsEnabled"", ""TutorBookDiscountEnabled"", ""BundledWritingAssessments"",
     ""BundledSpeakingSessions"", ""BundledAiCredits"", ""BundledTutorBook"", ""BundledBasicEnglish"",
     ""ExtensionAllowed"", ""RecallUpdatesEnabled"", ""ArchivedAt"", ""CreatedAt"", ""UpdatedAt"")
VALUES
    ('plan_full-physiotherapy', 'full-physiotherapy', 'Physiotherapy Full Course',
     'Profession-specific OET preparation for physiotherapists. Covers all four sub-tests with physiotherapy-specific Writing letters (referral, discharge, transfer, update) and physiotherapy Speaking scenarios. Base SKU without bundled assessments or AI credits.',
     75, NULL, 'GBP', 'one_time', 6, 180, true, false, false, 0, 160, 0, 'one_per_lifetime', '[]', '{}',
     '[""Listening"",""Reading"",""Writing"",""Speaking"",""MaterialsLibrary"",""Recalls"",""Addons""]',
     'planv_full-physiotherapy_v1', 'planv_full-physiotherapy_v1', 0, 1, 'full_course', 'physiotherapy',
     true, false, true, 0, 0, 0, false, false, true, true, NULL, now(), now()),
    ('plan_full-allied-health', 'full-allied-health', 'Allied Health Profession Full Course',
     'Profession-specific OET preparation for allied health professionals. Covers all four sub-tests with allied-health Writing letters and Speaking scenarios drawn from across the allied health professions. Base SKU without bundled assessments or AI credits.',
     75, NULL, 'GBP', 'one_time', 6, 180, true, false, false, 0, 170, 0, 'one_per_lifetime', '[]', '{}',
     '[""Listening"",""Reading"",""Writing"",""Speaking"",""MaterialsLibrary"",""Recalls"",""Addons""]',
     'planv_full-allied-health_v1', 'planv_full-allied-health_v1', 0, 1, 'full_course', 'allied_health',
     true, false, true, 0, 0, 0, false, false, true, true, NULL, now(), now())
ON CONFLICT (""Id"") DO UPDATE SET
    ""Name"" = EXCLUDED.""Name"",
    ""Description"" = EXCLUDED.""Description"",
    ""Price"" = EXCLUDED.""Price"",
    ""IsVisible"" = EXCLUDED.""IsVisible"",
    ""IsDraft"" = EXCLUDED.""IsDraft"",
    ""DisplayOrder"" = EXCLUDED.""DisplayOrder"",
    ""Profession"" = EXCLUDED.""Profession"",
    ""ProductCategory"" = EXCLUDED.""ProductCategory"",
    ""Status"" = EXCLUDED.""Status"",
    ""UpdatedAt"" = now();
");

            // ── 3. Insert the matching immutable plan-version snapshots ───────────
            migrationBuilder.Sql(@"
INSERT INTO ""BillingPlanVersions""
    (""Id"", ""PlanId"", ""VersionNumber"", ""Code"", ""Name"", ""Description"", ""Price"", ""OriginalPriceGbp"",
     ""Currency"", ""Interval"", ""DurationMonths"", ""AccessDurationDays"", ""IsVisible"", ""IsRenewable"",
     ""IsDraft"", ""TrialDays"", ""DisplayOrder"", ""IncludedCredits"", ""IncludedSubtestsJson"",
     ""EntitlementsJson"", ""DashboardModulesJson"", ""Status"", ""ProductCategory"", ""Profession"",
     ""WritingAddonsEnabled"", ""SpeakingAddonsEnabled"", ""TutorBookDiscountEnabled"",
     ""BundledWritingAssessments"", ""BundledSpeakingSessions"", ""BundledAiCredits"", ""BundledTutorBook"",
     ""BundledBasicEnglish"", ""ExtensionAllowed"", ""RecallUpdatesEnabled"", ""ArchivedAt"",
     ""CreatedByAdminId"", ""CreatedByAdminName"", ""CreatedAt"")
VALUES
    ('planv_full-physiotherapy_v1', 'plan_full-physiotherapy', 1, 'full-physiotherapy', 'Physiotherapy Full Course',
     'Profession-specific OET preparation for physiotherapists. Covers all four sub-tests with physiotherapy-specific Writing letters (referral, discharge, transfer, update) and physiotherapy Speaking scenarios. Base SKU without bundled assessments or AI credits.',
     75, NULL, 'GBP', 'one_time', 6, 180, true, false, false, 0, 160, 0, '[]', '{}',
     '[""Listening"",""Reading"",""Writing"",""Speaking"",""MaterialsLibrary"",""Recalls"",""Addons""]',
     1, 'full_course', 'physiotherapy', true, false, true, 0, 0, 0, false, false, true, true, NULL,
     'system:oet-2026-catalog', 'OET 2026 Catalog Seeder', now()),
    ('planv_full-allied-health_v1', 'plan_full-allied-health', 1, 'full-allied-health', 'Allied Health Profession Full Course',
     'Profession-specific OET preparation for allied health professionals. Covers all four sub-tests with allied-health Writing letters and Speaking scenarios drawn from across the allied health professions. Base SKU without bundled assessments or AI credits.',
     75, NULL, 'GBP', 'one_time', 6, 180, true, false, false, 0, 170, 0, '[]', '{}',
     '[""Listening"",""Reading"",""Writing"",""Speaking"",""MaterialsLibrary"",""Recalls"",""Addons""]',
     1, 'full_course', 'allied_health', true, false, true, 0, 0, 0, false, false, true, true, NULL,
     'system:oet-2026-catalog', 'OET 2026 Catalog Seeder', now())
ON CONFLICT (""Id"") DO UPDATE SET
    ""Name"" = EXCLUDED.""Name"",
    ""Description"" = EXCLUDED.""Description"",
    ""Price"" = EXCLUDED.""Price"",
    ""IsVisible"" = EXCLUDED.""IsVisible"",
    ""IsDraft"" = EXCLUDED.""IsDraft"",
    ""DisplayOrder"" = EXCLUDED.""DisplayOrder"",
    ""Profession"" = EXCLUDED.""Profession"",
    ""ProductCategory"" = EXCLUDED.""ProductCategory"",
    ""Status"" = EXCLUDED.""Status"";
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the two new SKUs and restore the pharmacy draft to its prior state.
            migrationBuilder.Sql(@"
DELETE FROM ""BillingPlanVersions"" WHERE ""Code"" IN ('full-physiotherapy', 'full-allied-health');
");
            migrationBuilder.Sql(@"
DELETE FROM ""BillingPlans"" WHERE ""Code"" IN ('full-physiotherapy', 'full-allied-health');
");
            // Re-hide pharmacy (ContentStatus.Draft = 0).
            migrationBuilder.Sql(@"
UPDATE ""BillingPlans""
SET ""Name"" = 'Full Pharmacy OET Course', ""Price"" = 0, ""IsDraft"" = true, ""IsVisible"" = false, ""UpdatedAt"" = now()
WHERE ""Code"" = 'full-pharmacy';
");
            migrationBuilder.Sql(@"
UPDATE ""BillingPlanVersions""
SET ""Name"" = 'Full Pharmacy OET Course', ""Price"" = 0, ""IsDraft"" = true, ""IsVisible"" = false
WHERE ""Code"" = 'full-pharmacy';
");
            migrationBuilder.Sql(@"
UPDATE ""ContentPackages""
SET ""Title"" = 'Full Pharmacy OET Course', ""Status"" = 0, ""UpdatedAt"" = now()
WHERE ""Code"" = 'full-pharmacy';
");
        }
    }
}
