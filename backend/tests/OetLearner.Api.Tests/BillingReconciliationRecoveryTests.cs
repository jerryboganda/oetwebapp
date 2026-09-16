using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Owner P0, 15 Sep 2026 — "Missed webhook + reconciliation: the successful payment
/// is recovered automatically."
///
/// The reconciliation sweep used to only WRITE A FINDING when it saw a provider-paid
/// payment with no local fulfilment, so the money was confirmed, the divergence was
/// logged, and the learner still had no course until someone intervened by hand.
///
/// The nastiest shape of this is the one pinned here: a verified, completed provider
/// event whose local processing landed in <c>ignored</c> — which happens when the
/// event arrives before the local order row exists. <c>ignored</c> is TERMINAL to the
/// webhook dedupe, so every provider redelivery afterwards short-circuits as a
/// duplicate and the payment is buried permanently. Reconciliation re-driving it is
/// the only thing that gets it back.
/// </summary>
public sealed class BillingReconciliationRecoveryTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BillingReconciliationRecoveryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Sweep_RecoversABuriedProviderPaidEvent_AndGrantsExactlyOnce()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"recon-user-{suffix}";
        var planCode = $"recon-plan-{suffix}";
        var subscriptionId = $"recon-sub-{suffix}";
        var quoteId = $"recon-quote-{suffix}";
        var eventId = $"evt_{suffix}";

        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", "Recovered Candidate");

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;

            db.BillingPlans.Add(new BillingPlan
            {
                Id = planCode,
                Code = planCode,
                Name = "Full Condensed Recorded OET Course - Medicine (test)",
                Price = 100m,
                Currency = "GBP",
                Interval = "one_time",
                DurationMonths = 6,
                AccessDurationDays = 180,
                IncludedCredits = 5,
                DeliveryMethod = DeliveryMethods.AutomaticWeb,
                CreatedAt = now,
                UpdatedAt = now,
            });

            // Exactly the state the incident left behind: a pre-payment Draft
            // scaffold and a pending transaction, with the money already taken.
            db.Subscriptions.Add(new Subscription
            {
                Id = subscriptionId,
                UserId = userId,
                PlanId = planCode,
                Status = SubscriptionStatus.Draft,
                StartedAt = now,
                ChangedAt = now,
                NextRenewalAt = now.AddMonths(6),
                PriceAmount = 100m,
                Currency = "GBP",
                Interval = "one_time",
            });

            db.BillingQuotes.Add(new BillingQuote
            {
                Id = quoteId,
                UserId = userId,
                SubscriptionId = subscriptionId,
                PlanCode = planCode,
                Currency = "GBP",
                SubtotalAmount = 100m,
                TotalAmount = 100m,
                Status = BillingQuoteStatus.Applied,
                CreatedAt = now,
                ExpiresAt = now.AddHours(1),
                SnapshotJson = "{}",
            });

            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = userId,
                Gateway = "stripe",
                GatewayTransactionId = quoteId,
                TransactionType = "subscription_payment",
                Status = "pending",
                Amount = 100m,
                Currency = "GBP",
                ProductType = "plan",
                ProductId = planCode,
                QuoteId = quoteId,
                CreatedAt = now,
                UpdatedAt = now,
            });

            // The provider told us it was paid; we buried the message.
            db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
            {
                Id = Guid.NewGuid(),
                Gateway = "stripe",
                GatewayEventId = eventId,
                EventType = "payment_intent.succeeded",
                GatewayTransactionId = quoteId,
                NormalizedStatus = "completed",
                ProcessingStatus = "ignored",
                VerificationStatus = "verified",
                VerifiedAt = now,
                ReceivedAt = now,
                LastAttemptedAt = now,
                AttemptCount = 1,
                PayloadJson = "{}",
            });

            await db.SaveChangesAsync();
        }

        var worker = _factory.Services.GetRequiredService<BillingReconciliationWorker>();
        await worker.RunOnceAsync(CancellationToken.None);

        await using (var assertScope = _factory.Services.CreateAsyncScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            var subscription = await db.Subscriptions.AsNoTracking()
                .FirstAsync(s => s.Id == subscriptionId);

            // The whole point: no human touched this and the learner now has access.
            Assert.NotEqual(SubscriptionStatus.Draft, subscription.Status);

            var quote = await db.BillingQuotes.AsNoTracking().FirstAsync(q => q.Id == quoteId);
            Assert.Equal(BillingQuoteStatus.Completed, quote.Status);

            var transaction = await db.PaymentTransactions.AsNoTracking()
                .FirstAsync(t => t.QuoteId == quoteId);
            Assert.Equal("completed", transaction.Status);

            var evt = await db.PaymentWebhookEvents.AsNoTracking()
                .FirstAsync(e => e.GatewayEventId == eventId);
            Assert.Equal("completed", evt.ProcessingStatus);

            // A finding is still written, so the divergence stays auditable even
            // though it healed itself.
            var findings = await db.BillingEvents.AsNoTracking()
                .Where(e => e.EventType == BillingReconciliationWorker.MismatchEventType
                    && e.PayloadJson.Contains(eventId))
                .ToListAsync();
            Assert.NotEmpty(findings);
        }

        // Idempotency: a second sweep (or a provider redelivery landing later) must
        // not grant, invoice or charge anything a second time.
        await worker.RunOnceAsync(CancellationToken.None);

        await using (var reassertScope = _factory.Services.CreateAsyncScope())
        {
            var db = reassertScope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            var subscriptions = await db.Subscriptions.AsNoTracking()
                .Where(s => s.UserId == userId)
                .ToListAsync();
            Assert.Single(subscriptions);

            var transactions = await db.PaymentTransactions.AsNoTracking()
                .Where(t => t.LearnerUserId == userId && t.Status == "completed")
                .ToListAsync();
            Assert.Single(transactions);

            var invoices = await db.Invoices.AsNoTracking()
                .Where(i => i.UserId == userId)
                .ToListAsync();
            Assert.True(invoices.Count <= 1, $"Recovery created {invoices.Count} invoices; it must never create more than one.");
        }
    }
}
