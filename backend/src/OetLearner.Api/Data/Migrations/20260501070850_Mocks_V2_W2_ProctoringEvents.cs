using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Mocks_V2_W2_ProctoringEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MockProctoringEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockSectionAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockProctoringEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockProctoringEvents_MockAttempts_MockAttemptId",
                        column: x => x.MockAttemptId,
                        principalTable: "MockAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MockProctoringEvents_MockSectionAttempts_MockSectionAttempt~",
                        column: x => x.MockSectionAttemptId,
                        principalTable: "MockSectionAttempts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MockProctoringEvents_Kind",
                table: "MockProctoringEvents",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_MockProctoringEvents_MockAttemptId_OccurredAt",
                table: "MockProctoringEvents",
                columns: new[] { "MockAttemptId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MockProctoringEvents_MockSectionAttemptId",
                table: "MockProctoringEvents",
                column: "MockSectionAttemptId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MockProctoringEvents");
        }
    }
}
