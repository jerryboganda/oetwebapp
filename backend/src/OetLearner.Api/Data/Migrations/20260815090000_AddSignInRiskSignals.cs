using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>RefreshTokenRecord.CountryCode</c> (captured from CF-IPCountry
    /// at sign-in) and the <c>SecurityRiskMode</c> RuntimeSettings toggle
    /// (Course Platform Security Requirements §3.3). Ships in "log_only" —
    /// risk signals are recorded as SecurityEvents but never block a sign-in
    /// until an admin reviews a week of data and flips to "enforce".
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260815090000_AddSignInRiskSignals")]
    public partial class AddSignInRiskSignals : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "RefreshTokenRecords",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityRiskMode",
                table: "RuntimeSettings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityRiskMode",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "RefreshTokenRecords");
        }
    }
}
