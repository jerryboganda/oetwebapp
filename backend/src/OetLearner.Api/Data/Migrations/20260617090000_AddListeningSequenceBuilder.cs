using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddListeningSequenceBuilder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // WS4 — Admin Sequence Builder. Optional explicit exam-sequence for
            // Listening papers, authored by admins and consumed by the session
            // FSM when present. Nullable with no backfill: a null value means
            // the FSM derives the canonical sequence from the effective policy,
            // reproducing today's per-window timing byte-for-byte. Mirrors the
            // sibling ContentPaper.ExtractedTextJson convention (plain `text`).
            migrationBuilder.AddColumn<string>(
                name: "ListeningSequenceJson",
                table: "ContentPapers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ListeningSequenceJson",
                table: "ContentPapers");
        }
    }
}
