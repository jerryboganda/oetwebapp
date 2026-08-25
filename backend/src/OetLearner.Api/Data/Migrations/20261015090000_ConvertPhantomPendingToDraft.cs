using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Urgent payment-status fix: Pending must mean payment succeeded + awaiting admin fulfilment.
/// Pre-payment scaffolds created when a learner merely opened checkout were incorrectly
/// persisted as Pending (and surfaced as "pending" in admin User Management). They must
/// be Draft only — hidden, no entitlements, never counted as a valid subscription.
/// This migration re-labels existing phantom Pending+auto rows that have no completed
/// payment transaction or approved proof to Draft (10), preserving FK integrity for
/// their BillingQuotes.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20261015090000_ConvertPhantomPendingToDraft")]
public partial class ConvertPhantomPendingToDraft : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Pending (1) + auto that never saw a completed gateway transaction nor an approved proof
            -- are the cart-only scaffolds. Move them to Draft (10) so they stay hidden from
            -- all normal learner/admin views and never satisfy the "Pending = payment received"
            -- invariant. FK to BillingQuotes is kept — the quotes simply stay Completed/Created.
            UPDATE "Subscriptions" s
            SET "Status" = 10,
                "ChangedAt" = now()
            WHERE s."Status" = 1
              AND s."FulfilmentStatus" = 'auto'
              AND NOT EXISTS (
                  SELECT 1 FROM "BillingQuotes" q
                  JOIN "PaymentTransactions" t ON t."QuoteId" = q."Id"
                  WHERE q."SubscriptionId" = s."Id"
                    AND t."Status" = 'completed'
              )
              AND NOT EXISTS (
                  SELECT 1 FROM "ManualPaymentRequests" r
                  WHERE r."AccessGrantedSubscriptionId" = s."Id"
                    AND r."Status" IN ('approved', 'paid')
              )
              -- Keep recently failed checkout scaffolds that were intentionally cancelled/expired?
              -- Only auto-pending ones are phantom; pending_manual/pending_verification are real paid orders.
            ;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE "Subscriptions" s
            SET "Status" = 1,
                "ChangedAt" = now()
            WHERE s."Status" = 10
              AND s."FulfilmentStatus" = 'auto';
            """);
    }
}
