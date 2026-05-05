using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds the four JSON tag columns introduced for the Recalls Content Pack v1
    /// matrix tag system on <c>VocabularyTerms</c>:
    /// <list type="bullet">
    ///   <item><c>CommonMistakesJson</c> — plausible learner mistakes (spelling-diff classifier inputs).</item>
    ///   <item><c>SimilarSoundingJson</c> — distractors for the audio-recognition quiz.</item>
    ///   <item><c>OetSubtestTagsJson</c> — OET subtest dimension (listening_a … speaking).</item>
    ///   <item><c>RecallSetCodesJson</c> — year/source dimension (e.g. ["old","2026"]).</item>
    /// </list>
    /// All four are nullable-default <c>"[]"</c>, never deletes existing data.
    /// Hand-written because the EF Core model snapshot is drifted (see
    /// <c>memories/repo/migration-drift-note.md</c>).
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260505100000_AddRecallSetTagsAndJsonColumns")]
    public partial class AddRecallSetTagsAndJsonColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommonMistakesJson",
                table: "VocabularyTerms",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "SimilarSoundingJson",
                table: "VocabularyTerms",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "OetSubtestTagsJson",
                table: "VocabularyTerms",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "RecallSetCodesJson",
                table: "VocabularyTerms",
                type: "text",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CommonMistakesJson", table: "VocabularyTerms");
            migrationBuilder.DropColumn(name: "SimilarSoundingJson", table: "VocabularyTerms");
            migrationBuilder.DropColumn(name: "OetSubtestTagsJson", table: "VocabularyTerms");
            migrationBuilder.DropColumn(name: "RecallSetCodesJson", table: "VocabularyTerms");
        }
    }
}
