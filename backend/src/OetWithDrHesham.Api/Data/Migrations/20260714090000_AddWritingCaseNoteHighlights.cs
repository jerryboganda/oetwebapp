using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWritingCaseNoteHighlights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CaseNoteHighlightsJson",
                table: "WritingSubmissions",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateTable(
                name: "WritingCaseNoteHighlights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    HighlightsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingCaseNoteHighlights", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WritingCaseNoteHighlights_UserId_ScenarioId",
                table: "WritingCaseNoteHighlights",
                columns: new[] { "UserId", "ScenarioId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WritingCaseNoteHighlights");

            migrationBuilder.DropColumn(
                name: "CaseNoteHighlightsJson",
                table: "WritingSubmissions");
        }
    }
}
