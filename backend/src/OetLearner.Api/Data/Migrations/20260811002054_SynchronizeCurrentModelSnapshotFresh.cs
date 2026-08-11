using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Records the current EF model after the idempotent schema migrations that
/// were added without regenerating the historical snapshot. Those migrations
/// own the schema operations; this migration only records the synchronized
/// model and intentionally emits no duplicate SQL.
/// </summary>
public partial class SynchronizeCurrentModelSnapshotFresh : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
