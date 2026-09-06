using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain.Billing;
using OetLearner.Api.Services.Billing;
using Stripe;
using Stripe.Checkout;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// Idempotency-gate vectors for the Fulfilment deepening (F1, #199).
/// Replays must converge without duplicate grants, events, or notifications;
/// Stripe round-trips must not run for already-fulfilled sessions.
/// </summary>
public sealed class FulfillmentGateTests
{
    private static LearnerDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static FulfillmentService NewService(LearnerDbContext db, CountingStripeService stripe)
        => new(db, stripe, walletService: null!, NullLogger<FulfillmentService>.Instance);

    private static CheckoutSession FulfilledSession(string userId, string? stripeSessionId, string? gatewayOrderId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        StripeSessionId = stripeSessionId,
        GatewayOrderId = gatewayOrderId,
        IdempotencyKey = $"idem-{Guid.NewGuid():N}",
        Status = "fulfilled",
        TotalAmount = 49m,
        Currency = "GBP",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        FulfilledAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task CheckoutFulfilled_Replay_PerformsNoStripeCalls()
    {
        await using var db = NewContext();
        db.CheckoutSessions.Add(FulfilledSession("u1", "cs_fulfilled", null));
        await db.SaveChangesAsync();
        var stripe = new CountingStripeService();
        var svc = NewService(db, stripe);

        await svc.FulfillCheckoutAsync("cs_fulfilled", CancellationToken.None);
        await svc.FulfillCheckoutAsync("cs_fulfilled", CancellationToken.None);

        Assert.Equal(0, stripe.RetrieveCheckoutSessionCalls);
        Assert.Equal("fulfilled", (await db.CheckoutSessions.SingleAsync()).Status);
    }

    [Fact]
    public async Task CartFulfilled_Replay_WritesNoAdditionalEvents()
    {
        await using var db = NewContext();
        db.CheckoutSessions.Add(FulfilledSession("u1", null, "order_fulfilled"));
        await db.SaveChangesAsync();
        var svc = NewService(db, new CountingStripeService());

        await svc.FulfillCartByGatewayOrderAsync("order_fulfilled", CancellationToken.None);
        await svc.FulfillCartByGatewayOrderAsync("order_fulfilled", CancellationToken.None);

        Assert.Empty(await db.BillingEvents.ToListAsync());
        Assert.Equal("fulfilled", (await db.CheckoutSessions.SingleAsync()).Status);
    }

    [Fact]
    public async Task RenewalReplay_SkipsDuplicateSideEffects()
    {
        await using var db = NewContext();
        var now = DateTimeOffset.UtcNow;
        var productId = Guid.NewGuid();
        db.BillingProducts.Add(new BillingProduct
        {
            Id = productId,
            Code = "renewal-mocks",
            Name = "Renewal mocks",
            ProductType = "package",
            IsActive = true,
            MetadataJson = """{"mocks":2}""",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.CustomerSubscriptions.Add(new CustomerSubscription
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            StripeSubscriptionId = "sub_renew",
            StripePriceId = "price_1",
            BillingProductId = productId,
            Status = "active",
            CurrentPeriodStart = now.AddDays(-30),
            CurrentPeriodEnd = now.AddDays(-1),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var periodStart = DateTime.SpecifyKind(now.AddDays(-30).UtcDateTime, DateTimeKind.Utc);
        var periodEnd = DateTime.SpecifyKind(now.AddDays(-1).UtcDateTime, DateTimeKind.Utc);
        var stripe = new CountingStripeService { PeriodStart = periodStart, PeriodEnd = periodEnd };
        var svc = NewService(db, stripe);

        await svc.FulfillSubscriptionRenewalAsync("sub_renew", CancellationToken.None);
        await svc.FulfillSubscriptionRenewalAsync("sub_renew", CancellationToken.None);

        Assert.Equal(1, await db.BillingEvents.CountAsync(e => e.EventType == "subscription.renewed"));
        Assert.Equal(1, await db.BillingEvents.CountAsync(e => e.EventType == "fulfillment.mocks_granted"));
    }

    [Fact]
    public async Task RenewalNewPeriod_GrantsAgain()
    {
        await using var db = NewContext();
        var now = DateTimeOffset.UtcNow;
        var productId = Guid.NewGuid();
        db.BillingProducts.Add(new BillingProduct
        {
            Id = productId,
            Code = "renewal-mocks",
            Name = "Renewal mocks",
            ProductType = "package",
            IsActive = true,
            MetadataJson = """{"mocks":2}""",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.CustomerSubscriptions.Add(new CustomerSubscription
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            StripeSubscriptionId = "sub_renew2",
            StripePriceId = "price_1",
            BillingProductId = productId,
            Status = "active",
            CurrentPeriodStart = now.AddDays(-60),
            CurrentPeriodEnd = now.AddDays(-31),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stripe = new CountingStripeService
        {
            PeriodStart = DateTime.SpecifyKind(now.AddDays(-60).UtcDateTime, DateTimeKind.Utc),
            PeriodEnd = DateTime.SpecifyKind(now.AddDays(-31).UtcDateTime, DateTimeKind.Utc),
        };
        var svc = NewService(db, stripe);
        await svc.FulfillSubscriptionRenewalAsync("sub_renew2", CancellationToken.None);

        stripe.PeriodStart = DateTime.SpecifyKind(now.AddDays(-30).UtcDateTime, DateTimeKind.Utc);
        stripe.PeriodEnd = DateTime.SpecifyKind(now.AddDays(-1).UtcDateTime, DateTimeKind.Utc);
        await svc.FulfillSubscriptionRenewalAsync("sub_renew2", CancellationToken.None);

        Assert.Equal(2, await db.BillingEvents.CountAsync(e => e.EventType == "subscription.renewed"));
    }

    private sealed class CountingStripeService : IStripeService
    {
        public int RetrieveCheckoutSessionCalls { get; private set; }
        public int RetrieveSubscriptionCalls { get; private set; }
        public int InvoiceSubscriptionCalls { get; private set; }
        public DateTime PeriodStart { get; set; } = DateTime.UtcNow.AddDays(-30);
        public DateTime PeriodEnd { get; set; } = DateTime.UtcNow.AddDays(-1);

        public Task<Session> RetrieveCheckoutSessionAsync(string sessionId, CancellationToken ct = default)
        {
            RetrieveCheckoutSessionCalls++;
            return Task.FromResult(new Session { Id = sessionId });
        }

        public Task<Subscription> RetrieveSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
        {
            RetrieveSubscriptionCalls++;
            return Task.FromResult(new Subscription
            {
                Id = subscriptionId,
                Status = "active",
                CurrentPeriodStart = PeriodStart,
                CurrentPeriodEnd = PeriodEnd,
            });
        }

        public Task<string?> GetInvoiceSubscriptionIdAsync(string invoiceId, CancellationToken ct = default)
        {
            InvoiceSubscriptionCalls++;
            return Task.FromResult<string?>("sub_renew");
        }

        public Task<string> EnsureCustomerAsync(string userId, string email, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<(string SessionId, string Url)> CreateCheckoutSessionAsync(CreateCheckoutSessionRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<(string SessionId, string Url)> CreateAdHocPaymentCheckoutSessionAsync(string stripeCustomerId, string userId, string userEmail, string currency, long amountMinorUnits, string productName, string successUrl, string cancelUrl, string? idempotencyKey, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> CreatePortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> CreateRefundAsync(string paymentIntentId, long? amountCents, string? reason, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Event ConstructWebhookEvent(string requestBody, string signatureHeader, string webhookSecret)
            => throw new NotImplementedException();
        public Task CancelSubscriptionAsync(string subscriptionId, bool cancelAtPeriodEnd = true, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task UpdateSubscriptionAsync(string subscriptionId, string newPriceId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task UpdateSubscriptionAsync(string subscriptionId, string newPriceId, bool prorate, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task PauseSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task PauseSubscriptionAsync(string subscriptionId, DateTimeOffset? resumeAt, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task ResumeSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task ApplyCouponToSubscriptionAsync(string subscriptionId, string? couponId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IEnumerable<Invoice>> ListInvoicesAsync(string stripeCustomerId, int limit = 24, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PayInvoiceResult> PayInvoiceAsync(string stripeInvoiceId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> CreateCouponAsync(CreateStripeCouponRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> CreatePromotionCodeAsync(string couponId, string code, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
