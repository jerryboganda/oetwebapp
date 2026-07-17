using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Domain.Billing;

namespace OetWithDrHesham.Api.Services.Billing;

public sealed class CheckoutService : ICheckoutService
{
    // SQLite is used by the desktop runtime and focused tests. It has no
    // cross-process advisory locks, so serialize its short checkout-create path
    // in-process. PostgreSQL uses a transaction-scoped advisory lock below,
    // which also coordinates separate API instances.
    private static readonly SemaphoreSlim NonPostgresCheckoutGate = new(1, 1);

    private readonly LearnerDbContext _db;
    private readonly IStripeService _stripe;
    private readonly ICartService _cartService;
    private readonly BillingOptions _billingOptions;
    private readonly PaymentGatewayService _paymentGateways;

    public CheckoutService(
        LearnerDbContext db,
        IStripeService stripe,
        ICartService cartService,
        IOptions<BillingOptions> billingOptions,
        PaymentGatewayService paymentGateways)
    {
        _db = db;
        _stripe = stripe;
        _cartService = cartService;
        _billingOptions = billingOptions.Value;
        _paymentGateways = paymentGateways;
    }

    public async Task<CheckoutSessionDto> CreateCheckoutSessionAsync(
        string userId, string email, string cartId, string? successUrl = null, string? cancelUrl = null, CancellationToken ct = default)
    {
        var idempotencyKey = $"checkout_{userId}_{cartId}";
        var isPostgres = _db.Database.IsNpgsql();

        if (!isPostgres)
            await NonPostgresCheckoutGate.WaitAsync(ct);

        await using var transaction = isPostgres
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            if (isPostgres)
            {
                // Serialize this idempotency key before reading or calling Stripe.
                // The lock is released with the transaction, including exceptions.
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({idempotencyKey}, 0));",
                    ct);
            }

            // Keep the row tracked: a legacy row without a stored URL is backfilled
            // in this same transaction while the idempotency lock is held.
            var existing = await _db.CheckoutSessions
                .FirstOrDefaultAsync(s => s.IdempotencyKey == idempotencyKey, ct);

            if (existing is not null && existing.Status == "pending"
                && existing.StripeSessionId is not null)
            {
                var url = existing.HostedCheckoutUrl;
                if (string.IsNullOrWhiteSpace(url))
                {
                    // One provider read for pre-column rows, then all later replays
                    // use the persisted URL without touching Stripe.
                    var stripeSession = await _stripe.RetrieveCheckoutSessionAsync(existing.StripeSessionId, ct);
                    url = stripeSession.Url;
                    if (string.IsNullOrWhiteSpace(url))
                        throw new InvalidOperationException("Stripe checkout session did not include a hosted URL.");

                    existing.HostedCheckoutUrl = url;
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                    await _db.SaveChangesAsync(ct);
                }

                if (transaction is not null)
                    await transaction.CommitAsync(ct);

                return new CheckoutSessionDto(
                    existing.Id,
                    existing.StripeSessionId,
                    url,
                    existing.Status,
                    existing.TotalAmount,
                    existing.Currency);
            }

            // Load cart with items and prices (owner-scoped to the checkout caller)
            var cart = await _cartService.GetCartByIdAsync(cartId, userId, ct)
                ?? throw ApiException.NotFound("cart_not_found", "Cart not found.");

            if (cart.Items.Count == 0)
                throw ApiException.Validation("cart_empty", "Cart is empty.");

            // Determine checkout mode: "subscription" if any item has a recurring interval
            var mode = cart.Items.Any(i => i.Interval is not null) ? "subscription" : "payment";

            // Map BillingPriceId → StripePriceId
            var priceIds = cart.Items.Select(i => i.BillingPriceId).ToList();
            var billingPrices = await _db.BillingPrices
                .Where(p => priceIds.Contains(p.Id))
                .AsNoTracking()
                .ToListAsync(ct);

            var stripeLineItems = cart.Items
                .Select(i =>
                {
                    var bp = billingPrices.FirstOrDefault(p => p.Id == i.BillingPriceId)
                        ?? throw new InvalidOperationException($"BillingPrice '{i.BillingPriceId}' not found.");
                    return new CheckoutLineItem(
                        StripePriceId: bp.StripePriceId,
                        Quantity: i.Quantity,
                        UnitAmount: checked((long)bp.Amount),
                        Currency: bp.Currency,
                        ProductName: i.ProductName,
                        Interval: bp.Interval,
                        IntervalCount: bp.IntervalCount);
                })
                .ToList();

            // Resolve Stripe customer only after replay detection and while the
            // idempotency lock is held, so concurrent callers make one provider call.
            var stripeCustomerId = await _stripe.EnsureCustomerAsync(userId, email, ct);

            var resolvedSuccessUrl = EnsureStripeSessionPlaceholder(successUrl)
                ?? EnsureStripeSessionPlaceholder(_billingOptions.Stripe.SuccessUrl)
                ?? $"{_billingOptions.CheckoutBaseUrl}/checkout/success";
            var resolvedCancelUrl = cancelUrl
                ?? _billingOptions.Stripe.CancelUrl
                ?? $"{_billingOptions.CheckoutBaseUrl}/checkout/cancel";

            var request = new CreateCheckoutSessionRequest(
                StripeCustomerId: stripeCustomerId,
                UserId: userId,
                UserEmail: email,
                LineItems: stripeLineItems,
                Mode: mode,
                SuccessUrl: resolvedSuccessUrl,
                CancelUrl: resolvedCancelUrl,
                IdempotencyKey: idempotencyKey,
                Currency: cart.Currency.ToLowerInvariant());

            var (sessionId, sessionUrl) = await _stripe.CreateCheckoutSessionAsync(request, ct);
            if (string.IsNullOrWhiteSpace(sessionUrl))
                throw new InvalidOperationException("Stripe checkout session did not include a hosted URL.");

            var now = DateTimeOffset.UtcNow;
            var checkoutSession = new CheckoutSession
            {
                Id = Guid.NewGuid(),
                CartId = Guid.TryParse(cartId, out var cid) ? cid : (Guid?)null,
                UserId = userId,
                StripeSessionId = sessionId,
                HostedCheckoutUrl = sessionUrl,
                IdempotencyKey = idempotencyKey,
                Status = "pending",
                TotalAmount = cart.Total,
                Currency = cart.Currency.ToUpperInvariant(),
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = now.AddHours(24)
            };

            // Session id and hosted URL are written by the same SaveChanges call.
            _db.CheckoutSessions.Add(checkoutSession);
            await _db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);

            return new CheckoutSessionDto(
                checkoutSession.Id,
                sessionId,
                sessionUrl,
                checkoutSession.Status,
                checkoutSession.TotalAmount,
                checkoutSession.Currency);
        }
        finally
        {
            if (!isPostgres)
                NonPostgresCheckoutGate.Release();
        }
    }

    public async Task<PayPalCartOrderDto> CreatePayPalCartOrderAsync(
        string userId, string email, string cartId, CancellationToken ct = default)
    {
        var idempotencyKey = $"checkout_paypal_{userId}_{cartId}";

        // Reuse an existing pending PayPal order for this cart so a repeated create
        // (double-click / retry) does not open a second PayPal order.
        var existing = await _db.CheckoutSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null && existing.Status == "pending" && !string.IsNullOrEmpty(existing.GatewayOrderId))
        {
            return new PayPalCartOrderDto(existing.GatewayOrderId!, existing.Id, existing.TotalAmount, existing.Currency);
        }

        var cart = await _cartService.GetCartByIdAsync(cartId, userId, ct)
            ?? throw ApiException.NotFound("cart_not_found", "Cart not found.");

        if (cart.Items.Count == 0)
            throw ApiException.Validation("cart_empty", "Cart is empty.");

        // PayPal one-time capture cannot open a Stripe subscription, so a cart with any
        // recurring line must use card (Stripe). Surface a clean validation the picker
        // falls back on rather than letting the order creation fail downstream.
        if (cart.Items.Any(i => i.Interval is not null))
        {
            throw ApiException.Validation(
                "paypal_recurring_unsupported",
                "PayPal can't be used for subscription items. Please pay by card.");
        }

        var currency = cart.Currency.ToUpperInvariant();
        var successUrl = $"{_billingOptions.CheckoutBaseUrl}/checkout/success";
        var cancelUrl = $"{_billingOptions.CheckoutBaseUrl}/checkout/cancel";

        var intent = await _paymentGateways.GetGateway("paypal").CreatePaymentIntentAsync(new CreatePaymentIntentRequest(
            UserId: userId,
            Amount: cart.Total,
            Currency: currency,
            ProductType: "cart",
            ProductId: cartId,
            Description: $"OET with Dr Hesham cart ({cart.Items.Count} item(s))",
            Metadata: new Dictionary<string, string> { ["cart_id"] = cartId },
            SuccessUrl: successUrl,
            CancelUrl: cancelUrl,
            IdempotencyKey: idempotencyKey), ct);

        var now = DateTimeOffset.UtcNow;
        var checkoutSession = new CheckoutSession
        {
            Id = Guid.NewGuid(),
            CartId = Guid.TryParse(cartId, out var cid) ? cid : (Guid?)null,
            UserId = userId,
            Gateway = "paypal",
            GatewayOrderId = intent.GatewayTransactionId,
            IdempotencyKey = idempotencyKey,
            Status = "pending",
            TotalAmount = cart.Total,
            Currency = currency,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddHours(3),
        };

        _db.CheckoutSessions.Add(checkoutSession);
        await _db.SaveChangesAsync(ct);

        return new PayPalCartOrderDto(intent.GatewayTransactionId, checkoutSession.Id, cart.Total, currency);
    }

    public async Task<CheckoutSessionStatusDto?> GetSessionStatusAsync(
        string userId, string sessionId, CancellationToken ct = default)
    {
        var parsedGuid = Guid.TryParse(sessionId, out var guid);
        // Object-level authorization: scope the lookup to the caller so a learner
        // can only read the status of their OWN checkout session (not any session by guid).
        var session = await _db.CheckoutSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId
                && ((parsedGuid && s.Id == guid) || s.StripeSessionId == sessionId), ct);

        if (session is null) return null;

        var items = session.CartId.HasValue
            ? await _db.CartItems
                .Include(item => item.BillingProduct)
                .Where(item => item.CartId == session.CartId.Value)
                .AsNoTracking()
                .Select(item => new CheckoutSessionStatusItemDto(
                    item.BillingProduct.Code,
                    item.BillingProduct.Name,
                    item.Quantity,
                    item.BillingProduct.Description))
                .ToListAsync(ct)
            : [];

        var (deliveryMethod, fulfilmentStatus) = ResolveDelivery();

        return new CheckoutSessionStatusDto(
            session.StripeSessionId ?? session.Id.ToString(),
            session.Id,
            session.StripeSessionId,
            session.Status,
            session.FulfilledAt,
            session.TotalAmount,
            session.Currency,
            items,
            FailureReasonFor(session.Status),
            deliveryMethod,
            fulfilmentStatus);
    }

    /// <summary>
    /// Delivery method and subscription fulfilment state for a cart order — both
    /// <c>null</c>, because neither is knowable from a cart order. This is deliberate:
    /// reporting "unknown" is the only honest answer available here.
    ///
    /// <para><b>Why nothing can be resolved.</b> <see cref="DeliveryMethods"/> is a
    /// <see cref="BillingPlan"/> property, and a cart order references no plan. A
    /// <see cref="CartItem"/> points only at a <see cref="BillingProduct"/>/<see cref="BillingPrice"/>;
    /// <see cref="BillingProduct"/> carries no plan id, and its <c>MetadataJson</c> is
    /// <c>{category, source}</c> (written by <see cref="BillingCatalogSyncStartupTask"/>) — no plan
    /// reference. No join table exists. The two catalogues are seeded independently and their
    /// Codes are disjoint namespaces, NOT a shared one: products come from
    /// <c>scripts/StripeProductSeeder/catalog.json</c> as snake_case (<c>pkg_oet_mastery</c>,
    /// <c>addon_credits_10</c>, <c>sub_mastery_annual</c>), while plan Codes come from
    /// <c>Data/Seeds/oet-2026-catalog.json</c> <c>plans[]</c> as kebab-case
    /// (<c>full-nursing</c>, <c>crash-course</c>). Their intersection is empty — the three codes
    /// that do appear in both files (<c>pkg_quick_check</c>, <c>pkg_exam_prep_pro</c>,
    /// <c>pkg_oet_mastery</c>) are <c>addOns[]</c>, which seed <see cref="BillingAddOn"/>, not
    /// <see cref="BillingPlan"/>. A previous revision matched <c>productCodes</c> against
    /// <c>BillingPlan.Code</c> on the stated premise that the namespaces were shared; that join
    /// matched nothing on every order and silently reported a confident <c>automatic_web</c>.
    /// It is not restored here: with disjoint namespaces any hit would be a coincidental
    /// code collision, not a real association, so it could only ever launder a guess into a
    /// claim about the buyer's access.
    ///
    /// <para><b>Why FulfilmentStatus is null.</b> <see cref="CheckoutSession"/> holds no
    /// subscription link, and the cart pipeline never opens a domain <c>Subscription</c> —
    /// <c>FulfillmentService</c> grants product entitlements and upserts the Stripe-mirror
    /// <c>CustomerSubscription</c> only. There is nothing to read.</para>
    ///
    /// <para>Callers must treat null as UNKNOWN and say only what is actually known: the
    /// payment cleared. Asserting entitlements were added — or that the order is awaiting a
    /// hand-over — would both be fabrications (spec 2026-07-15 §2/§6.6, §7).</para>
    /// </summary>
    private static (string? DeliveryMethod, string? FulfilmentStatus) ResolveDelivery()
        => (null, null);

    public async Task ExpireSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.CheckoutSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null) return;

        var now = DateTimeOffset.UtcNow;
        session.Status = "expired";
        session.ExpiresAt = now;
        session.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
    }

    private static string? EnsureStripeSessionPlaceholder(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return url.Contains("{CHECKOUT_SESSION_ID}", StringComparison.Ordinal)
            || url.Contains("{SESSION_ID}", StringComparison.Ordinal)
            ? url.Replace("{SESSION_ID}", "{CHECKOUT_SESSION_ID}", StringComparison.Ordinal)
            : Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(url, "session_id", "{CHECKOUT_SESSION_ID}");
    }

    private static string? FailureReasonFor(string status)
        => status.ToLowerInvariant() switch
        {
            "failed" => "Payment did not complete.",
            "expired" => "Checkout expired before payment was completed.",
            _ => null
        };
}
