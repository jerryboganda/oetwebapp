using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260513130000_AddMockLiveRoomTransitions")]
    public partial class AddMockLiveRoomTransitions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "MockBookings"
                ADD COLUMN IF NOT EXISTS "LiveRoomTransitionVersion" integer NOT NULL DEFAULT 0;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "MockLiveRoomTransitions" (
                    "Id" varchar(64) NOT NULL,
                    "BookingId" varchar(64) NOT NULL,
                    "ActorId" varchar(64) NOT NULL,
                    "ActorRole" varchar(32) NOT NULL,
                    "FromState" varchar(32) NOT NULL,
                    "ToState" varchar(32) NOT NULL,
                    "Reason" varchar(512) NULL,
                    "ClientTransitionId" varchar(96) NULL,
                    "TransitionVersion" integer NOT NULL,
                    "OccurredAt" timestamptz NOT NULL,
                    "MetadataJson" text NOT NULL DEFAULT '{}',
                    CONSTRAINT "PK_MockLiveRoomTransitions" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_MockLiveRoomTransitions_MockBookings_BookingId"
                        FOREIGN KEY ("BookingId") REFERENCES "MockBookings" ("Id") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_MockLiveRoomTransitions_BookingId_OccurredAt"
                ON "MockLiveRoomTransitions" ("BookingId", "OccurredAt");
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_MockLiveRoomTransitions_BookingId_TransitionVersion"
                ON "MockLiveRoomTransitions" ("BookingId", "TransitionVersion");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_MockLiveRoomTransitions_BookingId_ClientTransitionId"
                ON "MockLiveRoomTransitions" ("BookingId", "ClientTransitionId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_MockLiveRoomTransitions_BookingId_ClientTransitionId";
                """);
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_MockLiveRoomTransitions_BookingId_TransitionVersion";
                """);
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_MockLiveRoomTransitions_BookingId_OccurredAt";
                """);
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS "MockLiveRoomTransitions";
                """);
            migrationBuilder.Sql("""
                ALTER TABLE "MockBookings" DROP COLUMN IF EXISTS "LiveRoomTransitionVersion";
                """);
        }
    }
}