using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds ExperimentAssignmentId to BillingQuotes so that a confirmed payment
    /// can mark the corresponding PricingExperimentAssignment as converted.
    /// </summary>
    public partial class AddBillingQuoteExperimentAssignmentId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExperimentAssignmentId",
                table: "BillingQuotes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingQuotes_ExperimentAssignmentId",
                table: "BillingQuotes",
                column: "ExperimentAssignmentId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillingQuotes_ExperimentAssignmentId",
                table: "BillingQuotes");

            migrationBuilder.DropColumn(
                name: "ExperimentAssignmentId",
                table: "BillingQuotes");
        }
    }
}
