using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingPhase4DistractorAndReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LatestReviewNote",
                table: "ReadingQuestions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionDistractorsJson",
                table: "ReadingQuestions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewState",
                table: "ReadingQuestions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                table: "ReadingAttempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ScopeJson",
                table: "ReadingAttempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SelectedDistractorCategory",
                table: "ReadingAnswers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReadingErrorBankEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PartCode = table.Column<int>(type: "integer", nullable: false),
                    LastWrongAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FirstSeenWrongAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenWrongAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimesWrong = table.Column<int>(type: "integer", nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingErrorBankEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingQuestionReviewLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FromState = table.Column<int>(type: "integer", nullable: false),
                    ToState = table.Column<int>(type: "integer", nullable: false),
                    ReviewerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TransitionedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingQuestionReviewLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingQuestionReviewLogs_ReadingQuestions_ReadingQuestionId",
                        column: x => x.ReadingQuestionId,
                        principalTable: "ReadingQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingErrorBankEntries_UserId_IsResolved",
                table: "ReadingErrorBankEntries",
                columns: new[] { "UserId", "IsResolved" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingErrorBankEntries_UserId_LastSeenWrongAt",
                table: "ReadingErrorBankEntries",
                columns: new[] { "UserId", "LastSeenWrongAt" });

            migrationBuilder.CreateIndex(
                name: "UX_ReadingErrorBankEntry_User_Question",
                table: "ReadingErrorBankEntries",
                columns: new[] { "ReadingQuestionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestionReviewLogs_ReadingQuestionId_TransitionedAt",
                table: "ReadingQuestionReviewLogs",
                columns: new[] { "ReadingQuestionId", "TransitionedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadingErrorBankEntries");

            migrationBuilder.DropTable(
                name: "ReadingQuestionReviewLogs");

            migrationBuilder.DropColumn(
                name: "LatestReviewNote",
                table: "ReadingQuestions");

            migrationBuilder.DropColumn(
                name: "OptionDistractorsJson",
                table: "ReadingQuestions");

            migrationBuilder.DropColumn(
                name: "ReviewState",
                table: "ReadingQuestions");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "ReadingAttempts");

            migrationBuilder.DropColumn(
                name: "ScopeJson",
                table: "ReadingAttempts");

            migrationBuilder.DropColumn(
                name: "SelectedDistractorCategory",
                table: "ReadingAnswers");
        }
    }
}
