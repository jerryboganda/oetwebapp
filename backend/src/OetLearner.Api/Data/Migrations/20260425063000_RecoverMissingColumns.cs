using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>MediaAssets.UploadedBy</c> which is declared in the model
    /// snapshot but missing from any prior migration, causing 500s on
    /// <c>/v1/listening/home</c> with <c>42703: column m.UploadedBy does not exist</c>.
    /// Idempotent so it is safe across environments.
    /// </summary>
    public partial class RecoverMissingColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""MediaAssets"" ADD COLUMN IF NOT EXISTS ""UploadedBy"" character varying(64) NULL;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recovery migration; non-destructive.
        }
    }
}
