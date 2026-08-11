using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Persists the fail-closed hold raised when Listening audio playback fails.
/// Both the relational V2 attempt and the legacy fallback attempt remain
/// auditable, but cannot be scored until an administrator reviews the failure.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260903100000_AddListeningPlaybackReviewFlags")]
public partial class AddListeningPlaybackReviewFlags : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "RequiresAdminReview",
            table: "Attempts",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "AdminReviewReason",
            table: "Attempts",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AdminReviewFlaggedAt",
            table: "Attempts",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "RequiresAdminReview",
            table: "ListeningAttempts",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "AdminReviewReason",
            table: "ListeningAttempts",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AdminReviewFlaggedAt",
            table: "ListeningAttempts",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AdminReviewFlaggedAt",
            table: "ListeningAttempts");

        migrationBuilder.DropColumn(
            name: "AdminReviewReason",
            table: "ListeningAttempts");

        migrationBuilder.DropColumn(
            name: "RequiresAdminReview",
            table: "ListeningAttempts");

        migrationBuilder.DropColumn(
            name: "AdminReviewFlaggedAt",
            table: "Attempts");

        migrationBuilder.DropColumn(
            name: "AdminReviewReason",
            table: "Attempts");

        migrationBuilder.DropColumn(
            name: "RequiresAdminReview",
            table: "Attempts");
    }
}
