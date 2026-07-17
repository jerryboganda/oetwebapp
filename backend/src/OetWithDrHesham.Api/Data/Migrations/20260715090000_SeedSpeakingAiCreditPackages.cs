using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260715090000_SeedSpeakingAiCreditPackages")]
    public partial class SeedSpeakingAiCreditPackages : Migration
    {
        // Data migration — seeds the three dedicated AI-Speaking credit packages
        // (`pkg_speaking_ai_starter|standard|pro`) into production.
        //
        // The Oet2026CatalogSeeder is DISABLED in production, so the JSON manifest
        // entries alone never reach the live DB — catalog changes ship as data
        // migrations (mirrors 20260628090000_SeedAccessExtensionAddOn).
        //
        // These give the AI-examiner Speaking quota its own purchasable product,
        // distinct from human-tutor private Speaking sessions (addon-speaking-*).
        // The `pkg_speaking_*` code prefix routes grantCredits into the
        // Speaking-only AI credit bucket (AiPackageGrant.ResolvePackageType) and
        // groups the card under the storefront "Speaking" family
        // (LearnerService.ResolveAiPackageGroup). 1 credit = 1 card; 2 = 1 exam.
        //
        // Each row mirrors exactly what UpsertAddOnAsync / CopyAddOnIntoVersion
        // would write: BillingAddOn id "addon_{code}", BillingAddOnVersion id
        // "addonv_{code}_v1" (VersionNumber 1, Active), pointers wired to the
        // version. GrantEntitlementsJson '{"ai_credits":N}' matches
        // BuildGrantEntitlementsJson (GrantCredits>0). Seeder defaults: Currency
        // GBP, Interval one_time, Status Active(=1), IsRecurring false,
        // CompatiblePlanCodesJson '[]', AppliesToAllPlans false, QuantityStep 1,
        // MaxQuantity NULL (stackable), OriginalPriceGbp NULL, LettersGranted 0,
        // SessionsGranted 0, ExtensionDays 0, AiFeaturesJson '[]' (auto features).
        // AiPackageGroup pinned to 'speaking' so grouping is explicit.
        //
        // Idempotent: WHERE NOT EXISTS guards make re-runs / already-seeded DBs
        // no-ops. Verified against BillingEntities.cs — every NOT NULL column is
        // supplied. Timestamps default to now().

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            SeedPackage(migrationBuilder,
                code: "pkg_speaking_ai_starter",
                name: "AI Speaking Credits — Starter (4 credits)",
                description: "AI-marked Speaking credits for the AI examiner. Each card costs 1 credit and a full exam (Card A + Card B) costs 2 — so 4 credits cover 2 full AI Speaking exams. Separate from human-tutor sessions. 30-day validity.",
                price: 8, credits: 4, durationDays: 30, displayOrder: 101);

            SeedPackage(migrationBuilder,
                code: "pkg_speaking_ai_standard",
                name: "AI Speaking Credits — Standard (10 credits)",
                description: "AI-marked Speaking credits for the AI examiner. 1 credit per card, 2 per full exam — 10 credits cover 5 full AI Speaking exams. Separate from human-tutor sessions. 90-day validity.",
                price: 18, credits: 10, durationDays: 90, displayOrder: 102);

            SeedPackage(migrationBuilder,
                code: "pkg_speaking_ai_pro",
                name: "AI Speaking Credits — Pro (20 credits)",
                description: "AI-marked Speaking credits for the AI examiner. 1 credit per card, 2 per full exam — 20 credits cover 10 full AI Speaking exams. Separate from human-tutor sessions. 180-day validity.",
                price: 30, credits: 20, durationDays: 180, displayOrder: 103);
        }

        private static void SeedPackage(
            MigrationBuilder migrationBuilder,
            string code, string name, string description,
            int price, int credits, int durationDays, int displayOrder)
        {
            var addonId = $"addon_{code}";
            var versionId = $"addonv_{code}_v1";
            var grants = $"{{\"ai_credits\":{credits}}}";
            var nameSql = name.Replace("'", "''");
            var descSql = description.Replace("'", "''");

            migrationBuilder.Sql($@"
INSERT INTO ""BillingAddOns"" (
    ""Id"", ""Code"", ""Name"", ""Description"", ""Price"", ""Currency"", ""Interval"",
    ""Status"", ""IsRecurring"", ""DurationDays"", ""GrantCredits"", ""GrantEntitlementsJson"",
    ""CompatiblePlanCodesJson"", ""ActiveVersionId"", ""LatestVersionId"", ""AppliesToAllPlans"",
    ""IsStackable"", ""QuantityStep"", ""MaxQuantity"", ""DisplayOrder"", ""CreatedAt"", ""UpdatedAt"",
    ""OriginalPriceGbp"", ""AddonKind"", ""RequiresEligibleParent"", ""EligibilityFlag"",
    ""LettersGranted"", ""SessionsGranted"", ""ExtensionDays"", ""AiPackageGroup"", ""AiFeaturesJson""
)
SELECT
    '{addonId}', '{code}', '{nameSql}', '{descSql}',
    {price}, 'GBP', 'one_time',
    1, false, {durationDays}, {credits}, '{grants}',
    '[]', '{versionId}', '{versionId}', false,
    true, 1, NULL, {displayOrder}, now(), now(),
    NULL, 'ai_package', false, '',
    0, 0, 0, 'speaking', '[]'
WHERE NOT EXISTS (
    SELECT 1 FROM ""BillingAddOns"" WHERE ""Code"" = '{code}'
);
");

            migrationBuilder.Sql($@"
INSERT INTO ""BillingAddOnVersions"" (
    ""Id"", ""AddOnId"", ""VersionNumber"", ""Code"", ""Name"", ""Description"", ""Price"",
    ""Currency"", ""Interval"", ""Status"", ""IsRecurring"", ""DurationDays"", ""GrantCredits"",
    ""GrantEntitlementsJson"", ""CompatiblePlanCodesJson"", ""AppliesToAllPlans"", ""IsStackable"",
    ""QuantityStep"", ""MaxQuantity"", ""DisplayOrder"", ""CreatedByAdminId"", ""CreatedByAdminName"",
    ""CreatedAt"", ""OriginalPriceGbp"", ""AddonKind"", ""RequiresEligibleParent"", ""EligibilityFlag"",
    ""LettersGranted"", ""SessionsGranted"", ""ExtensionDays"", ""AiPackageGroup"", ""AiFeaturesJson""
)
SELECT
    '{versionId}', '{addonId}', 1, '{code}', '{nameSql}', '{descSql}',
    {price}, 'GBP', 'one_time', 1, false, {durationDays}, {credits},
    '{grants}', '[]', false, true,
    1, NULL, {displayOrder}, 'system:oet-2026-catalog', 'OET 2026 Catalog Seeder',
    now(), NULL, 'ai_package', false, '',
    0, 0, 0, 'speaking', '[]'
WHERE NOT EXISTS (
    SELECT 1 FROM ""BillingAddOnVersions"" WHERE ""Id"" = '{versionId}'
);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM ""BillingAddOnVersions"" WHERE ""Id"" IN (
    'addonv_pkg_speaking_ai_starter_v1',
    'addonv_pkg_speaking_ai_standard_v1',
    'addonv_pkg_speaking_ai_pro_v1'
);
");
            migrationBuilder.Sql(@"
DELETE FROM ""BillingAddOns"" WHERE ""Code"" IN (
    'pkg_speaking_ai_starter',
    'pkg_speaking_ai_standard',
    'pkg_speaking_ai_pro'
);
");
        }
    }
}
