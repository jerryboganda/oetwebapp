using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Aligns the EF model snapshot with the v1.1 Speaking Simulation model.
///
/// The v1.1 tables and columns were introduced by the preceding idempotent
/// migrations in this release. This migration intentionally changes no
/// database objects; it records the model snapshot so future EF checks do not
/// mistake the hand-authored schema migrations for pending model changes.
/// </summary>
public partial class SynchronizeSpeakingSimulationV11ModelSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
