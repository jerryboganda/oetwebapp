using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [Migration("20260805090000_AddSpeakingExamMockAttemptLink")]
    public partial class AddSpeakingExamMockAttemptLink : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MockAttemptId",
                table: "SpeakingExamSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MockSectionId",
                table: "SpeakingExamSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingExamSessions_MockAttemptId",
                table: "SpeakingExamSessions",
                column: "MockAttemptId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SpeakingExamSessions_MockAttemptId",
                table: "SpeakingExamSessions");

            migrationBuilder.DropColumn(
                name: "MockSectionId",
                table: "SpeakingExamSessions");

            migrationBuilder.DropColumn(
                name: "MockAttemptId",
                table: "SpeakingExamSessions");
        }
    }
}
