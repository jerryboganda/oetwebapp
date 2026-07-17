using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// OET Speaking double-marking + senior moderation closure
    /// (Developer Implementation Notes §15.4 / §15.5).
    ///
    /// <para><b>Column added to existing table</b></para>
    /// <list type="bullet">
    ///   <item><c>SpeakingTutorAssessments.MarkerRole</c> — distinguishes the
    ///   primary / second / moderated assessor tracks (default 'primary').</item>
    /// </list>
    ///
    /// <para><b>New table</b></para>
    /// <list type="bullet">
    ///   <item><c>SpeakingModerationCases</c> — one row per escalated session
    ///   tracking the double-marking + moderation lifecycle.</item>
    /// </list>
    ///
    /// <para>
    /// Hand-written following the <c>AddSpeakingDriftColumns</c> pattern.
    /// Uses <c>ADD COLUMN IF NOT EXISTS</c> and <c>CREATE TABLE IF NOT EXISTS</c>
    /// to be idempotent. ⚠ <b>Postgres-only SQL.</b> The test suite uses SQLite
    /// via <c>EnsureCreatedAsync()</c> and bypasses migrations entirely.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260601090000_AddSpeakingModeration")]
    public partial class AddSpeakingModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── SpeakingTutorAssessments.MarkerRole ──────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE ""SpeakingTutorAssessments""
                    ADD COLUMN IF NOT EXISTS ""MarkerRole"" character varying(16) NOT NULL DEFAULT 'primary';
            ");

            // ── SpeakingModerationCases (new table) ──────────────────────────
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""SpeakingModerationCases"" (
                    ""Id""                 character varying(64)    NOT NULL,
                    ""SpeakingSessionId""  character varying(64)    NOT NULL,
                    ""Reason""             character varying(24)    NOT NULL DEFAULT 'tutor_request',
                    ""FirstMarkerId""      character varying(64)    NULL,
                    ""FirstAssessmentId""  character varying(64)    NULL,
                    ""FirstScoreJson""     text                     NULL,
                    ""SecondMarkerId""     character varying(64)    NULL,
                    ""SecondAssessmentId"" character varying(64)    NULL,
                    ""SecondScoreJson""    text                     NULL,
                    ""ModeratorId""        character varying(64)    NULL,
                    ""FinalAssessmentId""  character varying(64)    NULL,
                    ""FinalScoreJson""     text                     NULL,
                    ""VariancePoints""     integer                  NULL,
                    ""VarianceReason""     character varying(500)   NULL,
                    ""FinalDecisionNote""  character varying(1000)  NULL,
                    ""RequestReattempt""   boolean                  NOT NULL DEFAULT FALSE,
                    ""Status""             character varying(24)    NOT NULL DEFAULT 'pending_second',
                    ""CreatedAt""          timestamp with time zone NOT NULL DEFAULT now(),
                    ""UpdatedAt""          timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT ""PK_SpeakingModerationCases"" PRIMARY KEY (""Id""),
                    CONSTRAINT ""FK_SpeakingModerationCases_SpeakingSessions_SpeakingSessionId""
                        FOREIGN KEY (""SpeakingSessionId"")
                        REFERENCES ""SpeakingSessions"" (""Id"") ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SpeakingModerationCases_SpeakingSessionId""
                    ON ""SpeakingModerationCases"" (""SpeakingSessionId"");
                CREATE INDEX IF NOT EXISTS ""IX_SpeakingModerationCases_Status""
                    ON ""SpeakingModerationCases"" (""Status"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""SpeakingModerationCases"";");
            migrationBuilder.Sql(@"
                ALTER TABLE ""SpeakingTutorAssessments""
                    DROP COLUMN IF EXISTS ""MarkerRole"";
            ");
        }
    }
}
