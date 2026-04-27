using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260420000000_AddVocabularyV1Enhancements")]
    public partial class AddVocabularyV1Enhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── VocabularyTerm additions ─────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "IpaPronunciation",
                table: "VocabularyTerms",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioMediaAssetId",
                table: "VocabularyTerms",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceProvenance",
                table: "VocabularyTerms",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "VocabularyTerms",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "VocabularyTerms",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero));

            // Widen Category 32 → 64 so profession-specific taxonomies fit.
            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "VocabularyTerms",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            // ── LearnerVocabulary additions ─────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "SourceRef",
                table: "LearnerVocabularies",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            // ── VocabularyQuizResult additions ──────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "Format",
                table: "VocabularyQuizResults",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "definition_match");

            // ── Indexes ──────────────────────────────────────────────────
            migrationBuilder.CreateIndex(
                name: "IX_VocabularyTerms_ProfessionId_Category_Status",
                table: "VocabularyTerms",
                columns: new[] { "ProfessionId", "Category", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyTerms_Term_ExamTypeCode_ProfessionId",
                table: "VocabularyTerms",
                columns: new[] { "Term", "ExamTypeCode", "ProfessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyQuizResults_UserId_CompletedAt",
                table: "VocabularyQuizResults",
                columns: new[] { "UserId", "CompletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VocabularyQuizResults_UserId_CompletedAt",
                table: "VocabularyQuizResults");

            migrationBuilder.DropIndex(
                name: "IX_VocabularyTerms_Term_ExamTypeCode_ProfessionId",
                table: "VocabularyTerms");

            migrationBuilder.DropIndex(
                name: "IX_VocabularyTerms_ProfessionId_Category_Status",
                table: "VocabularyTerms");

            migrationBuilder.DropColumn(name: "Format", table: "VocabularyQuizResults");
            migrationBuilder.DropColumn(name: "SourceRef", table: "LearnerVocabularies");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "VocabularyTerms",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.DropColumn(name: "UpdatedAt", table: "VocabularyTerms");
            migrationBuilder.DropColumn(name: "CreatedAt", table: "VocabularyTerms");
            migrationBuilder.DropColumn(name: "SourceProvenance", table: "VocabularyTerms");
            migrationBuilder.DropColumn(name: "AudioMediaAssetId", table: "VocabularyTerms");
            migrationBuilder.DropColumn(name: "IpaPronunciation", table: "VocabularyTerms");
        }
    }
}
