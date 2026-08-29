using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// W8 — mock attempt reservation state + unique (UserId, MockAttemptId).
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261205090000_AddMockLifecycleReservation")]
    public partial class AddMockLifecycleReservation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "MockEntitlementLedgers" ADD COLUMN IF NOT EXISTS "ReservationState" character varying(16) NOT NULL DEFAULT 'committed';

                UPDATE "MockEntitlementLedgers"
                SET "ReservationState" = 'committed'
                WHERE "ReservationState" IS NULL OR btrim("ReservationState") = '';

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_MockEntitlementLedgers_User_Attempt"
                    ON "MockEntitlementLedgers" ("UserId", "MockAttemptId")
                    WHERE "MockAttemptId" IS NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "UX_MockEntitlementLedgers_User_Attempt";
                ALTER TABLE "MockEntitlementLedgers" DROP COLUMN IF EXISTS "ReservationState";
                """);
        }
    }
}
