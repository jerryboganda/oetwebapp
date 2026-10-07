using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// SAMI Wave 3 (hand-authored): tutor/support handoffs prepared from chat
    /// (F-107/F-108/F-123) — structured, evidence-backed summaries with a
    /// tutor-facing lifecycle. Pure DDL per the hand-migration convention.
    /// </remarks>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270113090000_AddCompanionHandoffs")]
    public partial class AddCompanionHandoffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanionHandoffs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Route = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Issue = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    ScoresJson = table.Column<string>(type: "text", nullable: true),
                    TopErrorsJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    HandledBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HandledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanionHandoffs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionHandoffs_UserId_CreatedAt",
                table: "CompanionHandoffs",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionHandoffs_Status_CreatedAt",
                table: "CompanionHandoffs",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CompanionHandoffs");
        }
    }
}
