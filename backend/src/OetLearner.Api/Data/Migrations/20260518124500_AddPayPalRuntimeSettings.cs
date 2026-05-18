using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayPalRuntimeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayPalCancelUrl",
                table: "RuntimeSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalClientId",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalClientSecretEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalSuccessUrl",
                table: "RuntimeSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalWebhookIdEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayPalCancelUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PayPalClientId",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PayPalClientSecretEncrypted",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PayPalSuccessUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PayPalWebhookIdEncrypted",
                table: "RuntimeSettings");
        }
    }
}
