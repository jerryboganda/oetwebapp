using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWritingReviewVoiceNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WritingReviewVoiceNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingReviewVoiceNotes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WritingReviewVoiceNotes_SubmissionId",
                table: "WritingReviewVoiceNotes",
                column: "SubmissionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WritingReviewVoiceNotes");
        }
    }
}
