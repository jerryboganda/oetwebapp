using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain.Billing;

namespace OetLearner.Api.Services.Billing;

public sealed class CheckoutService : ICheckoutService
{
    private readonly LearnerDbContext _db;
    private readonly IStripeService _stripe;
    private readonly ICartService _cartService;
    private readonly BillingOptions _billingOptions;

    public CheckoutService(
        LearnerDbContext db,
        IStripeService stripe,
        ICartService cartService,
        IOptions<BillingOptions> billingOptions)
    {
        _db = db;
        _stripe = stripe;
        _cartService = cartService;
        _billingOptions = billingOptions.Value;
    }

    public async Task<CheckoutSessionDto> CreateCheckoutSessionAsync(
        string userId, string email, string cartId, CancellationToken ct = default)
    {
        var idempotencyKey = $"checkout_{userId}_{cartId}";

        // Return existing pending session if idempotency key already exists
        var existing = await _db.CheckoutSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.IdempotencyKey == idempotencyKey, ct);

        if (existing is not null && existing.Status == "pending"
            && existing.StripeSessionId is not null)
        {
            // Re-retrieve the Stripe URL from Stripe since we don't persist it locally
            var stripeSession = await _stripe.RetrieveCheckoutSessionAsync(existing.StripeSessionId, ct);
            var url = stripeSession.Url ?? string.Empty;
            return new CheckoutSessionDto(
                existing.Id,
                existing.StripeSessionId!,
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

        // Build line items
        var lineItems = cart.Items
            .Select(i => new CheckoutLineItem(i.BillingPriceId.ToString(), i.Quantity))
            .ToList();

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
                if (string.IsNullOrEmpty(bp.StripePriceId))
                    throw new InvalidOperationException($"BillingPrice '{i.BillingPriceId}' has no StripePriceId.");
                return new CheckoutLineItem(bp.StripePriceId, i.Quantity);
            })
            .ToList();

        // Resolve Stripe customer
        var stripeCustomerId = await _stripe.EnsureCustomerAsync(userId, email, ct);

        var successUrl = _billingOptions.Stripe.SuccessUrl
            ?? $"{_billingOptions.CheckoutBaseUrl}/checkout/success";
        var cancelUrl = _billingOptions.Stripe.CancelUrl
            ?? $"{_billingOptions.CheckoutBaseUrl}/checkout/cancel";

        var request = new CreateCheckoutSessionRequest(
            StripeCustomerId: stripeCustomerId,
            UserId: userId,
            UserEmail: email,
            LineItems: stripeLineItems,
            Mode: mode,
            SuccessUrl: successUrl,
            CancelUrl: cancelUrl,
            IdempotencyKey: idempotencyKey,
            Currency: cart.Currency.ToLowerInvariant());

        var (sessionId, sessionUrl) = await _stripe.CreateCheckoutSessionAsync(request, ct);

        var now = DateTimeOffset.UtcNow;
        var checkoutSession = new CheckoutSession
        {
            Id = Guid.NewGuid(),
            CartId = Guid.TryParse(cartId, out var cid) ? cid : (Guid?)null,
            UserId = userId,
            StripeSessionId = sessionId,
            IdempotencyKey = idempotencyKey,
            Status = "pending",
            TotalAmount = cart.Total,
            Currency = cart.Currency.ToUpperInvariant(),
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddHours(24)
        };

        _db.CheckoutSessions.Add(checkoutSession);
        await _db.SaveChangesAsync(ct);

        return new CheckoutSessionDto(
            checkoutSession.Id,
            sessionId,
            sessionUrl,
            checkoutSession.Status,
            checkoutSession.TotalAmount,
            checkoutSession.Currency);
    }

    public async Task<CheckoutSessionStatusDto?> GetSessionStatusAsync(
        string userId, Guid sessionId, CancellationToken ct = default)
    {
        // Object-level authorization: scope the lookup to the caller so a learner
        // can only read the status of their OWN checkout session (not any session by guid).
        var session = await _db.CheckoutSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

        return session is null
            ? null
            : new CheckoutSessionStatusDto(session.Id, session.Status, session.FulfilledAt);
    }

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
}
