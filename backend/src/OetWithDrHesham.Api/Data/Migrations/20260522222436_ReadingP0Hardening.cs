using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReadingP0Hardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_ReadingAttempt_UserPaperExam_InProgress",
                table: "ReadingAttempts",
                columns: new[] { "UserId", "PaperId", "Mode", "Status" },
                unique: true,
                filter: "\"Mode\" = 0 AND \"Status\" = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_ReadingParts_ContentPapers_PaperId",
                table: "ReadingParts",
                column: "PaperId",
                principalTable: "ContentPapers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReadingParts_ContentPapers_PaperId",
                table: "ReadingParts");

            migrationBuilder.DropIndex(
                name: "UX_ReadingAttempt_UserPaperExam_InProgress",
                table: "ReadingAttempts");
        }
    }
}
