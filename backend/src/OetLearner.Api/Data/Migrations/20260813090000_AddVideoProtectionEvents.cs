using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Creates <c>VideoProtectionEvents</c> — capture-protection / tamper
    /// telemetry from the video player (Course Platform Security Requirements
    /// §2). Plain heap table (unlike SecurityEvents, not range-partitioned —
    /// lower expected volume). Also adds the
    /// <c>VideoProtectionRevokeOnCaptureDetected</c> RuntimeSettings toggle.
    /// Hand-written Postgres-only migration; SQLite/InMemory test runs bypass
    /// migrations via <c>EnsureCreatedAsync()</c> and read the schema from the
    /// entity model directly (see LearnerDbContext.VideoLibrary.cs).
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260813090000_AddVideoProtectionEvents")]
    public partial class AddVideoProtectionEvents : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""VideoProtectionEvents"" (
    ""Id"" uuid NOT NULL,
    ""UserId"" character varying(64) NOT NULL,
    ""VideoId"" character varying(64),
    ""SessionId"" character varying(64),
    ""Kind"" character varying(48) NOT NULL,
    ""Severity"" character varying(16) NOT NULL DEFAULT 'info',
    ""Platform"" character varying(32),
    ""IpAddress"" character varying(64),
    ""OccurredAt"" timestamp with time zone NOT NULL,
    ""MetadataJson"" jsonb NOT NULL DEFAULT '{}',
    CONSTRAINT ""PK_VideoProtectionEvents"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_VideoProtectionEvents_UserId_OccurredAt"" ON ""VideoProtectionEvents"" (""UserId"", ""OccurredAt"");
CREATE INDEX IF NOT EXISTS ""IX_VideoProtectionEvents_SessionId"" ON ""VideoProtectionEvents"" (""SessionId"");
CREATE INDEX IF NOT EXISTS ""IX_VideoProtectionEvents_Kind"" ON ""VideoProtectionEvents"" (""Kind"");

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""VideoProtectionRevokeOnCaptureDetected"" boolean;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS ""VideoProtectionEvents"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""VideoProtectionRevokeOnCaptureDetected"";
");
        }
    }
}
