using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Slice A — May 2026 billing hardening, Track C closure (2026-05-12).
    ///
    /// Adds a nullable cross-DB <c>RowVersion</c> rowversion column on
    /// <c>Wallets</c> so SQLite + in-memory test providers see the same
    /// optimistic-concurrency surface as production PostgreSQL (which already
    /// uses <c>xmin</c> for row-versioning indirectly via the
    /// <c>ConfigureXminToken</c> path on related billing entities).
    ///
    /// Additive-only:
    ///   • Column nullable (<c>bytea NULL</c>) so existing rows survive.
    ///   • Wrapped in <c>IF NOT EXISTS</c> for idempotency; safe to re-run.
    ///   • No backfill; EF assigns a fresh shadow value on the next write.
    ///
    /// Migration timestamp 20260512100000 sequences after the in-flight
    /// 20260511110000_Listening_V2_Schema migration so the deterministic
    /// migration apply order is preserved.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260512100000_AddWalletRowVersion")]
    public partial class AddWalletRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Wallets"
                ADD COLUMN IF NOT EXISTS "RowVersion" bytea NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Wallets" DROP COLUMN IF EXISTS "RowVersion";
                """);
        }
    }
}
