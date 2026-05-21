using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Reading Module — Phase 1 closure. Adds two nullable per-answer
    /// timing columns to <c>ReadingAnswers</c>:
    /// <list type="bullet">
    ///   <item><c>ElapsedMs</c> — milliseconds the learner spent on this
    ///   question between the last focus/save and the current save. Server
    ///   caps at 14_400_000 ms (4 h) to defeat clock skew or hostile
    ///   payloads.</item>
    ///   <item><c>TotalElapsedMs</c> — accumulated milliseconds across every
    ///   autosave to this row. Drives "time per question" analytics in the
    ///   Phase 2 distractor / item-analysis dashboards.</item>
    /// </list>
    /// Submit idempotency is delivered in the same phase but uses the
    /// existing <c>IdempotencyRecords</c> table; no schema change is needed
    /// for that half.
    ///
    /// <para>
    /// Hand-written to match the <c>Mocks_V3_W1_ItemRetire</c> pattern
    /// because the EF Core model snapshot has historically drifted in this
    /// repo (pre-existing un-migrated Speaking entities). Both columns are
    /// nullable so the migration is a strictly additive change against
    /// existing rows — every row written before this migration has both
    /// fields <c>NULL</c>, which the analytics queries treat as "timing
    /// unavailable".
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Postgres-only SQL.</b> Uses <c>ADD COLUMN IF NOT EXISTS</c>. The
    /// test suite uses SQLite via <c>EnsureCreatedAsync()</c> and bypasses
    /// migrations entirely, so this is safe today.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260521190000_AddReadingAnswerTimingAndSubmitIdempotency")]
    public partial class AddReadingAnswerTimingAndSubmitIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""ReadingAnswers""
                    ADD COLUMN IF NOT EXISTS ""ElapsedMs"" integer NULL;
                ALTER TABLE ""ReadingAnswers""
                    ADD COLUMN IF NOT EXISTS ""TotalElapsedMs"" integer NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""ReadingAnswers"" DROP COLUMN IF EXISTS ""TotalElapsedMs"";
                ALTER TABLE ""ReadingAnswers"" DROP COLUMN IF EXISTS ""ElapsedMs"";
            ");
        }
    }
}
