using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds the <c>SecuritySingleActiveSessionEnabled</c> RuntimeSettings
    /// toggle (Course Platform Security Requirements §3.1). No new tables —
    /// enforcement keys entirely on the existing <c>RefreshTokenRecord.FamilyId</c>
    /// column via a new <c>sfam</c> JWT claim (AuthTokenService) and an
    /// <c>OnTokenValidated</c> liveness check (Program.cs); nothing to migrate
    /// there.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260814090000_AddSingleSessionEnforcement")]
    public partial class AddSingleSessionEnforcement : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SecuritySingleActiveSessionEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecuritySingleActiveSessionEnabled",
                table: "RuntimeSettings");
        }
    }
}
