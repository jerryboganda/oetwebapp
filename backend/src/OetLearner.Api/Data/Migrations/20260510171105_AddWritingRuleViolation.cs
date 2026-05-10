using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds the deterministic Writing rule-violation table and the matching
    /// hybrid-grader fields on Evaluation. Required by the rulebook-compliance
    /// audit (docs/audits/rulebook-compliance-2026-05-10.md, P0-1 / P2-2):
    /// every Writing submission must persist deterministic findings as
    /// first-class rows so per-rule analytics become possible, while the
    /// AI grader response and grader-mode discriminator are persisted on
    /// the Evaluation itself.
    ///
    /// Trimmed to ONLY the WritingRuleViolation + Evaluation columns owned
    /// by this work item. The full ModelSnapshot regen (which reflects
    /// unrelated parallel-agent entity drift such as AiFeatureRoutes,
    /// AiProviderAccounts, AiToolInvocations, ListeningAttempts column move,
    /// etc.) is left for the agents that own those changes; their own
    /// migrations will reconcile when they land.
    /// </summary>
    public partial class AddWritingRuleViolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Evaluation columns — hybrid grader bookkeeping.
            migrationBuilder.AddColumn<string>(
                name: "AiRawResponseJson",
                table: "Evaluations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CriticalRuleViolationCount",
                table: "Evaluations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "GraderMode",
                table: "Evaluations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "stub_legacy");

            migrationBuilder.AddColumn<int>(
                name: "RuleViolationCount",
                table: "Evaluations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RuleViolationsJson",
                table: "Evaluations",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            // First-class deterministic violations table.
            migrationBuilder.CreateTable(
                name: "WritingRuleViolations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvaluationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CriterionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Quote = table.Column<string>(type: "text", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: true),
                    FixSuggestion = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingRuleViolations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WritingRuleViolations_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WritingRuleViolations_EvaluationId",
                table: "WritingRuleViolations",
                column: "EvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingRuleViolations_RuleId_CreatedAt",
                table: "WritingRuleViolations",
                columns: new[] { "RuleId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingRuleViolations_UserId_CreatedAt",
                table: "WritingRuleViolations",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WritingRuleViolations");

            migrationBuilder.DropColumn(
                name: "RuleViolationsJson",
                table: "Evaluations");

            migrationBuilder.DropColumn(
                name: "RuleViolationCount",
                table: "Evaluations");

            migrationBuilder.DropColumn(
                name: "GraderMode",
                table: "Evaluations");

            migrationBuilder.DropColumn(
                name: "CriticalRuleViolationCount",
                table: "Evaluations");

            migrationBuilder.DropColumn(
                name: "AiRawResponseJson",
                table: "Evaluations");
        }
    }
}
