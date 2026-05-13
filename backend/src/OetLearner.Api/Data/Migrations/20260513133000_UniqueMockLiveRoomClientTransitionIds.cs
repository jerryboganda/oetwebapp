using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260513133000_UniqueMockLiveRoomClientTransitionIds")]
    public partial class UniqueMockLiveRoomClientTransitionIds : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_MockLiveRoomTransitions_BookingId_ClientTransitionId";
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_MockLiveRoomTransitions_BookingId_ClientTransitionId"
                ON "MockLiveRoomTransitions" ("BookingId", "ClientTransitionId")
                WHERE "ClientTransitionId" IS NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_MockLiveRoomTransitions_BookingId_ClientTransitionId";
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_MockLiveRoomTransitions_BookingId_ClientTransitionId"
                ON "MockLiveRoomTransitions" ("BookingId", "ClientTransitionId");
                """);
        }
    }
}
