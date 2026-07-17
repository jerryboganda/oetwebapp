using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain.Billing;
using OetWithDrHesham.Api.Services.Billing;
using Stripe;
using Stripe.Checkout;
using BillingCheckoutService = OetWithDrHesham.Api.Services.Billing.CheckoutService;

namespace OetWithDrHesham.Api.Tests;

public sealed class CheckoutSessionReplayTests
{
    [Fact]
    public async Task FirstCreatePersistsUrl_AndReplayMakesZeroProviderCalls()
    {
        await using var fixture = await CheckoutFixture.CreateAsync();
        var stripe = new CountingStripeService();
        await using var db = fixture.CreateContext();
        var service = fixture.CreateService(db, stripe);

        var first = await service.CreateCheckoutSessionAsync(
            fixture.UserId,
            fixture.Email,
            fixture.CartId,
            ct: CancellationToken.None);

        Assert.Equal(1, stripe.EnsureCustomerCallCount);
        Assert.Equal(1, stripe.CreateCallCount);
        Assert.Equal(0, stripe.RetrieveCallCount);
        var firstLineItem = Assert.Single(stripe.LastCreateRequest!.LineItems);
        Assert.Null(firstLineItem.StripePriceId);
        Assert.Equal(49, firstLineItem.UnitAmount);
        Assert.Equal("GBP", firstLineItem.Currency);
        Assert.Equal("Test Product", firstLineItem.ProductName);

        var callsAfterFirst = stripe.TotalProviderCallCount;
        var replay = await service.CreateCheckoutSessionAsync(
            fixture.UserId,
            fixture.Email,
            fixture.CartId,
            ct: CancellationToken.None);

        Assert.Equal(callsAfterFirst, stripe.TotalProviderCallCount);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(first.StripeSessionId, replay.StripeSessionId);
        Assert.Equal(first.Url, replay.Url);

        db.ChangeTracker.Clear();
        var stored = await db.CheckoutSessions.SingleAsync();
        Assert.Equal(first.StripeSessionId, stored.StripeSessionId);
        Assert.Equal(first.Url, stored.HostedCheckoutUrl);
    }

    [Fact]
    public void MissingStripePriceId_BuildsInlineStripePriceData()
    {
        var options = StripeService.BuildLineItemOptions(new CheckoutLineItem(
            StripePriceId: null,
            Quantity: 2,
            UnitAmount: 500,
            Currency: "GBP",
            ProductName: "Priority grade"));

        Assert.Null(options.Price);
        Assert.Equal(2, options.Quantity);
        Assert.Equal(500, options.PriceData.UnitAmount);
        Assert.Equal("gbp", options.PriceData.Currency);
        Assert.Equal("Priority grade", options.PriceData.ProductData.Name);
        Assert.Null(options.PriceData.Recurring);
    }

    [Fact]
    public async Task LegacyRowWithoutUrl_RetrievesOncePersistsThenReplaysLocally()
    {
        await using var fixture = await CheckoutFixture.CreateAsync();
        var stripe = new CountingStripeService
        {
            RetrievedUrl = "https://checkout.stripe.test/legacy-session"
        };
        await using var db = fixture.CreateContext();
        var now = DateTimeOffset.UtcNow;
        db.CheckoutSessions.Add(new CheckoutSession
        {
            Id = Guid.NewGuid(),
            CartId = Guid.Parse(fixture.CartId),
            UserId = fixture.UserId,
            StripeSessionId = "cs_legacy",
            HostedCheckoutUrl = null,
            IdempotencyKey = fixture.IdempotencyKey,
            Status = "pending",
            TotalAmount = 49m,
            Currency = "GBP",
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now.AddDays(-1),
            ExpiresAt = now.AddHours(1)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = fixture.CreateService(db, stripe);
        var firstReplay = await service.CreateCheckoutSessionAsync(
            fixture.UserId,
            fixture.Email,
            fixture.CartId,
            ct: CancellationToken.None);
        var secondReplay = await service.CreateCheckoutSessionAsync(
            fixture.UserId,
            fixture.Email,
            fixture.CartId,
            ct: CancellationToken.None);

        Assert.Equal("https://checkout.stripe.test/legacy-session", firstReplay.Url);
        Assert.Equal(firstReplay.Url, secondReplay.Url);
        Assert.Equal(1, stripe.RetrieveCallCount);
        Assert.Equal(0, stripe.EnsureCustomerCallCount);
        Assert.Equal(0, stripe.CreateCallCount);

        db.ChangeTracker.Clear();
        Assert.Equal(
            "https://checkout.stripe.test/legacy-session",
            (await db.CheckoutSessions.SingleAsync()).HostedCheckoutUrl);
    }

    [Fact]
    public async Task ConcurrentCreatesForSameCart_MakeOneProviderCreateAndReturnSameSession()
    {
        await using var fixture = await CheckoutFixture.CreateAsync();
        var stripe = new CountingStripeService { PauseCreate = true };
        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();
        var firstService = fixture.CreateService(firstDb, stripe);
        var secondService = fixture.CreateService(secondDb, stripe);

        var firstTask = firstService.CreateCheckoutSessionAsync(
            fixture.UserId,
            fixture.Email,
            fixture.CartId,
            ct: CancellationToken.None);
        await stripe.CreateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondTask = secondService.CreateCheckoutSessionAsync(
            fixture.UserId,
            fixture.Email,
            fixture.CartId,
            ct: CancellationToken.None);
        await Task.Delay(50);
        stripe.AllowCreate.TrySetResult();

        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(1, stripe.EnsureCustomerCallCount);
        Assert.Equal(1, stripe.CreateCallCount);
        Assert.Equal(0, stripe.RetrieveCallCount);
        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Equal(results[0].StripeSessionId, results[1].StripeSessionId);
        Assert.Equal(results[0].Url, results[1].Url);
        Assert.Equal(1, fixture.Cart.GetCallCount);

        await using var verificationDb = fixture.CreateContext();
        var stored = Assert.Single(await verificationDb.CheckoutSessions.ToListAsync());
        Assert.Equal(results[0].StripeSessionId, stored.StripeSessionId);
        Assert.Equal(results[0].Url, stored.HostedCheckoutUrl);
    }

    private sealed class CheckoutFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _anchor;
        private readonly DbContextOptions<LearnerDbContext> _options;

        private CheckoutFixture(
            SqliteConnection anchor,
            DbContextOptions<LearnerDbContext> options,
            Guid priceId)
        {
            _anchor = anchor;
            _options = options;
            CartId = Guid.NewGuid().ToString();
            Cart = new FakeCartService(new CartDto(
                CartId,
                UserId,
                "active",
                [
                    new CartItemDto(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "test-product",
                        "Test Product",
                        "package",
                        priceId,
                        49m,
                        "GBP",
                        null,
                        1,
                        49m)
                ],
                [],
                49m,
                0m,
                49m,
                "GBP",
                DateTimeOffset.UtcNow.AddHours(1)));
        }

        public string UserId { get; } = "checkout-user";
        public string Email => $"{UserId}@example.test";
        public string CartId { get; }
        public string IdempotencyKey => $"checkout_{UserId}_{CartId}";
        public FakeCartService Cart { get; }

        public static async Task<CheckoutFixture> CreateAsync()
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"checkout-replay-{Guid.NewGuid():N}",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
                DefaultTimeout = 10
            }.ToString();
            var anchor = new SqliteConnection(connectionString);
            await anchor.OpenAsync();
            var options = new DbContextOptionsBuilder<LearnerDbContext>()
                .UseSqlite(connectionString)
                .Options;

            var productId = Guid.NewGuid();
            var priceId = Guid.NewGuid();
            await using (var db = new LearnerDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                var now = DateTimeOffset.UtcNow;
                db.BillingProducts.Add(new BillingProduct
                {
                    Id = productId,
                    Code = "test-product",
                    Name = "Test Product",
                    ProductType = "package",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Prices =
                    [
                        new BillingPrice
                        {
                            Id = priceId,
                            BillingProductId = productId,
                            StripePriceId = null,
                            Currency = "GBP",
                            Amount = 49m,
                            IsActive = true,
                            CreatedAt = now,
                            UpdatedAt = now
                        }
                    ]
                });
                await db.SaveChangesAsync();
            }

            return new CheckoutFixture(anchor, options, priceId);
        }

        public LearnerDbContext CreateContext() => new(_options);

        public BillingCheckoutService CreateService(LearnerDbContext db, IStripeService stripe)
            => new(
                db,
                stripe,
                Cart,
                Options.Create(new BillingOptions
                {
                    CheckoutBaseUrl = "https://app.example.test"
                }),
                paymentGateways: null!);

        public async ValueTask DisposeAsync()
        {
            await _anchor.DisposeAsync();
        }
    }

    private sealed class FakeCartService(CartDto cart) : ICartService
    {
        private int _getCallCount;
        public int GetCallCount => Volatile.Read(ref _getCallCount);

        public Task<CartDto?> GetCartByIdAsync(
            string cartId,
            string userId,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _getCallCount);
            return Task.FromResult<CartDto?>(
                cartId == cart.Id && userId == cart.UserId ? cart : null);
        }

        public Task<CartDto> GetOrCreateCartAsync(string? userId, string? sessionToken, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<CartDto> AddItemAsync(string cartId, string userId, AddCartItemRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<CartDto> UpdateItemQuantityAsync(string cartId, string userId, Guid itemId, int quantity, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<CartDto> RemoveItemAsync(string cartId, string userId, Guid itemId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<CartDto> ApplyPromoCodeAsync(string cartId, string userId, string code, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<CartDto> RemovePromoCodeAsync(string cartId, string userId, string code, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task ClearCartAsync(string cartId, string userId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task MergeAnonymousCartAsync(string sessionToken, string userId, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class CountingStripeService : IStripeService
    {
        private int _ensureCustomerCallCount;
        private int _createCallCount;
        private int _retrieveCallCount;

        public int EnsureCustomerCallCount => Volatile.Read(ref _ensureCustomerCallCount);
        public int CreateCallCount => Volatile.Read(ref _createCallCount);
        public int RetrieveCallCount => Volatile.Read(ref _retrieveCallCount);
        public CreateCheckoutSessionRequest? LastCreateRequest { get; private set; }
        public int TotalProviderCallCount =>
            EnsureCustomerCallCount + CreateCallCount + RetrieveCallCount;
        public bool PauseCreate { get; init; }
        public string RetrievedUrl { get; init; } = "https://checkout.stripe.test/retrieved";
        public TaskCompletionSource CreateEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowCreate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> EnsureCustomerAsync(
            string userId,
            string email,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _ensureCustomerCallCount);
            return Task.FromResult("cus_test");
        }

        public async Task<(string SessionId, string Url)> CreateCheckoutSessionAsync(
            CreateCheckoutSessionRequest request,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _createCallCount);
            LastCreateRequest = request;
            CreateEntered.TrySetResult();
            if (PauseCreate)
                await AllowCreate.Task.WaitAsync(ct);
            return ("cs_test", "https://checkout.stripe.test/cs_test");
        }

        public Task<Session> RetrieveCheckoutSessionAsync(
            string sessionId,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _retrieveCallCount);
            return Task.FromResult(new Session
            {
                Id = sessionId,
                Url = RetrievedUrl
            });
        }

        public Task<(string SessionId, string Url)> CreateAdHocPaymentCheckoutSessionAsync(
            string stripeCustomerId,
            string userId,
            string userEmail,
            string currency,
            long amountMinorUnits,
            string productName,
            string successUrl,
            string cancelUrl,
            string? idempotencyKey,
            IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> CreatePortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> CreateRefundAsync(string paymentIntentId, long? amountCents, string? reason, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Event ConstructWebhookEvent(string requestBody, string signatureHeader, string webhookSecret)
            => throw new NotImplementedException();
        public Task<Stripe.Subscription> RetrieveSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
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
        public Task<string?> GetInvoiceSubscriptionIdAsync(string invoiceId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PayInvoiceResult> PayInvoiceAsync(string stripeInvoiceId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> CreateCouponAsync(CreateStripeCouponRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> CreatePromotionCodeAsync(string couponId, string code, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
