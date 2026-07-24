using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260808090000_AlignPlanModulesToPricingList")]
    public partial class AlignPlanModulesToPricingList : Migration
    {
        private const string RecallPlanCodes =
            "'full-condensed-medicine','full-condensed-medicine-tbook'," +
            "'full-nursing','full-nursing-assessment','full-nursing-premium'," +
            "'full-pharmacy','full-physiotherapy','full-allied-health'," +
            "'crash-course','crash-3letters','crash-5letters'";

        private const string RecordedCoursePlanCodes =
            "'full-condensed-medicine','full-condensed-medicine-tbook'," +
            "'full-nursing','full-nursing-assessment','full-nursing-premium'," +
            "'full-pharmacy','full-physiotherapy','full-allied-health','basic-english'," +
            "'crash-course','crash-3letters','crash-5letters'," +
            "'writing-crash','writing-crash-2','writing-crash-3','writing-crash-5'," +
            "'writing-crash-7','writing-crash-10','speaking-crash'," +
            "'double-special','mega-special'";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Undo the broad "all modules on" backfill. Unknown/legacy plans fail closed;
            // canonical products are added back below according to the approved pricing list.
            foreach (var key in new[] { "Mocks", "Recalls", "MaterialsLibrary", "VideoLibrary" })
            {
                RemoveModule(migrationBuilder, "BillingPlans", key, updateTimestamp: true);
                RemoveModule(migrationBuilder, "BillingPlanVersions", key, updateTimestamp: false);
            }

            AddModule(migrationBuilder, "BillingPlans", "Recalls", RecallPlanCodes, updateTimestamp: true);
            AddModule(migrationBuilder, "BillingPlanVersions", "Recalls", RecallPlanCodes, updateTimestamp: false);

            foreach (var key in new[] { "MaterialsLibrary", "VideoLibrary" })
            {
                AddModule(migrationBuilder, "BillingPlans", key, RecordedCoursePlanCodes, updateTimestamp: true);
                AddModule(migrationBuilder, "BillingPlanVersions", key, RecordedCoursePlanCodes, updateTimestamp: false);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only business-policy correction. Re-enabling Mocks on subscriptions
            // would violate the separate-purchase rule.
        }

        private static void RemoveModule(
            MigrationBuilder migrationBuilder,
            string table,
            string key,
            bool updateTimestamp)
        {
            var timestamp = updateTimestamp ? @", ""UpdatedAt"" = now()" : string.Empty;
            migrationBuilder.Sql($@"
UPDATE ""{table}""
SET ""DashboardModulesJson"" = CAST(CAST(""DashboardModulesJson"" AS jsonb) - '{key}' AS text){timestamp}
WHERE ""DashboardModulesJson"" LIKE '[%]'
  AND ""DashboardModulesJson"" LIKE '%""{key}""%';
");
        }

        private static void AddModule(
            MigrationBuilder migrationBuilder,
            string table,
            string key,
            string planCodes,
            bool updateTimestamp)
        {
            var timestamp = updateTimestamp ? @", ""UpdatedAt"" = now()" : string.Empty;
            migrationBuilder.Sql($@"
UPDATE ""{table}""
SET ""DashboardModulesJson"" = CAST(
        CAST(""DashboardModulesJson"" AS jsonb) || CAST('[""{key}""]' AS jsonb) AS text){timestamp}
WHERE ""Code"" IN ({planCodes})
  AND ""DashboardModulesJson"" LIKE '[%]'
  AND ""DashboardModulesJson"" NOT LIKE '%""{key}""%';
");
        }
    }
}
