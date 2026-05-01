using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Mocks_V2_W7_DiagnosticEntitlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiagnosticMockEntitlement",
                table: "BillingPlans",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "one_per_lifetime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiagnosticMockEntitlement",
                table: "BillingPlans");
        }
    }
}
