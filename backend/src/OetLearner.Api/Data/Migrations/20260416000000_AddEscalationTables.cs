using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEscalationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── LearnerEscalations ──────────────────────────────────────

            migrationBuilder.CreateTable(
                name: "LearnerEscalations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmissionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerEscalations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerEscalations_UserId_Status",
                table: "LearnerEscalations",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerEscalations_SubmissionId",
                table: "LearnerEscalations",
                column: "SubmissionId");

            // ── ReviewEscalations ───────────────────────────────────────

            migrationBuilder.CreateTable(
                name: "ReviewEscalations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecondReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TriggerCriterion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AiScore = table.Column<int>(type: "integer", nullable: false),
                    HumanScore = table.Column<int>(type: "integer", nullable: false),
                    Divergence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResolutionNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FinalScore = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewEscalations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewEscalations_ReviewRequestId_Status",
                table: "ReviewEscalations",
                columns: new[] { "ReviewRequestId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewEscalations_SecondReviewerId",
                table: "ReviewEscalations",
                column: "SecondReviewerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ReviewEscalations");
            migrationBuilder.DropTable(name: "LearnerEscalations");
        }
    }
}
