using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Slice C — billing-hardening (May 2026).
    /// Adds defensive indexes used by the catalog hardening pass:
    ///   • BillingCoupons (Status, EndsAt) — already in OnModelCreating, but
    ///     we re-issue with `IF NOT EXISTS` for environments that drifted
    ///     before the model snapshot picked it up.
    ///   • BillingCouponRedemptions (CouponId, Status) — supports the
    ///     race-safe redemption-counter projection.
    ///   • BillingCouponRedemptions (CouponCode, Status) — supports legacy
    ///     code-keyed lookups during the dual-write transition.
    ///
    /// All statements use `CREATE INDEX IF NOT EXISTS …` syntax which is
    /// supported by both PostgreSQL 9.5+ and SQLite 3.8+. No destructive
    /// schema changes are issued — the migration is fully additive and
    /// reversible.
    /// </summary>
    public partial class HardenCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_BillingCoupons_Status_EndsAt"
                ON "BillingCoupons" ("Status", "EndsAt");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_BillingCouponRedemptions_CouponId_Status"
                ON "BillingCouponRedemptions" ("CouponId", "Status");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_BillingCouponRedemptions_CouponCode_Status"
                ON "BillingCouponRedemptions" ("CouponCode", "Status");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_BillingCouponRedemptions_CouponCode_Status";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_BillingCouponRedemptions_CouponId_Status";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_BillingCoupons_Status_EndsAt";""");
        }
    }
}
