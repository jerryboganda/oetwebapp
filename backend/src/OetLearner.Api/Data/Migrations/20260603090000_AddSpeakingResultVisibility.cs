using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// WS6 — Speaking result-visibility config (Developer Implementation Notes
    /// §10). Creates the <c>SpeakingResultVisibilityConfigs</c> table: a single
    /// global singleton row (<c>Id == "global"</c>) plus optional per-role-play-card
    /// override rows keyed by <c>RolePlayCardId</c>. Mirrors the Writing
    /// result-visibility table shipped in <c>AddWritingExamModuleClosure</c>.
    ///
    /// <para>
    /// Hand-written following the <c>AddSpeakingSessionClock</c> pattern. Uses
    /// <c>CREATE TABLE IF NOT EXISTS</c> for idempotency. ⚠ <b>Postgres-only
    /// SQL.</b> The test suite uses SQLite via <c>EnsureCreatedAsync()</c> and
    /// builds straight from the model, bypassing migrations entirely.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260603090000_AddSpeakingResultVisibility")]
    public partial class AddSpeakingResultVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""SpeakingResultVisibilityConfigs"" (
                    ""Id"" character varying(64) NOT NULL,
                    ""RolePlayCardId"" character varying(64) NULL,
                    ""ShowSubmissionReceived"" boolean NOT NULL DEFAULT TRUE,
                    ""ShowAiEstimate"" boolean NOT NULL DEFAULT TRUE,
                    ""ShowReadinessBand"" boolean NOT NULL DEFAULT TRUE,
                    ""ShowTutorScore"" boolean NOT NULL DEFAULT TRUE,
                    ""ShowFullCriteria"" boolean NOT NULL DEFAULT TRUE,
                    ""ShowTranscript"" boolean NOT NULL DEFAULT TRUE,
                    ""ShowTutorComments"" boolean NOT NULL DEFAULT TRUE,
                    ""ShowRecommendedDrills"" boolean NOT NULL DEFAULT TRUE,
                    ""AllowReattempt"" boolean NOT NULL DEFAULT TRUE,
                    ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT ""PK_SpeakingResultVisibilityConfigs"" PRIMARY KEY (""Id"")
                );

                CREATE INDEX IF NOT EXISTS ""IX_SpeakingResultVisibilityConfigs_RolePlayCardId""
                    ON ""SpeakingResultVisibilityConfigs"" (""RolePlayCardId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS ""SpeakingResultVisibilityConfigs"";
            ");
        }
    }
}
