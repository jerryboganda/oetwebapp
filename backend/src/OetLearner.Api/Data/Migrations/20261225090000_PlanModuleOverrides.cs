using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    // Owner directive 2026-09-07: companion access fully admin-configurable.
    //
    // Adds PlanModuleOverrides — per-PLAN enable/disable of admin-togglable
    // subscription modules (starting with AiCompanion) that layers on top of
    // BillingPlan.DashboardModulesJson. Rows here survive the OET-2026 catalog
    // seeder, which rewrites DashboardModulesJson from the manifest on every
    // boot, so admin grants made in the companion access section are durable.
    //
    // HAND-AUTHORED (repo convention): contains ONLY the new objects; the model
    // snapshot is left as-is; the runtime model comes from the entity classes.
    // Future-dated id (…1225) keeps it ordered after the in-flight catalog
    // migrations. SAFETY: purely additive — one brand-new table. No backfill,
    // no destructive change; forward-only but Down() is provided.
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261225090000_PlanModuleOverrides")]
    public partial class PlanModuleOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanModuleOverrides",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanModuleOverrides", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanModuleOverrides_PlanCode",
                table: "PlanModuleOverrides",
                column: "PlanCode");

            migrationBuilder.CreateIndex(
                name: "IX_PlanModuleOverrides_PlanCode_ModuleKey",
                table: "PlanModuleOverrides",
                columns: new[] { "PlanCode", "ModuleKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PlanModuleOverrides");
        }
    }
}
