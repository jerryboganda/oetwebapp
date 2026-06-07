using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>ExtensionDays</c> to <c>BillingAddOns</c> and <c>BillingAddOnVersions</c>.
    /// Used by the <c>access_extension</c> add-on kind to push a course
    /// <c>Subscription.ExpiresAt</c> out by N days. Hand-written idempotent SQL
    /// (matches the house style of the sibling billing/writing migrations) so it
    /// applies cleanly on fresh and existing databases and is safe to re-run.
    /// Strictly additive: defaults to 0, a no-op for every existing add-on.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260627090000_AddBillingAddOnExtensionDays")]
    public partial class AddBillingAddOnExtensionDays : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""BillingAddOns"" ADD COLUMN IF NOT EXISTS ""ExtensionDays"" integer NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(@"ALTER TABLE ""BillingAddOnVersions"" ADD COLUMN IF NOT EXISTS ""ExtensionDays"" integer NOT NULL DEFAULT 0;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""BillingAddOns"" DROP COLUMN IF EXISTS ""ExtensionDays"";");
            migrationBuilder.Sql(@"ALTER TABLE ""BillingAddOnVersions"" DROP COLUMN IF EXISTS ""ExtensionDays"";");
        }
    }
}
