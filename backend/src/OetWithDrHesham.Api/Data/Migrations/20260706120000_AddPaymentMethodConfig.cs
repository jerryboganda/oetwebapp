using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Admin-configurable payment methods. Adds the <c>PaymentMethodConfigs</c>
    /// table and seeds the 7 methods that were previously hard-coded in the
    /// frontend manual-payment page, preserving their exact values and order.
    ///
    /// Hand-authored (no .Designer.cs) to match the surrounding billing
    /// migrations; the model snapshot is updated by hand.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260706120000_AddPaymentMethodConfig")]
    public partial class AddPaymentMethodConfig : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentMethodConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "international"),
                    Detail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    Meta = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Instructions = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false, defaultValue: ""),
                    Note = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReferenceRule = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ShowQr = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    QrImageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IconName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_PaymentMethodConfigs", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_PaymentMethodConfigs_Key", table: "PaymentMethodConfigs", column: "Key", unique: true);
            migrationBuilder.CreateIndex(name: "IX_PaymentMethodConfigs_Category_IsActive_DisplayOrder", table: "PaymentMethodConfigs", columns: new[] { "Category", "IsActive", "DisplayOrder" });

            // Seed the 7 spec methods with the exact values previously hard-coded
            // in app/billing/manual-payment/page.tsx. ON CONFLICT keeps later admin
            // edits intact if the migration is ever replayed.
            var ts = "TIMESTAMP '2026-06-14 00:00:00+00'";
            migrationBuilder.Sql($@"
                INSERT INTO ""PaymentMethodConfigs""
                    (""Id"", ""Key"", ""Label"", ""Category"", ""Detail"", ""Meta"", ""Instructions"", ""Note"", ""ReferenceRule"", ""ShowQr"", ""QrImageKey"", ""IconName"", ""IsActive"", ""DisplayOrder"", ""CreatedAt"", ""UpdatedAt"")
                VALUES
                  ('pmc-instapay-qr-link', 'instapay_qr_link', 'InstaPay QR / link', 'inside_egypt',
                   'Handle: drahmedhesham_work@instapay', 'https://ipn.eg/S/drahmedhesham_work/instapay/2wqbVW',
                   'Open InstaPay or a supported banking app, scan the QR code or use the payment link, enter the required amount, complete the payment, then send proof of the successful transaction.',
                   NULL, FALSE, TRUE, NULL, 'QrCode', TRUE, 1, {ts}, {ts}),
                  ('pmc-vodafone-cash-fawry', 'vodafone_cash_fawry', 'Vodafone Cash / Fawry', 'inside_egypt',
                   '+201062365271', 'Ahmed Hesham Ibrahim Abdrabu',
                   'Transfer the required amount to the number above, then send a screenshot of the confirmation message.',
                   NULL, FALSE, FALSE, NULL, 'WalletCards', TRUE, 2, {ts}, {ts}),
                  ('pmc-qnb-egypt', 'qnb_egypt', 'QNB Egypt bank transfer', 'inside_egypt',
                   'AHMED HISHAM IBRAHIM ABDRABO IBRAHIM', 'QNB · Account 1002506251368',
                   'Transfer the required amount to the account above, then send proof of payment.',
                   'Inside Egypt only.', FALSE, FALSE, NULL, 'Landmark', TRUE, 3, {ts}, {ts}),
                  ('pmc-stripe-card', 'stripe_card', 'Stripe card', 'international',
                   'Use the card checkout route for instant, verified activation.', 'Manual proof is only needed if support asks for it.',
                   'Pay by card through the secure Stripe checkout — access is activated automatically once the payment is confirmed.',
                   NULL, FALSE, FALSE, NULL, 'CreditCard', TRUE, 4, {ts}, {ts}),
                  ('pmc-paypal-business', 'paypal_business', 'PayPal Business', 'international',
                   'support@oetwithdrhesham.co.uk', '+447961725989',
                   'Pay through PayPal Business, then send proof of payment if access is not activated automatically.',
                   NULL, FALSE, FALSE, NULL, 'WalletCards', TRUE, 5, {ts}, {ts}),
                  ('pmc-uk-monzo-transfer', 'uk_monzo_transfer', 'UK bank transfer — Monzo', 'international',
                   'Ahmed Ibrahim · Monzo Bank', 'Account 98630202 · Sort code 04-00-03',
                   'Send a UK bank transfer using the details above, then send proof of payment.',
                   NULL, TRUE, FALSE, NULL, 'Landmark', TRUE, 6, {ts}, {ts}),
                  ('pmc-international-monzo-transfer', 'international_monzo_transfer', 'International bank transfer — Monzo', 'international',
                   'Ahmed Ibrahim · IBAN GB44MONZ04000398630202', 'BIC / SWIFT MONZGB2L (some banks use MONZGB2LXXX)',
                   'Send the transfer using the IBAN and BIC/SWIFT details above, then send proof of payment.',
                   NULL, TRUE, FALSE, NULL, 'Landmark', TRUE, 7, {ts}, {ts})
                ON CONFLICT (""Key"") DO NOTHING;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PaymentMethodConfigs");
        }
    }
}
