using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>AiPackageGroup</c> and <c>AiFeaturesJson</c> to <c>BillingAddOns</c> and
    /// <c>BillingAddOnVersions</c>. These make an <c>ai_package</c> add-on's storefront
    /// group and feature bullets admin-configurable instead of derived from the code
    /// prefix / auto-generated copy. Hand-written idempotent SQL (house style) so it is
    /// safe on fresh and existing databases. Strictly additive: empty defaults keep the
    /// existing seeded packages rendering exactly as before via the code fallbacks.
    /// Presentational only — never read at checkout/fulfillment.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260703090000_AddAiPackageGroupAndFeatures")]
    public partial class AddAiPackageGroupAndFeatures : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""BillingAddOns"" ADD COLUMN IF NOT EXISTS ""AiPackageGroup"" character varying(32) NOT NULL DEFAULT '';");
            migrationBuilder.Sql(@"ALTER TABLE ""BillingAddOns"" ADD COLUMN IF NOT EXISTS ""AiFeaturesJson"" character varying(4096) NOT NULL DEFAULT '[]';");
            migrationBuilder.Sql(@"ALTER TABLE ""BillingAddOnVersions"" ADD COLUMN IF NOT EXISTS ""AiPackageGroup"" character varying(32) NOT NULL DEFAULT '';");
            migrationBuilder.Sql(@"ALTER TABLE ""BillingAddOnVersions"" ADD COLUMN IF NOT EXISTS ""AiFeaturesJson"" character varying(4096) NOT NULL DEFAULT '[]';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""BillingAddOns"" DROP COLUMN IF EXISTS ""AiPackageGroup"";");
            migrationBuilder.Sql(@"ALTER TABLE ""BillingAddOns"" DROP COLUMN IF EXISTS ""AiFeaturesJson"";");
            migrationBuilder.Sql(@"ALTER TABLE ""BillingAddOnVersions"" DROP COLUMN IF EXISTS ""AiPackageGroup"";");
            migrationBuilder.Sql(@"ALTER TABLE ""BillingAddOnVersions"" DROP COLUMN IF EXISTS ""AiFeaturesJson"";");
        }
    }
}
