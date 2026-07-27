using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Owner-directed reprice of the three Full Mock exam packages (data only, no schema change).
/// 1 Full Mock £19 → £25, 3 Full Mocks £45 → £60, 5 Full Mocks £67 → £110.
/// The Oet2026CatalogSeeder is DISABLED in production, so catalog changes ship as a migration —
/// Data/Seeds/oet-2026-catalog.json is updated in the same commit for dev/test reseed parity.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260823090000_UpdateFullMockPackagePricing")]
public partial class UpdateFullMockPackagePricing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        SetAddOnPrice(migrationBuilder, "pkg_mock_1", 25);
        SetAddOnPrice(migrationBuilder, "pkg_mock_3", 60);
        SetAddOnPrice(migrationBuilder, "pkg_mock_5", 110);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        SetAddOnPrice(migrationBuilder, "pkg_mock_1", 19);
        SetAddOnPrice(migrationBuilder, "pkg_mock_3", 45);
        SetAddOnPrice(migrationBuilder, "pkg_mock_5", 67);
    }

    private static void SetAddOnPrice(MigrationBuilder mb, string code, int price)
    {
        mb.Sql($@"UPDATE ""BillingAddOns"" SET ""Price"" = {price}, ""UpdatedAt"" = now() WHERE ""Code"" = '{code}';");
        mb.Sql($@"UPDATE ""BillingAddOnVersions"" SET ""Price"" = {price} WHERE ""Code"" = '{code}';");
    }
}
