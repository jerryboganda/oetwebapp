using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds draft-save support to <c>ExpertCalibrationResults</c>:
    /// <list type="bullet">
    ///   <item><description><c>IsDraft</c>: reviewer saved a draft (not a final submission).</description></item>
    ///   <item><description><c>UpdatedAt</c>: last mutation moment, distinct from <c>SubmittedAt</c>.</description></item>
    /// </list>
    /// Hand-crafted (matching the pattern established by
    /// <c>20260421160000_AddDisabledFeaturesCsv</c>) so the pre-existing out-of-sync
    /// <c>LearnerDbContextModelSnapshot</c> is not disturbed.
    /// </summary>
    public partial class AddCalibrationDraftFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDraft",
                table: "ExpertCalibrationResults",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "ExpertCalibrationResults",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDraft",
                table: "ExpertCalibrationResults");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ExpertCalibrationResults");
        }
    }
}
