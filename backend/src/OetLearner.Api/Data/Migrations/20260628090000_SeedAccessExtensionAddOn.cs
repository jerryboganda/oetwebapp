using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260628090000_SeedAccessExtensionAddOn")]
    public partial class SeedAccessExtensionAddOn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data migration — seeds the `addon-extend-90` ("Extend Access — 90 days")
            // access_extension add-on into production.
            //
            // The Oet2026CatalogSeeder is DISABLED by default in production, so the
            // JSON manifest entry alone never reaches the live database. This migration
            // writes EXACTLY what Oet2026CatalogSeeder.UpsertAddOnAsync /
            // CopyAddOnIntoVersion would have written for this add-on:
            //
            //   BillingAddOn  Id = "addon_addon-extend-90"
            //   BillingAddOnVersion Id = "addonv_addon-extend-90_v1" (VersionNumber 1, Active)
            //   BillingAddOn.ActiveVersionId / LatestVersionId -> the version Id.
            //
            // Field values mirror the manifest object (Data/Seeds/oet-2026-catalog.json):
            //   price 15, addonKind access_extension, eligibilityFlag "",
            //   requiresEligibleParent true, extensionDays 90, isStackable true,
            //   displayOrder 75. Seeder-derived defaults: Currency GBP, Interval
            //   one_time, Status Active(=1), IsRecurring false, DurationDays 180
            //   (AddOnDto default — manifest omits durationDays), GrantCredits 0,
            //   GrantEntitlementsJson "{}" (all grants zero for access_extension),
            //   CompatiblePlanCodesJson "[]", AppliesToAllPlans false, QuantityStep 1,
            //   MaxQuantity NULL (stackable), LettersGranted 0, SessionsGranted 0,
            //   OriginalPriceGbp NULL.
            //
            // Verified against LearnerDbContextModelSnapshot.cs — every NOT NULL /
            // non-nullable column on both tables is provided below. Idempotent: guarded
            // by WHERE NOT EXISTS so re-runs and an already-seeded DB are no-ops.
            // Timestamps default to now() so a manual run does not need a value.

            migrationBuilder.Sql(@"
INSERT INTO ""BillingAddOns"" (
    ""Id"", ""Code"", ""Name"", ""Description"", ""Price"", ""Currency"", ""Interval"",
    ""Status"", ""IsRecurring"", ""DurationDays"", ""GrantCredits"", ""GrantEntitlementsJson"",
    ""CompatiblePlanCodesJson"", ""ActiveVersionId"", ""LatestVersionId"", ""AppliesToAllPlans"",
    ""IsStackable"", ""QuantityStep"", ""MaxQuantity"", ""DisplayOrder"", ""CreatedAt"", ""UpdatedAt"",
    ""OriginalPriceGbp"", ""AddonKind"", ""RequiresEligibleParent"", ""EligibilityFlag"",
    ""LettersGranted"", ""SessionsGranted"", ""ExtensionDays""
)
SELECT
    'addon_addon-extend-90', 'addon-extend-90', 'Extend Access — 90 days',
    'Extends your current course access by 90 days, pushing the expiry of your active eligible subscription out from the later of today or its current expiry.',
    15, 'GBP', 'one_time',
    1, false, 180, 0, '{}',
    '[]', 'addonv_addon-extend-90_v1', 'addonv_addon-extend-90_v1', false,
    true, 1, NULL, 75, now(), now(),
    NULL, 'access_extension', true, '',
    0, 0, 90
WHERE NOT EXISTS (
    SELECT 1 FROM ""BillingAddOns"" WHERE ""Code"" = 'addon-extend-90'
);
");

            migrationBuilder.Sql(@"
INSERT INTO ""BillingAddOnVersions"" (
    ""Id"", ""AddOnId"", ""VersionNumber"", ""Code"", ""Name"", ""Description"", ""Price"",
    ""Currency"", ""Interval"", ""Status"", ""IsRecurring"", ""DurationDays"", ""GrantCredits"",
    ""GrantEntitlementsJson"", ""CompatiblePlanCodesJson"", ""AppliesToAllPlans"", ""IsStackable"",
    ""QuantityStep"", ""MaxQuantity"", ""DisplayOrder"", ""CreatedByAdminId"", ""CreatedByAdminName"",
    ""CreatedAt"", ""OriginalPriceGbp"", ""AddonKind"", ""RequiresEligibleParent"", ""EligibilityFlag"",
    ""LettersGranted"", ""SessionsGranted"", ""ExtensionDays""
)
SELECT
    'addonv_addon-extend-90_v1', 'addon_addon-extend-90', 1, 'addon-extend-90',
    'Extend Access — 90 days',
    'Extends your current course access by 90 days, pushing the expiry of your active eligible subscription out from the later of today or its current expiry.',
    15, 'GBP', 'one_time', 1, false, 180, 0,
    '{}', '[]', false, true,
    1, NULL, 75, 'system:oet-2026-catalog', 'OET 2026 Catalog Seeder',
    now(), NULL, 'access_extension', true, '',
    0, 0, 90
WHERE NOT EXISTS (
    SELECT 1 FROM ""BillingAddOnVersions"" WHERE ""Id"" = 'addonv_addon-extend-90_v1'
);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse — remove the seeded add-on and its version. Null out the
            // version pointers first to avoid leaving dangling references, then
            // delete by deterministic Id / Code.
            migrationBuilder.Sql(@"
DELETE FROM ""BillingAddOnVersions"" WHERE ""Id"" = 'addonv_addon-extend-90_v1';
");
            migrationBuilder.Sql(@"
DELETE FROM ""BillingAddOns"" WHERE ""Code"" = 'addon-extend-90';
");
        }
    }
}
