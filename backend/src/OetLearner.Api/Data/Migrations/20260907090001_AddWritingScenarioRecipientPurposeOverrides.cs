using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260907090001_AddWritingScenarioRecipientPurposeOverrides")]
    public partial class AddWritingScenarioRecipientPurposeOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stored, admin-editable overrides for WritingTaskUnderstandingService's
            // heuristic recipient/diagnosis extraction. Root-cause fix for
            // recipient_unresolved / diagnosis_or_request_unresolved release
            // blockers: the heuristic is regex-based over free-text task/case-note
            // wording and cannot cover every real phrasing. Once an admin
            // confirms or corrects these in their own words, the publish gate
            // must prefer the stored value over re-running the heuristic —
            // never re-guess a value a human already confirmed.
            migrationBuilder.AddColumn<string>(
                name: "RecipientRawText",
                table: "WritingScenarios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientNormalizedJson",
                table: "WritingScenarios",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedPurposeText",
                table: "WritingScenarios",
                type: "text",
                nullable: true);

            // InternalCode uniqueness (WR-0188 was observed live as a genuine
            // duplicate across two rows before a manual fix). Defensive
            // dedupe first so the unique index can never fail to apply:
            // any remaining duplicate's later rows (by CreatedAt) get a
            // "-DUPn" suffix rather than blocking the migration.
            migrationBuilder.Sql(@"
WITH ranked AS (
    SELECT ""Id"", ""InternalCode"",
           ROW_NUMBER() OVER (PARTITION BY ""InternalCode"" ORDER BY ""CreatedAt"", ""Id"") AS rn
    FROM ""WritingScenarios""
    WHERE ""InternalCode"" IS NOT NULL
)
UPDATE ""WritingScenarios"" s
SET ""InternalCode"" = s.""InternalCode"" || '-DUP' || (r.rn - 1)
FROM ranked r
WHERE s.""Id"" = r.""Id"" AND r.rn > 1;");

            migrationBuilder.DropIndex(
                name: "IX_WritingScenarios_InternalCode",
                table: "WritingScenarios");

            migrationBuilder.CreateIndex(
                name: "IX_WritingScenarios_InternalCode",
                table: "WritingScenarios",
                column: "InternalCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WritingScenarios_InternalCode",
                table: "WritingScenarios");

            migrationBuilder.CreateIndex(
                name: "IX_WritingScenarios_InternalCode",
                table: "WritingScenarios",
                column: "InternalCode");

            migrationBuilder.DropColumn(name: "RecipientRawText", table: "WritingScenarios");
            migrationBuilder.DropColumn(name: "RecipientNormalizedJson", table: "WritingScenarios");
            migrationBuilder.DropColumn(name: "ConfirmedPurposeText", table: "WritingScenarios");
        }
    }
}
