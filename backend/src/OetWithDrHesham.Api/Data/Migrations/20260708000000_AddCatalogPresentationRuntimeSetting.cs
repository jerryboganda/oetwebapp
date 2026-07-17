using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Adds the admin-configurable <c>RuntimeSettings.CatalogPresentationJson</c>
    /// column (text, nullable). It holds the catalog "storefront" presentation
    /// document — hero copy, category labels/order/visibility, legend, section
    /// toggles, CTA, accent, plus a per-plan / per-add-on presentation overlay
    /// keyed by product code. Read by <c>GET /v1/catalog/pricing</c> and edited
    /// via the admin storefront editor. Null = client uses built-in defaults, so
    /// there is zero behaviour change until an admin writes a value.
    ///
    /// Hand-written idempotent SQL (house style); kept in exact sync with
    /// <c>RuntimeSettingsSchemaSelfHeal</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260708000000_AddCatalogPresentationRuntimeSetting")]
    public partial class AddCatalogPresentationRuntimeSetting : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CatalogPresentationJson"" text;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""CatalogPresentationJson"";
");
        }
    }
}
