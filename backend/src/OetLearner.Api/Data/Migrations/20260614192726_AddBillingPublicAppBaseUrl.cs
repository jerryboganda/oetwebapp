using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingPublicAppBaseUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingPublicAppBaseUrl",
                table: "RuntimeSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingPublicAppBaseUrl",
                table: "RuntimeSettings");
        }
    }
}
