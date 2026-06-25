using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDiagnostic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WritingDiagnosticSessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WritingDiagnosticSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadingPhaseEndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingDiagnosticSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WritingDiagnosticSessions_ExpiresAt",
                table: "WritingDiagnosticSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_WritingDiagnosticSessions_SubmissionId",
                table: "WritingDiagnosticSessions",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingDiagnosticSessions_UserId_Id",
                table: "WritingDiagnosticSessions",
                columns: new[] { "UserId", "Id" });
        }
    }
}
