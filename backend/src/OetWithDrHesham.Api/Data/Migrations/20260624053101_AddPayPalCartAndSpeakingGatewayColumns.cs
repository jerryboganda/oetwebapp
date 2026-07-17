using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayPalCartAndSpeakingGatewayColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: RuntimeSettings.CatalogPresentationJson is added by
            // 20260708000000_AddCatalogPresentationRuntimeSetting (idempotent
            // ADD COLUMN IF NOT EXISTS). It was duplicated here by mistake and
            // crashed prod startup ("column already exists"); removed so this
            // migration only adds the new PayPal/speaking gateway columns.
            migrationBuilder.AddColumn<string>(
                name: "PaymentGateway",
                table: "PrivateSpeakingBookings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentGatewayOrderId",
                table: "PrivateSpeakingBookings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gateway",
                table: "CheckoutSessions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                // Existing checkout sessions are all Stripe (hosted) — backfill accordingly.
                defaultValue: "stripe");

            migrationBuilder.AddColumn<string>(
                name: "GatewayOrderId",
                table: "CheckoutSessions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_GatewayOrderId",
                table: "CheckoutSessions",
                column: "GatewayOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckoutSessions_GatewayOrderId",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "PaymentGateway",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(
                name: "PaymentGatewayOrderId",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(
                name: "Gateway",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "GatewayOrderId",
                table: "CheckoutSessions");
        }
    }
}
