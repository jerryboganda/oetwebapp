using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Speaking module EF snapshot drift correction. The domain entities for
    /// the Speaking module grew new columns and a new table across several
    /// Speaking module phases, but those additions were never reflected in the
    /// EF Core migrations because the model snapshot had already drifted on
    /// this branch. This hand-authored migration brings the physical schema
    /// back in sync without absorbing unrelated snapshot noise.
    ///
    /// <para><b>Columns added to existing tables</b></para>
    /// <list type="bullet">
    ///   <item><c>SpeakingSessions.WarmupStartedAt</c> — Phase 3 warm-up start timestamp.</item>
    ///   <item><c>SpeakingSessions.WarmupEndedAt</c> — Phase 3 warm-up end timestamp.</item>
    ///   <item><c>SpeakingSessions.RecommendedDrillIdsJson</c> — Phase 8 course-pathway drill recommendations (up to 5 IDs, JSON array).</item>
    ///   <item><c>SpeakingRecordings.IsWarmup</c> — Phase 3 flag distinguishing warm-up recordings from scored ones.</item>
    ///   <item><c>SpeakingMockSessions.OrchestratorState</c> — Phase 5 granular orchestrator state string (default 'prep1').</item>
    ///   <item><c>SpeakingMockSessions.BridgeStartedAt</c> — Phase 5 bridge entry timestamp.</item>
    /// </list>
    ///
    /// <para><b>New table</b></para>
    /// <list type="bullet">
    ///   <item><c>SpeakingCardBatchRequests</c> — Phase 11 (G.11.4) admin batch-card-generation queue.</item>
    /// </list>
    ///
    /// <para>
    /// Hand-written following the <c>Mocks_V3_W1_ItemRetire</c> pattern.
    /// Uses <c>ADD COLUMN IF NOT EXISTS</c> and <c>CREATE TABLE IF NOT EXISTS</c>
    /// to be idempotent and tolerant of partial prior runs.
    /// ⚠ <b>Postgres-only SQL.</b> The test suite uses SQLite via
    /// <c>EnsureCreatedAsync()</c> and bypasses migrations entirely.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260522100000_AddSpeakingDriftColumns")]
    public partial class AddSpeakingDriftColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── SpeakingSessions ─────────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE ""SpeakingSessions""
                    ADD COLUMN IF NOT EXISTS ""WarmupStartedAt"" timestamp with time zone NULL;
                ALTER TABLE ""SpeakingSessions""
                    ADD COLUMN IF NOT EXISTS ""WarmupEndedAt"" timestamp with time zone NULL;
                ALTER TABLE ""SpeakingSessions""
                    ADD COLUMN IF NOT EXISTS ""RecommendedDrillIdsJson"" character varying(1024) NULL;
            ");

            // ── SpeakingRecordings ───────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE ""SpeakingRecordings""
                    ADD COLUMN IF NOT EXISTS ""IsWarmup"" boolean NOT NULL DEFAULT FALSE;
            ");

            // ── SpeakingMockSessions ─────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE ""SpeakingMockSessions""
                    ADD COLUMN IF NOT EXISTS ""OrchestratorState"" character varying(16) NOT NULL DEFAULT 'prep1';
                ALTER TABLE ""SpeakingMockSessions""
                    ADD COLUMN IF NOT EXISTS ""BridgeStartedAt"" timestamp with time zone NULL;
            ");

            // ── SpeakingCardBatchRequests (new table) ────────────────────────
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""SpeakingCardBatchRequests"" (
                    ""Id""                        character varying(64)   NOT NULL,
                    ""ProfessionId""              character varying(32)   NOT NULL DEFAULT 'nursing',
                    ""Count""                     integer                 NOT NULL DEFAULT 0,
                    ""GeneratedCount""            integer                 NOT NULL DEFAULT 0,
                    ""TopicListJson""             character varying(2000) NOT NULL DEFAULT '[]',
                    ""DifficultyDistributionJson"" character varying(500)  NOT NULL DEFAULT '{}',
                    ""Status""                    integer                 NOT NULL DEFAULT 0,
                    ""RequestedByAdminId""        character varying(64)   NOT NULL DEFAULT '',
                    ""RequestedByAdminName""      character varying(160)  NULL,
                    ""IdempotencyKey""            character varying(96)   NULL,
                    ""Error""                     character varying(1024) NULL,
                    ""CreatedAt""                 timestamp with time zone NOT NULL DEFAULT now(),
                    ""StartedAt""                 timestamp with time zone NULL,
                    ""CompletedAt""               timestamp with time zone NULL,
                    CONSTRAINT ""PK_SpeakingCardBatchRequests"" PRIMARY KEY (""Id"")
                );

                CREATE INDEX IF NOT EXISTS ""IX_SpeakingCardBatchRequests_Status_CreatedAt""
                    ON ""SpeakingCardBatchRequests"" (""Status"", ""CreatedAt"");

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SpeakingCardBatchRequests_IdempotencyKey""
                    ON ""SpeakingCardBatchRequests"" (""IdempotencyKey"")
                    WHERE ""IdempotencyKey"" IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SpeakingCardBatchRequests — drop the whole table.
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS ""SpeakingCardBatchRequests"";
            ");

            // SpeakingMockSessions — drop the two new columns.
            migrationBuilder.Sql(@"
                ALTER TABLE ""SpeakingMockSessions"" DROP COLUMN IF EXISTS ""BridgeStartedAt"";
                ALTER TABLE ""SpeakingMockSessions"" DROP COLUMN IF EXISTS ""OrchestratorState"";
            ");

            // SpeakingRecordings — drop IsWarmup.
            migrationBuilder.Sql(@"
                ALTER TABLE ""SpeakingRecordings"" DROP COLUMN IF EXISTS ""IsWarmup"";
            ");

            // SpeakingSessions — drop the three new columns.
            migrationBuilder.Sql(@"
                ALTER TABLE ""SpeakingSessions"" DROP COLUMN IF EXISTS ""RecommendedDrillIdsJson"";
                ALTER TABLE ""SpeakingSessions"" DROP COLUMN IF EXISTS ""WarmupEndedAt"";
                ALTER TABLE ""SpeakingSessions"" DROP COLUMN IF EXISTS ""WarmupStartedAt"";
            ");
        }
    }
}
