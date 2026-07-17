using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// WS1 — server-authoritative Speaking session clock & two-role-play mock
    /// pairing (Developer Implementation Notes §1.2, §13.3, §22.5).
    ///
    /// <para><b>Columns added to existing <c>SpeakingSessions</c> table</b></para>
    /// <list type="bullet">
    ///   <item>Per-role-play timestamps <c>Rp1PrepStartedAt</c> /
    ///   <c>Rp1StartedAt</c> / <c>Rp1EndedAt</c> and the <c>Rp2*</c> trio so a
    ///   two-role-play strict mock can record both halves on one session row
    ///   for post-hoc timing audit.</item>
    ///   <item><c>SubmittedAt</c> — distinct from <c>EndedAt</c>; stamps the
    ///   two-recording submission gate (§14.2).</item>
    ///   <item><c>TechnicalIssueFlag</c> / <c>TechnicalIssueNote</c> — §22.5
    ///   technical-issue reporting (never affects scoring).</item>
    /// </list>
    ///
    /// <para>
    /// Hand-written following the <c>AddSpeakingModeration</c> pattern. Uses
    /// <c>ADD COLUMN IF NOT EXISTS</c> for idempotency. ⚠ <b>Postgres-only
    /// SQL.</b> The test suite uses SQLite via <c>EnsureCreatedAsync()</c> and
    /// builds straight from the model, bypassing migrations entirely.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260602090000_AddSpeakingSessionClock")]
    public partial class AddSpeakingSessionClock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""SpeakingSessions""
                    ADD COLUMN IF NOT EXISTS ""Rp1PrepStartedAt"" timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS ""Rp1StartedAt""     timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS ""Rp1EndedAt""       timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS ""Rp2PrepStartedAt"" timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS ""Rp2StartedAt""     timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS ""Rp2EndedAt""       timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS ""SubmittedAt""      timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS ""TechnicalIssueFlag"" boolean NOT NULL DEFAULT FALSE,
                    ADD COLUMN IF NOT EXISTS ""TechnicalIssueNote"" character varying(1000) NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""SpeakingSessions""
                    DROP COLUMN IF EXISTS ""Rp1PrepStartedAt"",
                    DROP COLUMN IF EXISTS ""Rp1StartedAt"",
                    DROP COLUMN IF EXISTS ""Rp1EndedAt"",
                    DROP COLUMN IF EXISTS ""Rp2PrepStartedAt"",
                    DROP COLUMN IF EXISTS ""Rp2StartedAt"",
                    DROP COLUMN IF EXISTS ""Rp2EndedAt"",
                    DROP COLUMN IF EXISTS ""SubmittedAt"",
                    DROP COLUMN IF EXISTS ""TechnicalIssueFlag"",
                    DROP COLUMN IF EXISTS ""TechnicalIssueNote"";
            ");
        }
    }
}
