using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// F-070 — per-question Reading confidence (hand-authored, additive): one
    /// nullable <c>ReadingAnswers.Confidence</c> column, storing
    /// <c>ReadingConfidence</c> as its integer value (0 guessed, 1 unsure,
    /// 2 fairly sure, 3 certain).
    ///
    /// <para>
    /// <b>No backfill, on purpose.</b> The column is nullable and every existing
    /// row stays <c>NULL</c>, which means "the learner was never asked". Writing
    /// a default would be the one thing that makes the F-045/F-070 analysis
    /// unusable: <c>companion_learning_fingerprint</c> reports accuracy per
    /// confidence level, and a defaulted value would be counted there as if the
    /// learner had rated the question. The server-side default is therefore
    /// "unset" and nothing in the code path substitutes a level.
    /// </para>
    ///
    /// <para>
    /// Additive and non-destructive: old code keeps running against the new
    /// schema during a blue/green overlap (it never selects or writes the
    /// column), and the companion tool treats a null rating as absent data
    /// rather than as an error.
    /// </para>
    ///
    /// <para>
    /// The column is written by the existing Reading autosave
    /// (<c>PUT /v1/reading-papers/attempts/{attemptId}/answers/{questionId}</c>,
    /// optional <c>confidence</c> field) through
    /// <c>ReadingAttemptService.SaveAnswerAsync</c>. A save that omits it leaves
    /// any stored rating untouched.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270120090000_AddReadingAnswerConfidence")]
    public partial class AddReadingAnswerConfidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Confidence",
                table: "ReadingAnswers",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "ReadingAnswers");
        }
    }
}
