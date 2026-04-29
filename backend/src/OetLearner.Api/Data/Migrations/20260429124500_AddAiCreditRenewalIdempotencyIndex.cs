using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260429124500_AddAiCreditRenewalIdempotencyIndex")]
    public partial class AddAiCreditRenewalIdempotencyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_AiCreditLedger_PlanRenewal_ReferenceId",
                table: "AiCreditLedger",
                column: "ReferenceId",
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND \"Source\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_AiCreditLedger_PlanRenewal_ReferenceId",
                table: "AiCreditLedger");
        }
    }
}