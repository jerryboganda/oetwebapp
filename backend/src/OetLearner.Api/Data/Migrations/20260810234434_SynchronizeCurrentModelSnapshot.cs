using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Synchronizes the EF model snapshot with schema changes that were already
/// shipped by the idempotent migrations added after the previous snapshot.
/// This migration intentionally has no SQL operations; the schema changes are
/// owned by those earlier migrations and must not be replayed here.
/// </summary>
public partial class SynchronizeCurrentModelSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
