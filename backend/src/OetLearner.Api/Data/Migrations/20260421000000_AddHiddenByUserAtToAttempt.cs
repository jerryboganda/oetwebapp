using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>HiddenByUserAt</c> to <c>Attempts</c> to support learner
    /// self-service hide/unhide in the Submission History UI. The column is
    /// nullable — a null value means "visible", a non-null value means
    /// "hidden from History lists but retained for analytics, readiness,
    /// and progress computations". See <see cref="SubmissionHistoryService"/>.
    /// </summary>
    public partial class AddHiddenByUserAtToAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HiddenByUserAt",
                table: "Attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_UserId_HiddenByUserAt_SubmittedAt",
                table: "Attempts",
                columns: new[] { "UserId", "HiddenByUserAt", "SubmittedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Attempts_UserId_HiddenByUserAt_SubmittedAt",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "HiddenByUserAt",
                table: "Attempts");
        }
    }
}
