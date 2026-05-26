using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDunningAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Wave A5 — smart-retry dunning ladder (T+24h / T+72h / T+168h).
            migrationBuilder.CreateTable(
                name: "DunningAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InvoiceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    StripeFailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_DunningAttempts", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_DunningAttempts_SubscriptionId_InvoiceId",
                table: "DunningAttempts",
                columns: new[] { "SubscriptionId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_DunningAttempts_InvoiceId_AttemptNumber",
                table: "DunningAttempts",
                columns: new[] { "InvoiceId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DunningAttempts_Outcome_ScheduledAt",
                table: "DunningAttempts",
                columns: new[] { "Outcome", "ScheduledAt" });

            // Wave A5 — Cart.RecoveryEmailSentAt for the +24h abandoned-cart
            // recovery email idempotency guard.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecoveryEmailSentAt",
                table: "Carts",
                type: "timestamp with time zone",
                nullable: true);

            // Wave A5 — Stripe Tax / Customer Portal / Radar runtime overrides.
            migrationBuilder.AddColumn<bool>(
                name: "StripeTaxAutomaticEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeTaxRegistrationsCsv",
                table: "RuntimeSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerPortalConfigurationId",
                table: "RuntimeSettings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StripeRadarHighRiskCountryAllowReview",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeRadarBlockEmailDomainsCsv",
                table: "RuntimeSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DunningAttempts");

            migrationBuilder.DropColumn(name: "RecoveryEmailSentAt", table: "Carts");

            migrationBuilder.DropColumn(name: "StripeTaxAutomaticEnabled", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "StripeTaxRegistrationsCsv", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "StripeCustomerPortalConfigurationId", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "StripeRadarHighRiskCountryAllowReview", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "StripeRadarBlockEmailDomainsCsv", table: "RuntimeSettings");
        }
    }
}
