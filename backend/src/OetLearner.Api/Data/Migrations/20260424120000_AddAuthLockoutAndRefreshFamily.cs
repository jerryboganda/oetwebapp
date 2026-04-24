using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Security hardening (H1, H3):
    ///  * ApplicationUserAccounts gains FailedSignInCount + LockoutUntil for
    ///    per-account credential-stuffing protection.
    ///  * RefreshTokenRecords gains FamilyId for refresh-token reuse detection
    ///    (OAuth 2 BCP §4.13.2). Existing rows are back-filled with FamilyId =
    ///    Id so each pre-existing token is its own singleton family.
    /// </summary>
    public partial class AddAuthLockoutAndRefreshFamily : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedSignInCount",
                table: "ApplicationUserAccounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockoutUntil",
                table: "ApplicationUserAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "RefreshTokenRecords",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            // Back-fill existing rows: each token is its own family.
            migrationBuilder.Sql(
                "UPDATE \"RefreshTokenRecords\" SET \"FamilyId\" = \"Id\" WHERE \"FamilyId\" = '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokenRecords_FamilyId",
                table: "RefreshTokenRecords",
                column: "FamilyId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokenRecords_FamilyId",
                table: "RefreshTokenRecords");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "RefreshTokenRecords");

            migrationBuilder.DropColumn(
                name: "LockoutUntil",
                table: "ApplicationUserAccounts");

            migrationBuilder.DropColumn(
                name: "FailedSignInCount",
                table: "ApplicationUserAccounts");
        }
    }
}
