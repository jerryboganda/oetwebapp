using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Placement test: admin-approved extra-time accommodations. Creates
    /// <c>PlacementAccommodations</c> (the grant: who approved it, when, how
    /// much, revocation trail) and <c>PlacementAccommodationUses</c> (which
    /// placement session ran with a grant; unique per session).
    ///
    /// HAND-AUTHORED (repo convention, see 20261231090000): inline
    /// <c>[Migration]</c>/<c>[DbContext]</c>, no Designer file (a migration
    /// without <c>BuildTargetModel</c> is valid); the ModelSnapshot IS updated
    /// so <c>dotnet ef migrations has-pending-model-changes</c> stays clean.
    /// Idempotent (IF NOT EXISTS) so it is safe against a database where the
    /// tables were already created out-of-band.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270102090000_PlacementAccommodations")]
    public partial class PlacementAccommodations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "PlacementAccommodations" (
                    "Id" character varying(64) NOT NULL,
                    "LearnerUserId" character varying(64) NOT NULL,
                    "ExtraTimePercent" integer NOT NULL,
                    "Reference" character varying(200) NULL,
                    "ApprovedByUserId" character varying(64) NOT NULL,
                    "ApprovedByName" character varying(128) NOT NULL,
                    "ApprovedAt" timestamp with time zone NOT NULL,
                    "RevokedByUserId" character varying(64) NULL,
                    "RevokedByName" character varying(128) NULL,
                    "RevokedAt" timestamp with time zone NULL,
                    "RevokedReason" character varying(200) NULL,
                    CONSTRAINT "PK_PlacementAccommodations" PRIMARY KEY ("Id")
                );
                CREATE INDEX IF NOT EXISTS "IX_PlacementAccommodations_LearnerUserId_ApprovedAt" ON "PlacementAccommodations" ("LearnerUserId", "ApprovedAt");
                CREATE TABLE IF NOT EXISTS "PlacementAccommodationUses" (
                    "Id" character varying(64) NOT NULL,
                    "AccommodationId" character varying(64) NOT NULL,
                    "LearnerUserId" character varying(64) NOT NULL,
                    "SessionId" character varying(64) NOT NULL,
                    "ExtraTimePercent" integer NOT NULL,
                    "AppliedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_PlacementAccommodationUses" PRIMARY KEY ("Id")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_PlacementAccommodationUses_SessionId" ON "PlacementAccommodationUses" ("SessionId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS "PlacementAccommodationUses";
                DROP TABLE IF EXISTS "PlacementAccommodations";
                """);
        }
    }
}
