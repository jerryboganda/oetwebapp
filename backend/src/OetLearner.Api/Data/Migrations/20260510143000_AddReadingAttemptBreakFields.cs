using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds the server-authored Part A optional break state required by the
    /// strict Reading attempt lifecycle.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260510143000_AddReadingAttemptBreakFields")]
    public partial class AddReadingAttemptBreakFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PartABreakUsed",
                table: "ReadingAttempts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PartBCPausedSeconds",
                table: "ReadingAttempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PartBCTimerPausedAt",
                table: "ReadingAttempts",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PartABreakUsed",
                table: "ReadingAttempts");

            migrationBuilder.DropColumn(
                name: "PartBCPausedSeconds",
                table: "ReadingAttempts");

            migrationBuilder.DropColumn(
                name: "PartBCTimerPausedAt",
                table: "ReadingAttempts");
        }
    }
}