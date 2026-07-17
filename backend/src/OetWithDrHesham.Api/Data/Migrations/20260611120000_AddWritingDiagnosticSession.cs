using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>WritingDiagnosticSessions</c> table — the durable replacement
    /// for the previous <c>IMemoryCache</c> store in
    /// <c>WritingOnboardingService</c>. Reading-phase progress now survives
    /// process restarts. See <c>PROGRESS.md</c> "Known follow-ups" → diagnostic
    /// session persistence.
    /// </summary>
    public partial class AddWritingDiagnosticSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WritingDiagnosticSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadingPhaseEndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingDiagnosticSessions", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_WritingDiagnosticSessions_UserId_Id",
                table: "WritingDiagnosticSessions",
                columns: new[] { "UserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingDiagnosticSessions_ExpiresAt",
                table: "WritingDiagnosticSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_WritingDiagnosticSessions_SubmissionId",
                table: "WritingDiagnosticSessions",
                column: "SubmissionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WritingDiagnosticSessions");
        }
    }
}
