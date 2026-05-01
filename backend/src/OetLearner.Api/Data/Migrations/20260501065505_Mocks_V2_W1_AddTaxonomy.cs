using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Mocks_V2_W1_AddTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryMode",
                table: "MockAttempts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "computer");

            migrationBuilder.AddColumn<long>(
                name: "RandomisationSeed",
                table: "MockAttempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Strictness",
                table: "MockAttempts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "exam");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryMode",
                table: "MockAttempts");

            migrationBuilder.DropColumn(
                name: "RandomisationSeed",
                table: "MockAttempts");

            migrationBuilder.DropColumn(
                name: "Strictness",
                table: "MockAttempts");
        }
    }
}
