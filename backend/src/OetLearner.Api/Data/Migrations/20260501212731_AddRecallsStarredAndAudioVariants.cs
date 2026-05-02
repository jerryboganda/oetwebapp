using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecallsStarredAndAudioVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AmericanSpelling",
                table: "VocabularyTerms",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioSentenceUrl",
                table: "VocabularyTerms",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioSlowUrl",
                table: "VocabularyTerms",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StarReason",
                table: "ReviewItems",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Starred",
                table: "ReviewItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorTypeCode",
                table: "LearnerVocabularies",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StarReason",
                table: "LearnerVocabularies",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Starred",
                table: "LearnerVocabularies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerVocabularies_UserId_Starred",
                table: "LearnerVocabularies",
                columns: new[] { "UserId", "Starred" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LearnerVocabularies_UserId_Starred",
                table: "LearnerVocabularies");

            migrationBuilder.DropColumn(
                name: "AmericanSpelling",
                table: "VocabularyTerms");

            migrationBuilder.DropColumn(
                name: "AudioSentenceUrl",
                table: "VocabularyTerms");

            migrationBuilder.DropColumn(
                name: "AudioSlowUrl",
                table: "VocabularyTerms");

            migrationBuilder.DropColumn(
                name: "StarReason",
                table: "ReviewItems");

            migrationBuilder.DropColumn(
                name: "Starred",
                table: "ReviewItems");

            migrationBuilder.DropColumn(
                name: "LastErrorTypeCode",
                table: "LearnerVocabularies");

            migrationBuilder.DropColumn(
                name: "StarReason",
                table: "LearnerVocabularies");

            migrationBuilder.DropColumn(
                name: "Starred",
                table: "LearnerVocabularies");
        }
    }
}
