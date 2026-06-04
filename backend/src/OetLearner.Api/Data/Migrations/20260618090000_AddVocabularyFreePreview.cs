using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260618090000_AddVocabularyFreePreview")]
    public partial class AddVocabularyFreePreview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Free-preview locking for the Recall Vocabulary Bank. Admin-curated
            // flag marking the subset of terms a non-subscribed learner may access.
            // Defaults to false so existing terms stay locked behind the paywall
            // until an admin explicitly opts them into the free preview.
            migrationBuilder.AddColumn<bool>(
                name: "IsFreePreview",
                table: "VocabularyTerms",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyTerms_ExamTypeCode_Status_IsFreePreview",
                table: "VocabularyTerms",
                columns: new[] { "ExamTypeCode", "Status", "IsFreePreview" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VocabularyTerms_ExamTypeCode_Status_IsFreePreview",
                table: "VocabularyTerms");

            migrationBuilder.DropColumn(
                name: "IsFreePreview",
                table: "VocabularyTerms");
        }
    }
}
