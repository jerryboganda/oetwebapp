using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Adds RowVersion (int, default 0) to ContentPapers for optimistic concurrency.
/// Counterpart of 20260527200000_AddListeningAttemptRowVersion which did the same
/// for the ListeningAttempts table.
/// </summary>
public partial class AddContentPaperRowVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "RowVersion",
            table: "ContentPapers",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RowVersion",
            table: "ContentPapers");
    }
}
