using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>SecurityInactiveSessionTimeoutDays</c> (Course Platform
    /// Security Requirements §4.2) — the idle-session window
    /// <c>AuthDataRetentionWorker</c> revokes past. Null defaults to 30 (see
    /// RuntimeSettingsProvider resolver).
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260817090000_AddSecurityInactiveSessionTimeout")]
    public partial class AddSecurityInactiveSessionTimeout : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SecurityInactiveSessionTimeoutDays",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SecurityInactiveSessionTimeoutDays", table: "RuntimeSettings");
        }
    }
}
