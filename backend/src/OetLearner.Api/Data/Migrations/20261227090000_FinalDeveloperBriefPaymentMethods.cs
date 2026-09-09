using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Final Developer Modification Brief (8 Sept 2026), item 3/4: retire Stripe
    /// card, PayPal Business and both Monzo bank-transfer routes from every
    /// candidate-facing payment surface, and add the final allowed routes —
    /// Whop and Fawaterak as manual-proof methods, plus HSBC/Lloyds/Barclays as
    /// selectable "how did you pay?" entries for Inside-the-UK and International
    /// transfers (the existing UkBankTransfer instructional component already
    /// shows the same three banks' account details and is unchanged by this).
    ///
    /// Deactivates rather than deletes the retired rows so historical
    /// ManualPaymentRequest.Method values referencing them stay resolvable.
    ///
    /// Hand-authored (no .Designer.cs) to match the surrounding billing
    /// migrations; no column/table shape changes, so the model snapshot is
    /// unaffected.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261227090000_FinalDeveloperBriefPaymentMethods")]
    public partial class FinalDeveloperBriefPaymentMethods : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""PaymentMethodConfigs""
                SET ""IsActive"" = FALSE, ""UpdatedAt"" = NOW()
                WHERE ""Key"" IN ('stripe_card', 'paypal_business', 'uk_monzo_transfer', 'international_monzo_transfer');
            ");

            var ts = "TIMESTAMP '2026-09-10 00:00:00+00'";
            migrationBuilder.Sql($@"
                INSERT INTO ""PaymentMethodConfigs""
                    (""Id"", ""Key"", ""Label"", ""Category"", ""Detail"", ""Meta"", ""Instructions"", ""Note"", ""ReferenceRule"", ""ShowQr"", ""QrImageKey"", ""IconName"", ""IsActive"", ""DisplayOrder"", ""CreatedAt"", ""UpdatedAt"")
                VALUES
                  ('pmc-whop-manual', 'whop_manual', 'Whop', 'international',
                   'Pay through the Whop checkout.', 'Manual proof is only needed if support asks for it.',
                   'Pay through the secure Whop checkout — access is activated automatically once the payment is confirmed.',
                   NULL, FALSE, FALSE, NULL, 'WalletCards', TRUE, 4, {ts}, {ts}),
                  ('pmc-fawaterak-manual', 'fawaterak_manual', 'Fawaterak', 'international',
                   'Pay through the Fawaterak checkout.', 'Manual proof is only needed if support asks for it.',
                   'Pay through the secure Fawaterak checkout — access is activated automatically once the payment is confirmed.',
                   NULL, FALSE, FALSE, NULL, 'WalletCards', TRUE, 5, {ts}, {ts}),
                  ('pmc-hsbc-uk-transfer', 'hsbc_uk_transfer', 'HSBC bank transfer - Inside the UK', 'international',
                   'Ahmed Hesham Ibrahim Abdrabu Ibrahim · HSBC', 'Account 64686063 · Sort code 40-16-64',
                   'Send a UK bank transfer using the details above, then send proof of payment.',
                   NULL, TRUE, FALSE, NULL, 'Landmark', TRUE, 6, {ts}, {ts}),
                  ('pmc-hsbc-international-transfer', 'hsbc_international_transfer', 'HSBC bank transfer - International', 'international',
                   'Ahmed Hesham Ibrahim Abdrabu Ibrahim · IBAN GB57HBUK40166464686063', 'SWIFT / BIC HBUKGB4196Y',
                   'Send the transfer using the IBAN and SWIFT/BIC details above, then send proof of payment.',
                   NULL, TRUE, FALSE, NULL, 'Landmark', TRUE, 7, {ts}, {ts}),
                  ('pmc-lloyds-uk-transfer', 'lloyds_uk_transfer', 'Lloyds bank transfer - Inside the UK', 'international',
                   'Ahmed Ibrahim · Lloyds', 'Account 84744968 · Sort code 77-48-17',
                   'Send a UK bank transfer using the details above, then send proof of payment.',
                   NULL, TRUE, FALSE, NULL, 'Landmark', TRUE, 8, {ts}, {ts}),
                  ('pmc-lloyds-international-transfer', 'lloyds_international_transfer', 'Lloyds bank transfer - International', 'international',
                   'Ahmed Ibrahim · IBAN GB22LOYD77481784744968', 'SWIFT / BIC LOYDGB21W78',
                   'Send the transfer using the IBAN and SWIFT/BIC details above, then send proof of payment.',
                   NULL, TRUE, FALSE, NULL, 'Landmark', TRUE, 9, {ts}, {ts}),
                  ('pmc-barclays-uk-transfer', 'barclays_uk_transfer', 'Barclays bank transfer - Inside the UK', 'international',
                   'AHMED IBRAHIM · Barclays', 'Account 10274178 · Sort code 20-25-44',
                   'Send a UK bank transfer using the details above, then send proof of payment.',
                   NULL, TRUE, FALSE, NULL, 'Landmark', TRUE, 10, {ts}, {ts}),
                  ('pmc-barclays-international-transfer', 'barclays_international_transfer', 'Barclays bank transfer - International', 'international',
                   'AHMED IBRAHIM · IBAN GB90BUKB20254410274178', 'SWIFT / BIC BUKBGB22',
                   'Send the transfer using the IBAN and SWIFT/BIC details above, then send proof of payment.',
                   NULL, TRUE, FALSE, NULL, 'Landmark', TRUE, 11, {ts}, {ts})
                ON CONFLICT (""Key"") DO NOTHING;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""PaymentMethodConfigs""
                WHERE ""Key"" IN (
                    'whop_manual', 'fawaterak_manual',
                    'hsbc_uk_transfer', 'hsbc_international_transfer',
                    'lloyds_uk_transfer', 'lloyds_international_transfer',
                    'barclays_uk_transfer', 'barclays_international_transfer'
                );

                UPDATE ""PaymentMethodConfigs""
                SET ""IsActive"" = TRUE, ""UpdatedAt"" = NOW()
                WHERE ""Key"" IN ('stripe_card', 'paypal_business', 'uk_monzo_transfer', 'international_monzo_transfer');
            ");
        }
    }
}
