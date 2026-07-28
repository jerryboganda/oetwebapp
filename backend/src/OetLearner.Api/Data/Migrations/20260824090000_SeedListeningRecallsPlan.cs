using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260824090000_SeedListeningRecallsPlan")]
    public partial class SeedListeningRecallsPlan : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data migration — seeds the standalone `listening-recalls` (£17, 180-day)
            // package into production. It grants ONLY the Recalls (vocabulary) dashboard
            // module — no Materials/Video/Mocks/Writing/Speaking — via the same
            // per-plan DashboardModulesJson toggle every other plan already uses
            // (see PlanModulePolicy.RecallPlanCodes, updated alongside this migration).
            //
            // The Oet2026CatalogSeeder is DISABLED by default in production, so the
            // JSON manifest entry (Data/Seeds/oet-2026-catalog.json) alone never reaches
            // the live database. This migration writes exactly what
            // Oet2026CatalogSeeder.UpsertPlanAsync / CopyPlanIntoVersion would have
            // written for this plan:
            //
            //   BillingPlan         Id = "plan_listening-recalls"
            //   BillingPlanVersion  Id = "planv_listening-recalls_v1" (VersionNumber 1, Active)
            //   BillingPlan.ActiveVersionId / LatestVersionId -> the version Id.
            //
            // Field values: price 17 GBP, one_time, accessDays 180 (DurationMonths=6),
            // productCategory "recall_package", profession "all", dashboardModules
            // ["Recalls"] only, no bundled extras, extensionAllowed true,
            // recallUpdatesEnabled true (new words added regularly). Seeder-derived
            // defaults: DiagnosticMockEntitlement "one_per_lifetime", IncludedSubtestsJson
            // "[]", EntitlementsJson "{}", ActiveSubscribers 0, IsDraft false, IsVisible
            // true. NOT-NULL columns verified against LearnerDbContextModelSnapshot.cs and
            // Domain/AdminEntities.cs — matches the column list used by
            // 20260711090000_PublishDisciplineFullCourses.cs (DeliveryMethod /
            // SpeakingPracticeAccessEnabled / TelegramInviteUrl / DeliveryInstructions /
            // ContentOverridesJson all carry DB-level defaults and are omitted here, same
            // as that migration). Idempotent: guarded by WHERE NOT EXISTS so re-runs and
            // an already-seeded DB are no-ops. Timestamps default to now().

            migrationBuilder.Sql(@"
INSERT INTO ""BillingPlans"" (
    ""Id"", ""Code"", ""Name"", ""Description"", ""Price"", ""OriginalPriceGbp"", ""Currency"", ""Interval"",
    ""DurationMonths"", ""AccessDurationDays"", ""IsVisible"", ""IsRenewable"", ""IsDraft"", ""TrialDays"",
    ""DisplayOrder"", ""IncludedCredits"", ""DiagnosticMockEntitlement"", ""IncludedSubtestsJson"",
    ""EntitlementsJson"", ""DashboardModulesJson"", ""ActiveVersionId"", ""LatestVersionId"",
    ""ActiveSubscribers"", ""Status"", ""ProductCategory"", ""Profession"", ""WritingAddonsEnabled"",
    ""SpeakingAddonsEnabled"", ""TutorBookDiscountEnabled"", ""BundledWritingAssessments"",
    ""BundledSpeakingSessions"", ""BundledAiCredits"", ""BundledTutorBook"", ""BundledBasicEnglish"",
    ""ExtensionAllowed"", ""RecallUpdatesEnabled"", ""ArchivedAt"", ""CreatedAt"", ""UpdatedAt""
)
SELECT
    'plan_listening-recalls', 'listening-recalls', 'Listening Recalls',
    'Master your OET Listening with more than 2,500 essential words, including the latest additions and commonly tested spellings. Listen, repeat, and memorise each word to improve your accuracy and confidence.',
    17, NULL, 'GBP', 'one_time',
    6, 180, true, false, false, 0,
    465, 0, 'one_per_lifetime', '[]',
    '{}', '[""Recalls""]', 'planv_listening-recalls_v1', 'planv_listening-recalls_v1',
    0, 1, 'recall_package', 'all', false,
    false, false, 0,
    0, 0, false, false,
    true, true, NULL, now(), now()
WHERE NOT EXISTS (
    SELECT 1 FROM ""BillingPlans"" WHERE ""Code"" = 'listening-recalls'
);
");

            migrationBuilder.Sql(@"
INSERT INTO ""BillingPlanVersions"" (
    ""Id"", ""PlanId"", ""VersionNumber"", ""Code"", ""Name"", ""Description"", ""Price"", ""OriginalPriceGbp"",
    ""Currency"", ""Interval"", ""DurationMonths"", ""AccessDurationDays"", ""IsVisible"", ""IsRenewable"",
    ""IsDraft"", ""TrialDays"", ""DisplayOrder"", ""IncludedCredits"", ""IncludedSubtestsJson"",
    ""EntitlementsJson"", ""DashboardModulesJson"", ""Status"", ""ProductCategory"", ""Profession"",
    ""WritingAddonsEnabled"", ""SpeakingAddonsEnabled"", ""TutorBookDiscountEnabled"",
    ""BundledWritingAssessments"", ""BundledSpeakingSessions"", ""BundledAiCredits"", ""BundledTutorBook"",
    ""BundledBasicEnglish"", ""ExtensionAllowed"", ""RecallUpdatesEnabled"", ""ArchivedAt"",
    ""CreatedByAdminId"", ""CreatedByAdminName"", ""CreatedAt""
)
SELECT
    'planv_listening-recalls_v1', 'plan_listening-recalls', 1, 'listening-recalls', 'Listening Recalls',
    'Master your OET Listening with more than 2,500 essential words, including the latest additions and commonly tested spellings. Listen, repeat, and memorise each word to improve your accuracy and confidence.',
    17, NULL, 'GBP', 'one_time', 6, 180, true, false,
    false, 0, 465, 0, '[]',
    '{}', '[""Recalls""]', 1, 'recall_package', 'all',
    false, false, false, 0,
    0, 0, false, false,
    true, true, NULL, 'system:oet-2026-catalog', 'OET 2026 Catalog Seeder', now()
WHERE NOT EXISTS (
    SELECT 1 FROM ""BillingPlanVersions"" WHERE ""Id"" = 'planv_listening-recalls_v1'
);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse — remove the seeded plan and its version. Null out the version
            // pointers first to avoid leaving dangling references, then delete by
            // deterministic Id / Code.
            migrationBuilder.Sql(@"
UPDATE ""BillingPlans"" SET ""ActiveVersionId"" = NULL, ""LatestVersionId"" = NULL
WHERE ""Code"" = 'listening-recalls';
");
            migrationBuilder.Sql(@"
DELETE FROM ""BillingPlanVersions"" WHERE ""Id"" = 'planv_listening-recalls_v1';
");
            migrationBuilder.Sql(@"
DELETE FROM ""BillingPlans"" WHERE ""Code"" = 'listening-recalls';
");
        }
    }
}
