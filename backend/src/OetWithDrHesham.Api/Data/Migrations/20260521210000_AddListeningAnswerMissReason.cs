using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Listening Module — Wave 1 of the OET Listening gap-fill plan. Adds a
    /// nullable <c>MissReason</c> integer column to <c>ListeningAnswers</c>.
    /// Values map to <c>ListeningMissReason</c> (Match=0, Empty=1,
    /// SpellingError=2, WrongNumber=3, ExtraInfo=4, WrongSection=5,
    /// Paraphrase=6, Other=7). The grader populates this at grade time so
    /// the post-submit review page can render the spec's "Missed because…"
    /// feedback without a second pass.
    ///
    /// <para>
    /// Hand-written to match the existing <c>AddReadingAnswerTimingAndSubmitIdempotency</c>
    /// pattern (the EF Core model snapshot has historically drifted in this
    /// repo). The column is nullable and additive — legacy rows graded
    /// before this migration carry <c>NULL</c>, which the review UI treats
    /// as "no reason recorded".
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Postgres-only SQL.</b> Uses <c>ADD COLUMN IF NOT EXISTS</c>.
    /// SQLite tests bypass migrations via <c>EnsureCreatedAsync()</c> so
    /// this is safe.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260521210000_AddListeningAnswerMissReason")]
    public partial class AddListeningAnswerMissReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""ListeningAnswers""
                    ADD COLUMN IF NOT EXISTS ""MissReason"" integer NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""ListeningAnswers"" DROP COLUMN IF EXISTS ""MissReason"";
            ");
        }
    }
}
