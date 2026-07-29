using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds encrypted runtime configuration for the external VPN/proxy/Tor/
    /// datacenter intelligence provider used by sign-in risk scoring.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260826090000_AddExternalIpIntelligence")]
    public partial class AddExternalIpIntelligence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityIpIntelligenceProvider",
                table: "RuntimeSettings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityIpinfoTokenEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityIpIntelligenceProvider",
                table: "RuntimeSettings");
            migrationBuilder.DropColumn(
                name: "SecurityIpinfoTokenEncrypted",
                table: "RuntimeSettings");
        }
    }
}
