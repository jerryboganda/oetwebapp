using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Domain.Billing;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;
using Xunit;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// PAY-07/08/09/10 regression gate (security standard, audit §4.5): the
/// webhook fulfilment path must fail closed when an authoritative order cannot
/// be resolved or contradicts the payment. A verified webhook that cannot be
/// bound to a server-side order parks as failed and grants nothing.
/// </summary>
public sealed class WebhookOrderBindingGateTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public WebhookOrderBindingGateTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CompletedWebhook_WithoutResolvableQuote_ParksFailed_AndGrantsNothing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;
        var userId = $"binding-user-{suffix}";
        var gatewayTransactionId = $"gw-txn-{suffix}";
        var eventId = Guid.NewGuid();

        // A subscription payment transaction that references NO quote at all:
        // no QuoteId, no metadata quoteId, no CheckoutSession link. Pre-fix this
        // fell through the binding gate and completed the transaction.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            SeedLearner(db, userId, now);
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = userId,
                Gateway = "stripe",
                GatewayTransactionId = gatewayTransactionId,
                TransactionType = "subscription_payment",
                Status = "pending",
                Amount = 199m,
                Currency = "AUD",
                ProductType = "plan",
                ProductId = "some-plan",
                MetadataJson = "{}",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.PaymentWebhookEvents.Add(VerifiedCompletedEvent(eventId, gatewayTransactionId, now));
            await db.SaveChangesAsync();
        }

        var result = await DriveAsync(eventId, gatewayTransactionId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            // Event parked for reviewed resolution, NOT completed.
            Assert.Equal("failed", result.ProcessingStatus);

            // Transaction was NOT completed and NO subscription was activated.
            var transaction = await db.PaymentTransactions.SingleAsync(t => t.GatewayTransactionId == gatewayTransactionId);
            Assert.Equal("pending", transaction.Status);
            Assert.Null(await db.Subscriptions.FirstOrDefaultAsync(s => s.Status == SubscriptionStatus.Active && s.UserId == userId));
        }
    }

    [Fact]
    public async Task CompletedWebhook_AmountContradictsQuote_ParksFailed_AndGrantsNothing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;
        var userId = $"binding-user-{suffix}";
        var quoteId = $"quote-{suffix}";
        var gatewayTransactionId = $"gw-txn-{suffix}";
        var eventId = Guid.NewGuid();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            SeedLearner(db, userId, now);
            db.BillingQuotes.Add(new BillingQuote
            {
                Id = quoteId,
                UserId = userId,
                SubscriptionId = null,
                PlanCode = null,
                PlanVersionId = null,
                AddOnCodesJson = "[]",
                AddOnVersionIdsJson = "{}",
                Currency = "AUD",
                SubtotalAmount = 199m,
                DiscountAmount = 0m,
                TotalAmount = 199m,
                Status = BillingQuoteStatus.Applied,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(30),
                SnapshotJson = OetLearner.Api.Services.JsonSupport.Serialize(new { items = Array.Empty<object>(), summary = "Test", validation = new { } }),
            });
            // Transaction reports 1 AUD against a 199 AUD quote — binding must fail.
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = userId,
                Gateway = "stripe",
                GatewayTransactionId = gatewayTransactionId,
                TransactionType = "subscription_payment",
                Status = "pending",
                Amount = 1m,
                Currency = "AUD",
                ProductType = "plan",
                ProductId = "some-plan",
                QuoteId = quoteId,
                MetadataJson = OetLearner.Api.Services.JsonSupport.Serialize(new { quoteId }),
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.PaymentWebhookEvents.Add(VerifiedCompletedEvent(eventId, gatewayTransactionId, now));
            await db.SaveChangesAsync();
        }

        var result = await DriveAsync(eventId, gatewayTransactionId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            Assert.Equal("failed", result.ProcessingStatus);
            var transaction = await db.PaymentTransactions.SingleAsync(t => t.GatewayTransactionId == gatewayTransactionId);
            Assert.Equal("pending", transaction.Status);
            // Quote is not consumed — access not granted.
            var quote = await db.BillingQuotes.SingleAsync(q => q.Id == quoteId);
            Assert.NotEqual(BillingQuoteStatus.Completed, quote.Status);
        }
    }

    [Fact]
    public async Task WalletTopUpWebhook_ProviderAmountContradictsTransaction_ParksFailed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;
        var userId = $"binding-user-{suffix}";
        var gatewayTransactionId = $"gw-txn-{suffix}";
        var eventId = Guid.NewGuid();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            SeedLearner(db, userId, now);
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = userId,
                Gateway = "stripe",
                GatewayTransactionId = gatewayTransactionId,
                TransactionType = "wallet_top_up",
                Status = "pending",
                Amount = 25m,
                Currency = "AUD",
                ProductType = "wallet_top_up",
                ProductId = $"wallet-{suffix}",
                MetadataJson = "{}",
                CreatedAt = now,
                UpdatedAt = now,
            });
            // Stripe-shaped payload reporting 1.00 in the same currency.
            db.PaymentWebhookEvents.Add(VerifiedCompletedEvent(
                eventId,
                gatewayTransactionId,
                now,
                payloadJson: """{"data":{"object":{"amount_total":100,"currency":"aud"}}}"""));
            await db.SaveChangesAsync();
        }

        var result = await DriveAsync(eventId, gatewayTransactionId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            Assert.Equal("failed", result.ProcessingStatus);
            var transaction = await db.PaymentTransactions.SingleAsync(t => t.GatewayTransactionId == gatewayTransactionId);
            Assert.Equal("pending", transaction.Status);
        }
    }

    // ── Driver ────────────────────────────────────────────────────────────────

    private async Task<OetLearner.Api.Services.PaymentWebhookRetryResult> DriveAsync(Guid eventId, string gatewayTransactionId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<LearnerService>();
        return await service.ApplyVerifiedPaymentWebhookEventAsync(
            eventId,
            gatewayTransactionId,
            normalizedStatus: "completed",
            eventCategory: "payment",
            gatewayObjectId: gatewayTransactionId,
            CancellationToken.None);
    }

    private static void SeedLearner(LearnerDbContext db, string userId, DateTimeOffset now)
    {
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Binding Learner",
            Email = $"{userId}@example.test",
            CreatedAt = now,
            LastActiveAt = now,
            AccountStatus = "active",
        });
    }

    private static PaymentWebhookEvent VerifiedCompletedEvent(
        Guid eventId,
        string gatewayTransactionId,
        DateTimeOffset now,
        string? payloadJson = null)
        => new()
        {
            Id = eventId,
            Gateway = "stripe",
            EventType = "checkout.session.completed",
            GatewayEventId = $"evt-{eventId:N}",
            ProcessingStatus = "failed",
            VerificationStatus = "verified",
            VerifiedAt = now,
            PayloadSha256 = new string('a', 64),
            ParserVersion = "payment-webhook-v1",
            GatewayTransactionId = gatewayTransactionId,
            NormalizedStatus = "completed",
            AttemptCount = 1,
            PayloadJson = payloadJson ?? "{}",
            ErrorMessage = "Transient local fulfillment failure.",
            ReceivedAt = now,
            ProcessedAt = now,
        };
}
