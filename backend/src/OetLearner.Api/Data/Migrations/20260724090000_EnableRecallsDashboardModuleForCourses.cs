using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260724090000_EnableRecallsDashboardModuleForCourses")]
    public partial class EnableRecallsDashboardModuleForCourses : Migration
    {
        // Surfaces the Recalls module on the learner dashboard for every FULL course
        // the owner listed (medicine, medicine+tutorbook, physiotherapy, allied health,
        // pharmacy, tutor-book, all Full Crash Course packages, all nursing courses).
        //
        // WHY A MIGRATION (not just the JSON seed): the Oet2026CatalogSeeder is DISABLED
        // in production (Content:Oet2026Catalog:Enabled=false), so edits to
        // Data/Seeds/oet-2026-catalog.json never reach the live DB. Prod catalog data is
        // only ever mutated by migrations. Today only full-physiotherapy and
        // full-allied-health carry "Recalls" in their live DashboardModulesJson (they were
        // INSERTed with it by 20260711090000_PublishDisciplineFullCourses); the other
        // listed courses were seeded before "Recalls" was added to the manifest, so their
        // live DashboardModulesJson lacks it and the Recalls tile does not render for them.
        //
        // SCOPE: this ONLY affects the dashboard module tile / catalog "what's included"
        // display. Recalls CONTENT access is gated elsewhere purely on an eligible,
        // non-frozen subscription (RecallsEndpoints ResolveIsPremiumAsync /
        // RequireRecallEnrolmentAsync -> EffectiveEntitlementResolver.HasEligibleSubscription)
        // and never reads DashboardModulesJson — so holders of these courses already receive
        // full recalls content today; this migration just makes the module visible.
        //
        // SAFETY:
        //   * Additive only. Appends "Recalls" and touches nothing else — never resets the
        //     array, so any other modules (incl. admin edits) are preserved.
        //   * Crash-proof. The WHERE clause uses only string LIKE tests, so no row is ever
        //     cast to jsonb unless it already looks like a JSON array; the jsonb concat in
        //     SET only evaluates for rows that passed WHERE. A malformed/empty-non-array
        //     value simply won't match and is left untouched (never fails the deploy).
        //   * Idempotent. NOT LIKE '%"Recalls"%' skips rows that already have it, so re-runs
        //     are no-ops.
        //   * Scoped. Only the 12 owner-listed plan codes are ever in the WHERE set; no other
        //     course is affected, and no course ever loses a module.
        //   * Mirrors BillingPlans + its immutable BillingPlanVersions snapshot (kept in sync
        //     by the seeder's CopyPlanIntoVersion), matching 20260711090000.
        //
        // Data/Seeds/oet-2026-catalog.json is updated in the same commit (adds "Recalls" to
        // tutor-book's dashboardModules — the only listed plan whose JSON source lacked it;
        // the other 11 already list it) so a future dev/test reseed converges.

        // All 12 owner-listed FULL courses. Up() is guarded, so physiotherapy/allied-health
        // (which already carry "Recalls") are harmless no-ops.
        private const string AllListedCodes =
            "'full-condensed-medicine','full-condensed-medicine-tbook','full-physiotherapy'," +
            "'full-allied-health','full-pharmacy','tutor-book'," +
            "'crash-course','crash-3letters','crash-5letters'," +
            "'full-nursing','full-nursing-assessment','full-nursing-premium'";

        // The 10 codes this migration actually adds "Recalls" to (physiotherapy and
        // allied-health already had it from 20260711090000). Down() reverts only these,
        // so it never strips "Recalls" from the two courses that legitimately shipped with it.
        private const string AddedByThisMigrationCodes =
            "'full-condensed-medicine','full-condensed-medicine-tbook','full-pharmacy','tutor-book'," +
            "'crash-course','crash-3letters','crash-5letters'," +
            "'full-nursing','full-nursing-assessment','full-nursing-premium'";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
UPDATE ""BillingPlans""
SET ""DashboardModulesJson"" = CAST(
        CAST(""DashboardModulesJson"" AS jsonb) || CAST('[""Recalls""]' AS jsonb) AS text),
    ""UpdatedAt"" = now()
WHERE ""Code"" IN ({AllListedCodes})
  AND ""DashboardModulesJson"" LIKE '[%]'
  AND ""DashboardModulesJson"" NOT LIKE '%""Recalls""%';
");

            migrationBuilder.Sql($@"
UPDATE ""BillingPlanVersions""
SET ""DashboardModulesJson"" = CAST(
        CAST(""DashboardModulesJson"" AS jsonb) || CAST('[""Recalls""]' AS jsonb) AS text)
WHERE ""Code"" IN ({AllListedCodes})
  AND ""DashboardModulesJson"" LIKE '[%]'
  AND ""DashboardModulesJson"" NOT LIKE '%""Recalls""%';
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
UPDATE ""BillingPlans""
SET ""DashboardModulesJson"" = CAST(
        CAST(""DashboardModulesJson"" AS jsonb) - 'Recalls' AS text),
    ""UpdatedAt"" = now()
WHERE ""Code"" IN ({AddedByThisMigrationCodes})
  AND ""DashboardModulesJson"" LIKE '%""Recalls""%';
");

            migrationBuilder.Sql($@"
UPDATE ""BillingPlanVersions""
SET ""DashboardModulesJson"" = CAST(
        CAST(""DashboardModulesJson"" AS jsonb) - 'Recalls' AS text)
WHERE ""Code"" IN ({AddedByThisMigrationCodes})
  AND ""DashboardModulesJson"" LIKE '%""Recalls""%';
");
        }
    }
}
