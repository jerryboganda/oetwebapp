using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261214090000_AddWritingTaskModelAnswer")]
    public partial class AddWritingTaskModelAnswer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WritingTaskModelAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsCandidateVisible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ModelAnswerText = table.Column<string>(type: "text", nullable: true),
                    GroundedFactReferencesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    HoldReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RulebookVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PromptVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ModelUsed = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingTaskModelAnswers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WritingTaskModelAnswers_ScenarioId",
                table: "WritingTaskModelAnswers",
                column: "ScenarioId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WritingTaskModelAnswers");
        }
    }
}
