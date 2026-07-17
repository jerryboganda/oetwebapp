using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>ListeningExtracts.NotesBodyMarkdown</c> — the OET Listening Part A
    /// note-completion document (markdown-ish: headings, bullets, inline <c>____</c>
    /// gap markers) stored once per consultation extract. Nullable text; null for
    /// Part B/C extracts. Additive, non-destructive.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260630090000_AddListeningExtractNotesBody")]
    public partial class AddListeningExtractNotesBody : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotesBodyMarkdown",
                table: "ListeningExtracts",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotesBodyMarkdown",
                table: "ListeningExtracts");
        }
    }
}
