using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260609110000_AddDunningAttempts")]
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

            // NOTE: the Stripe Tax / Customer Portal / Radar runtime override
            // columns on RuntimeSettings (StripeTaxAutomaticEnabled,
            // StripeTaxRegistrationsCsv, StripeCustomerPortalConfigurationId,
            // StripeRadarHighRiskCountryAllowReview, StripeRadarBlockEmailDomainsCsv)
            // are added by the earlier migration 20260526155000_AddZoomRuntimeSettings.
            // This migration originally re-added them; duplicate removed so a fresh
            // database does not hit a 42701 "column already exists" error.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DunningAttempts");

            migrationBuilder.DropColumn(name: "RecoveryEmailSentAt", table: "Carts");

            // Stripe Tax / Customer Portal / Radar columns on RuntimeSettings are
            // dropped by 20260526155000_AddZoomRuntimeSettings. Duplicate removed.
        }
    }
}
