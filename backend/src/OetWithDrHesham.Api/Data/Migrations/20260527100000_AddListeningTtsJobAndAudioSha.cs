using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260527100000_AddListeningTtsJobAndAudioSha")]
    public partial class AddListeningTtsJobAndAudioSha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Wave 4 deferred — SHA-256 of TTS-synthesised audio on each extract.
            migrationBuilder.Sql("""
                ALTER TABLE "ListeningExtracts"
                    ADD COLUMN IF NOT EXISTS "AudioContentSha" character varying(64) NULL;
                """);

            // Wave 4 deferred — TTS background job queue table.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "ListeningTtsJobs" (
                    "Id"           character varying(64)   NOT NULL,
                    "ExtractId"    character varying(64)   NOT NULL,
                    "RequestedBy"  character varying(64)   NOT NULL,
                    "Status"       integer                 NOT NULL DEFAULT 0,
                    "RetryCount"   integer                 NOT NULL DEFAULT 0,
                    "ErrorMessage" character varying(2048) NULL,
                    "RetryAfter"   timestamp with time zone NULL,
                    "CreatedAt"    timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt"    timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT "PK_ListeningTtsJobs" PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_ListeningTtsJobs_Status_CreatedAt"
                    ON "ListeningTtsJobs" ("Status", "CreatedAt");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_ListeningTtsJobs_ExtractId"
                    ON "ListeningTtsJobs" ("ExtractId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "ListeningTtsJobs";""");
            migrationBuilder.Sql("""
                ALTER TABLE "ListeningExtracts"
                    DROP COLUMN IF EXISTS "AudioContentSha";
                """);
        }
    }
}
