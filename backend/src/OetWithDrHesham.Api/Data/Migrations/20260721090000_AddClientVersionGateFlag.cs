using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260721090000_AddClientVersionGateFlag")]
    public partial class AddClientVersionGateFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnforceClientVersionGate",
                table: "LaunchReadinessSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnforceClientVersionGate",
                table: "LaunchReadinessSettings");
        }
    }
}
