using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// GitHub Copilot integration — Phase 3. Per-account analytics. Adds
    /// <c>AccountId</c> (FK-shaped, nullable, indexed with CreatedAt) and
    /// <c>FailoverTrace</c> (nullable, max 1024) to <c>AiUsageRecords</c>.
    /// Invariant from <c>docs/AI-COPILOT-PRD.md</c>: one turn = one
    /// <c>AiUsageRecord</c>. Failover hops collapse into the trace string.
    /// See <c>docs/AI-COPILOT-PROGRESS.md</c> Phase 3 and
    /// <c>CopilotAiModelProvider.TryCompleteWithAccountFailoverAsync</c>.
    /// </remarks>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260509120000_AddAccountIdAndFailoverTraceToAiUsageRecord")]
    public partial class AddAccountIdAndFailoverTraceToAiUsageRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountId",
                table: "AiUsageRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailoverTrace",
                table: "AiUsageRecords",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_AccountId_CreatedAt",
                table: "AiUsageRecords",
                columns: new[] { "AccountId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiUsageRecords_AccountId_CreatedAt",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(name: "AccountId", table: "AiUsageRecords");
            migrationBuilder.DropColumn(name: "FailoverTrace", table: "AiUsageRecords");
        }
    }
}
