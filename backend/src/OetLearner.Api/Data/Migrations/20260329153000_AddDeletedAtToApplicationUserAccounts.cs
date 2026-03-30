using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    public partial class AddDeletedAtToApplicationUserAccounts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "ApplicationUserAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUserAccounts_DeletedAt",
                table: "ApplicationUserAccounts",
                column: "DeletedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApplicationUserAccounts_DeletedAt",
                table: "ApplicationUserAccounts");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "ApplicationUserAccounts");
        }
    }
}