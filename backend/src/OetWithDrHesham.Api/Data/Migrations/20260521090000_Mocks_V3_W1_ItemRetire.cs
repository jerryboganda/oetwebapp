using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Mocks Module Phase 6 — adds three nullable retire-tracking columns to
    /// <c>MockItemAnalysisSnapshots</c>:
    /// <list type="bullet">
    ///   <item><c>RetiredAt</c> — UTC timestamp the item was withdrawn.</item>
    ///   <item><c>RetiredReason</c> — free-text rationale captured at retire.</item>
    ///   <item><c>RetiredByAdminId</c> — admin sub claim that triggered retire.</item>
    /// </list>
    /// Drives the new <c>PATCH /v1/admin/mocks/items/{itemId}</c> endpoint that
    /// the admin item-analysis dashboard calls to retire flagged items. Retire
    /// is a soft action: the snapshot row is preserved so historical analytics
    /// remain reproducible.
    ///
    /// <para>
    /// Hand-written to match the <c>Mocks_V2_W6_BookingRecording</c> pattern
    /// because the EF Core model snapshot has historically drifted in this
    /// repo. All three columns are nullable so the migration is a strictly
    /// additive change against existing rows.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Postgres-only SQL.</b> Uses <c>ADD COLUMN IF NOT EXISTS</c>. The
    /// test suite uses SQLite via <c>EnsureCreatedAsync()</c> and bypasses
    /// migrations entirely, so this is safe today.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260521090000_Mocks_V3_W1_ItemRetire")]
    public partial class Mocks_V3_W1_ItemRetire : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""MockItemAnalysisSnapshots""
                    ADD COLUMN IF NOT EXISTS ""RetiredAt"" timestamp with time zone NULL;
                ALTER TABLE ""MockItemAnalysisSnapshots""
                    ADD COLUMN IF NOT EXISTS ""RetiredReason"" character varying(512) NULL;
                ALTER TABLE ""MockItemAnalysisSnapshots""
                    ADD COLUMN IF NOT EXISTS ""RetiredByAdminId"" character varying(64) NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""MockItemAnalysisSnapshots"" DROP COLUMN IF EXISTS ""RetiredByAdminId"";
                ALTER TABLE ""MockItemAnalysisSnapshots"" DROP COLUMN IF EXISTS ""RetiredReason"";
                ALTER TABLE ""MockItemAnalysisSnapshots"" DROP COLUMN IF EXISTS ""RetiredAt"";
            ");
        }
    }
}
