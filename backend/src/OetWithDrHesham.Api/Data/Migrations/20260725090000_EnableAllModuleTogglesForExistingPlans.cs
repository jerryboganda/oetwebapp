using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260725090000_EnableAllModuleTogglesForExistingPlans")]
    public partial class EnableAllModuleTogglesForExistingPlans : Migration
    {
        // Owner directive 2026-07-11: the admin Pricing "Edit plan" dialog gains four
        // Enable/Disable module toggles — Recalls, Materials, Videos, Mocks — that now gate BOTH
        // the learner navigation/tiles AND real backend access (RecallsEndpoints,
        // VideoEntitlementService, MockEntitlementService, MaterialAccessService all read
        // EffectiveEntitlementSnapshot.IsModuleEnabled).
        //
        // The owner chose "All ON, then disable per plan": every EXISTING plan must start with all
        // four modules enabled so no current subscriber loses access the moment enforcement ships.
        // This migration back-fills the four canonical PascalCase module keys into every plan's
        // DashboardModulesJson (Recalls + MaterialsLibrary already exist on many plans; VideoLibrary
        // + Mocks are new keys with no prior presence).
        //
        // WHY A MIGRATION (not the JSON seed): the Oet2026CatalogSeeder is DISABLED in production
        // (Content:Oet2026Catalog:Enabled=false), so prod catalog rows are only ever mutated by
        // migrations. Mirrors 20260724090000_EnableRecallsDashboardModuleForCourses.
        //
        // SAFETY (identical guarantees to 20260724090000):
        //   * Additive only — appends missing keys, never resets the array, so admin edits and any
        //     other modules already present are preserved.
        //   * Crash-proof — the WHERE clause uses only string LIKE tests, so a row is cast to jsonb
        //     only when it already looks like a JSON array; malformed / null / non-array values are
        //     left untouched and never fail the deploy (they read fail-open at runtime).
        //   * Idempotent — NOT LIKE '%"<key>"%' skips rows that already carry the key, so re-runs
        //     and keys already present (e.g. Recalls, MaterialsLibrary) are no-ops.
        //   * Mirrors BillingPlans + its immutable BillingPlanVersions snapshot, matching
        //     20260711090000 / 20260724090000.
        //
        // Down() is intentionally a NO-OP: this backfill cannot be cleanly reversed because it does
        // not track which keys were pre-existing vs added (Recalls / MaterialsLibrary legitimately
        // predate it on most plans), and blindly stripping them would remove modules that were never
        // ours to remove. Data backfills of this kind are forward-only in production.

        private static readonly string[] ModuleKeys =
            { "Recalls", "MaterialsLibrary", "VideoLibrary", "Mocks" };

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var key in ModuleKeys)
            {
                migrationBuilder.Sql($@"
UPDATE ""BillingPlans""
SET ""DashboardModulesJson"" = CAST(
        CAST(""DashboardModulesJson"" AS jsonb) || CAST('[""{key}""]' AS jsonb) AS text),
    ""UpdatedAt"" = now()
WHERE ""DashboardModulesJson"" LIKE '[%]'
  AND ""DashboardModulesJson"" NOT LIKE '%""{key}""%';
");

                migrationBuilder.Sql($@"
UPDATE ""BillingPlanVersions""
SET ""DashboardModulesJson"" = CAST(
        CAST(""DashboardModulesJson"" AS jsonb) || CAST('[""{key}""]' AS jsonb) AS text)
WHERE ""DashboardModulesJson"" LIKE '[%]'
  AND ""DashboardModulesJson"" NOT LIKE '%""{key}""%';
");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only: see class remarks. No-op by design.
        }
    }
}
