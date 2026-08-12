using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Persists the fail-closed hold raised when a Reading answer payload contains
/// multiple selections for a single-answer MCQ.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260904100000_AddReadingAttemptReviewFlags")]
public partial class AddReadingAttemptReviewFlags : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AdminReviewFlaggedAt",
            table: "ReadingAttempts",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AdminReviewReason",
            table: "ReadingAttempts",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "RequiresAdminReview",
            table: "ReadingAttempts",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AdminReviewFlaggedAt",
            table: "ReadingAttempts");

        migrationBuilder.DropColumn(
            name: "AdminReviewReason",
            table: "ReadingAttempts");

        migrationBuilder.DropColumn(
            name: "RequiresAdminReview",
            table: "ReadingAttempts");
    }
}
