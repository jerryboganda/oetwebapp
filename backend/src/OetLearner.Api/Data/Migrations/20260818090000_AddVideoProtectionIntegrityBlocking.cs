using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>VideoProtectionBlockRootedDevices</c> / <c>VideoProtectionBlockEmulators</c>
    /// (Course Platform Security Requirements §3, mobile hardening). Both null
    /// defaults to true (see RuntimeSettingsProvider resolver) — owner directive
    /// to block rooted/jailbroken/emulator playback immediately once built.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260818090000_AddVideoProtectionIntegrityBlocking")]
    public partial class AddVideoProtectionIntegrityBlocking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "VideoProtectionBlockRootedDevices",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VideoProtectionBlockEmulators",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "VideoProtectionBlockEmulators", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "VideoProtectionBlockRootedDevices", table: "RuntimeSettings");
        }
    }
}
