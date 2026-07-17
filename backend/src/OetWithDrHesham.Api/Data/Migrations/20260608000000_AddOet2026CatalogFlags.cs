using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// OET 2026 catalog foundation — Wave 1 of the 27-SKU portfolio.
    ///
    /// Adds new columns to <c>BillingPlans</c>, <c>BillingPlanVersions</c>,
    /// <c>BillingAddOns</c>, <c>BillingAddOnVersions</c>, <c>Subscriptions</c>,
    /// and <c>ContentPackages</c> so the catalog defined by Dr Ahmed Hesham's
    /// 2026 portfolio PDFs can be modelled in the existing billing tables.
    ///
    /// <para>
    /// Hand-written Postgres-only migration. SQLite test runs bypass
    /// migrations via <c>EnsureCreatedAsync()</c> and pick up the change
    /// directly from entity attributes.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260608000000_AddOet2026CatalogFlags")]
    public partial class AddOet2026CatalogFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- ── BillingPlans ────────────────────────────────────────────────────────
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""OriginalPriceGbp"" numeric NULL;
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""AccessDurationDays"" integer NOT NULL DEFAULT 180;
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""WritingAddonsEnabled"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""SpeakingAddonsEnabled"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""TutorBookDiscountEnabled"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""Profession"" varchar(32) NOT NULL DEFAULT 'all';
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""ProductCategory"" varchar(32) NOT NULL DEFAULT '';
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""DashboardModulesJson"" varchar(2048) NOT NULL DEFAULT '[]';
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""BundledWritingAssessments"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""BundledSpeakingSessions"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""BundledAiCredits"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""BundledTutorBook"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""BundledBasicEnglish"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""IsDraft"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""ExtensionAllowed"" boolean NOT NULL DEFAULT true;
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""RecallUpdatesEnabled"" boolean NOT NULL DEFAULT false;

                -- ── BillingPlanVersions ─────────────────────────────────────────────────
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""OriginalPriceGbp"" numeric NULL;
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""AccessDurationDays"" integer NOT NULL DEFAULT 180;
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""WritingAddonsEnabled"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""SpeakingAddonsEnabled"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""TutorBookDiscountEnabled"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""Profession"" varchar(32) NOT NULL DEFAULT 'all';
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""ProductCategory"" varchar(32) NOT NULL DEFAULT '';
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""DashboardModulesJson"" varchar(2048) NOT NULL DEFAULT '[]';
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""BundledWritingAssessments"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""BundledSpeakingSessions"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""BundledAiCredits"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""BundledTutorBook"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""BundledBasicEnglish"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""IsDraft"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""ExtensionAllowed"" boolean NOT NULL DEFAULT true;
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""RecallUpdatesEnabled"" boolean NOT NULL DEFAULT false;

                -- ── BillingAddOns ───────────────────────────────────────────────────────
                ALTER TABLE ""BillingAddOns"" ADD COLUMN IF NOT EXISTS ""OriginalPriceGbp"" numeric NULL;
                ALTER TABLE ""BillingAddOns"" ADD COLUMN IF NOT EXISTS ""AddonKind"" varchar(32) NOT NULL DEFAULT '';
                ALTER TABLE ""BillingAddOns"" ADD COLUMN IF NOT EXISTS ""RequiresEligibleParent"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""BillingAddOns"" ADD COLUMN IF NOT EXISTS ""EligibilityFlag"" varchar(32) NOT NULL DEFAULT '';
                ALTER TABLE ""BillingAddOns"" ADD COLUMN IF NOT EXISTS ""LettersGranted"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""BillingAddOns"" ADD COLUMN IF NOT EXISTS ""SessionsGranted"" integer NOT NULL DEFAULT 0;

                -- ── BillingAddOnVersions ────────────────────────────────────────────────
                ALTER TABLE ""BillingAddOnVersions"" ADD COLUMN IF NOT EXISTS ""OriginalPriceGbp"" numeric NULL;
                ALTER TABLE ""BillingAddOnVersions"" ADD COLUMN IF NOT EXISTS ""AddonKind"" varchar(32) NOT NULL DEFAULT '';
                ALTER TABLE ""BillingAddOnVersions"" ADD COLUMN IF NOT EXISTS ""RequiresEligibleParent"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""BillingAddOnVersions"" ADD COLUMN IF NOT EXISTS ""EligibilityFlag"" varchar(32) NOT NULL DEFAULT '';
                ALTER TABLE ""BillingAddOnVersions"" ADD COLUMN IF NOT EXISTS ""LettersGranted"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""BillingAddOnVersions"" ADD COLUMN IF NOT EXISTS ""SessionsGranted"" integer NOT NULL DEFAULT 0;

                -- ── Subscriptions ───────────────────────────────────────────────────────
                ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""ExpiresAt"" timestamp with time zone NULL;
                ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""WritingAssessmentsRemaining"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""SpeakingSessionsRemaining"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""AiCreditsRemaining"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""TutorBookUnlocked"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""BasicEnglishUnlocked"" boolean NOT NULL DEFAULT false;

                -- ── ContentPackages ─────────────────────────────────────────────────────
                ALTER TABLE ""ContentPackages"" ADD COLUMN IF NOT EXISTS ""BillingAddOnId"" varchar(64) NULL;

                -- ── Indexes for fast catalog + eligibility queries ──────────────────────
                CREATE INDEX IF NOT EXISTS ""IX_BillingPlans_PublicCatalog""
                    ON ""BillingPlans"" (""IsVisible"", ""IsDraft"", ""ProductCategory"", ""DisplayOrder"");

                CREATE INDEX IF NOT EXISTS ""IX_BillingPlans_ProfessionCategory""
                    ON ""BillingPlans"" (""Profession"", ""ProductCategory"", ""DisplayOrder"");

                CREATE INDEX IF NOT EXISTS ""IX_BillingAddOns_Eligibility""
                    ON ""BillingAddOns"" (""EligibilityFlag"", ""Status"")
                    WHERE ""RequiresEligibleParent"" = true;

                CREATE INDEX IF NOT EXISTS ""IX_ContentPackages_BillingAddOnId""
                    ON ""ContentPackages"" (""BillingAddOnId"")
                    WHERE ""BillingAddOnId"" IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_ContentPackages_BillingAddOnId"";
                DROP INDEX IF EXISTS ""IX_BillingAddOns_Eligibility"";
                DROP INDEX IF EXISTS ""IX_BillingPlans_ProfessionCategory"";
                DROP INDEX IF EXISTS ""IX_BillingPlans_PublicCatalog"";

                ALTER TABLE ""ContentPackages"" DROP COLUMN IF EXISTS ""BillingAddOnId"";

                ALTER TABLE ""Subscriptions"" DROP COLUMN IF EXISTS ""BasicEnglishUnlocked"";
                ALTER TABLE ""Subscriptions"" DROP COLUMN IF EXISTS ""TutorBookUnlocked"";
                ALTER TABLE ""Subscriptions"" DROP COLUMN IF EXISTS ""AiCreditsRemaining"";
                ALTER TABLE ""Subscriptions"" DROP COLUMN IF EXISTS ""SpeakingSessionsRemaining"";
                ALTER TABLE ""Subscriptions"" DROP COLUMN IF EXISTS ""WritingAssessmentsRemaining"";
                ALTER TABLE ""Subscriptions"" DROP COLUMN IF EXISTS ""ExpiresAt"";

                ALTER TABLE ""BillingAddOnVersions"" DROP COLUMN IF EXISTS ""SessionsGranted"";
                ALTER TABLE ""BillingAddOnVersions"" DROP COLUMN IF EXISTS ""LettersGranted"";
                ALTER TABLE ""BillingAddOnVersions"" DROP COLUMN IF EXISTS ""EligibilityFlag"";
                ALTER TABLE ""BillingAddOnVersions"" DROP COLUMN IF EXISTS ""RequiresEligibleParent"";
                ALTER TABLE ""BillingAddOnVersions"" DROP COLUMN IF EXISTS ""AddonKind"";
                ALTER TABLE ""BillingAddOnVersions"" DROP COLUMN IF EXISTS ""OriginalPriceGbp"";

                ALTER TABLE ""BillingAddOns"" DROP COLUMN IF EXISTS ""SessionsGranted"";
                ALTER TABLE ""BillingAddOns"" DROP COLUMN IF EXISTS ""LettersGranted"";
                ALTER TABLE ""BillingAddOns"" DROP COLUMN IF EXISTS ""EligibilityFlag"";
                ALTER TABLE ""BillingAddOns"" DROP COLUMN IF EXISTS ""RequiresEligibleParent"";
                ALTER TABLE ""BillingAddOns"" DROP COLUMN IF EXISTS ""AddonKind"";
                ALTER TABLE ""BillingAddOns"" DROP COLUMN IF EXISTS ""OriginalPriceGbp"";

                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""RecallUpdatesEnabled"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""ExtensionAllowed"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""IsDraft"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""BundledBasicEnglish"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""BundledTutorBook"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""BundledAiCredits"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""BundledSpeakingSessions"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""BundledWritingAssessments"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""DashboardModulesJson"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""ProductCategory"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""Profession"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""TutorBookDiscountEnabled"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""SpeakingAddonsEnabled"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""WritingAddonsEnabled"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""AccessDurationDays"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""OriginalPriceGbp"";

                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""RecallUpdatesEnabled"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""ExtensionAllowed"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""IsDraft"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""BundledBasicEnglish"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""BundledTutorBook"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""BundledAiCredits"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""BundledSpeakingSessions"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""BundledWritingAssessments"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""DashboardModulesJson"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""ProductCategory"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""Profession"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""TutorBookDiscountEnabled"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""SpeakingAddonsEnabled"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""WritingAddonsEnabled"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""AccessDurationDays"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""OriginalPriceGbp"";
            ");
        }
    }
}
