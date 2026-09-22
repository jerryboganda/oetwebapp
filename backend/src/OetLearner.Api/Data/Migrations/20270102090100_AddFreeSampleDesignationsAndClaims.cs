using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    // HAND-AUTHORED (repo convention): contains ONLY the new objects. Raw
    // idempotent SQL so a dev database that was created with EnsureCreated
    // does not fail with "already exists". The ModelSnapshot IS updated (see
    // PlacementAccommodations, 20270102090000) so `dotnet ef migrations
    // has-pending-model-changes` stays clean.
    // Free Mocks (owner 2026-09-22): per-profession free-sample designation and
    // the per-learner once-only claim (UNIQUE(UserId, Subtest)).
    // Same-day second migration (+0100): 20270102090000 was independently taken
    // by PlacementAccommodations (PR #234) before this one merged.
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270102090100_AddFreeSampleDesignationsAndClaims")]
    public partial class AddFreeSampleDesignationsAndClaims : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "FreeSampleDesignations" (
                    "Id" character varying(64) NOT NULL,
                    "Subtest" character varying(16) NOT NULL,
                    "Profession" character varying(32) NOT NULL,
                    "ContentId" character varying(64) NOT NULL,
                    "UpdatedByAdminId" character varying(64) NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_FreeSampleDesignations" PRIMARY KEY ("Id")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_FreeSampleDesignations_Subtest_Profession"
                    ON "FreeSampleDesignations" ("Subtest", "Profession");

                CREATE TABLE IF NOT EXISTS "FreeSampleClaims" (
                    "Id" character varying(64) NOT NULL,
                    "UserId" character varying(64) NOT NULL,
                    "Subtest" character varying(16) NOT NULL,
                    "Profession" character varying(32) NOT NULL,
                    "ContentId" character varying(64) NOT NULL,
                    "AttemptId" character varying(64) NULL,
                    "ClaimedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_FreeSampleClaims" PRIMARY KEY ("Id")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_FreeSampleClaims_UserId_Subtest"
                    ON "FreeSampleClaims" ("UserId", "Subtest");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FreeSampleClaims");
            migrationBuilder.DropTable(name: "FreeSampleDesignations");
        }
    }
}
