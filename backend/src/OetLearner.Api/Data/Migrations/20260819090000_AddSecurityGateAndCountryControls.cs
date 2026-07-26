using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Course Platform Security Requirements §4.2 + §3.3 follow-ups:
    ///  * <c>SecurityRequireVerifiedEmailForLearners</c> — owner-flippable
    ///    hard gate behind the email-verification banner (null = false).
    ///  * <c>SecurityCountryAllowList</c> / <c>SecurityCountryAllowListMode</c>
    ///    — optional sign-in country restriction (null mode = "off").
    ///  * <c>RefreshTokenRecords.Platform</c> / <c>AppVersion</c> — captured at
    ///    session create so the session lists can say WHERE a session lives.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260819090000_AddSecurityGateAndCountryControls")]
    public partial class AddSecurityGateAndCountryControls : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SecurityRequireVerifiedEmailForLearners",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityCountryAllowList",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityCountryAllowListMode",
                table: "RuntimeSettings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "RefreshTokenRecords",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppVersion",
                table: "RefreshTokenRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SecurityRequireVerifiedEmailForLearners", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SecurityCountryAllowList", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SecurityCountryAllowListMode", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "Platform", table: "RefreshTokenRecords");
            migrationBuilder.DropColumn(name: "AppVersion", table: "RefreshTokenRecords");
        }
    }
}
