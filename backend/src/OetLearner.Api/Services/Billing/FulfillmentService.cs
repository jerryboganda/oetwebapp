using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain.Billing;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Billing;

public sealed class FulfillmentService : IFulfillmentService
{
    private readonly LearnerDbContext _db;
    private readonly IStripeService _stripe;
    private readonly WalletService _walletService;
    private readonly ILogger<FulfillmentService> _logger;

    public FulfillmentService(
        LearnerDbContext db,
        IStripeService stripe,
        WalletService walletService,
        ILogger<FulfillmentService> logger)
    {
        _db = db;
        _stripe = stripe;
        _walletService = walletService;
        _logger = logger;
    }

    public async Task FulfillCheckoutAsync(string stripeSessionId, CancellationToken ct = default)
    {
        // Idempotency: skip if already fulfilled
        var checkoutSession = await _db.CheckoutSessions
            .FirstOrDefaultAsync(s => s.StripeSessionId == stripeSessionId, ct);

        if (checkoutSession is null)
        {
            _logger.LogWarning("FulfillCheckoutAsync: no CheckoutSession row for StripeSessionId={SessionId}", stripeSessionId);
            return;
        }

        if (checkoutSession.Status == "fulfilled")
        {
            _logger.LogInformation("FulfillCheckoutAsync: session {SessionId} already fulfilled, skipping.", stripeSessionId);
            return;
        }

        // Retrieve the Stripe session to confirm payment
        var stripeSession = await _stripe.RetrieveCheckoutSessionAsync(stripeSessionId, ct);

        var userId = checkoutSession.UserId;

        // Load cart items with prices and products
        if (checkoutSession.CartId.HasValue)
        {
            var cartItems = await _db.CartItems
                .Include(i => i.BillingPrice)
                .Include(i => i.BillingProduct)
                .Where(i => i.CartId == checkoutSession.CartId.Value)
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var item in cartItems)
            {
                var price = item.BillingPrice;
                var product = item.BillingProduct;

                if (price.Interval is not null)
                {
                    // Subscription item: create or update CustomerSubscription row
                    var stripeSubId = stripeSession.SubscriptionId;
                    if (!string.IsNullOrEmpty(stripeSubId))
                    {
                        await UpsertCustomerSubscriptionAsync(userId, stripeSubId, price, product.Id, ct);
                    }
                }
                else
                {
                    // One-time purchase: grant credits via WalletService
                    await GrantCreditsForProductAsync(userId, product, price, item.Quantity,
                        referenceId: stripeSessionId, ct);
                }
            }

            // Mark cart as converted
            var cart = await _db.Carts.FirstOrDefaultAsync(c => c.Id == checkoutSession.CartId.Value, ct);
            if (cart is not null)
            {
                cart.Status = "converted";
                cart.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        // Mark checkout session as fulfilled
        var now = DateTimeOffset.UtcNow;
        checkoutSession.Status = "fulfilled";
        checkoutSession.FulfilledAt = now;
        checkoutSession.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("FulfillCheckoutAsync: fulfilled session {SessionId} for user {UserId}.", stripeSessionId, userId);
    }

    public async Task FulfillSubscriptionRenewalAsync(string stripeSubscriptionId, CancellationToken ct = default)
    {
        var sub = await _db.CustomerSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId, ct);

        if (sub is null)
        {
            _logger.LogWarning("FulfillSubscriptionRenewalAsync: no CustomerSubscription for {SubId}", stripeSubscriptionId);
            return;
        }

        // Refresh period dates from Stripe
        var stripeSub = await _stripe.RetrieveSubscriptionAsync(stripeSubscriptionId, ct);

        var now = DateTimeOffset.UtcNow;
        sub.CurrentPeriodStart = new DateTimeOffset(stripeSub.CurrentPeriodStart, TimeSpan.Zero);
        sub.CurrentPeriodEnd = new DateTimeOffset(stripeSub.CurrentPeriodEnd, TimeSpan.Zero);
        sub.Status = stripeSub.Status;
        sub.UpdatedAt = now;

        // Grant renewal credits if a product is linked
        if (sub.BillingProductId.HasValue)
        {
            var product = await _db.BillingProducts
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == sub.BillingProductId.Value, ct);

            var price = await _db.BillingPrices
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.StripePriceId == sub.StripePriceId, ct);

            if (product is not null && price is not null)
            {
                var renewalIdemKey = $"renewal_{stripeSubscriptionId}_{sub.CurrentPeriodStart:yyyyMMdd}";
                await GrantCreditsForProductAsync(sub.UserId, product, price, quantity: 1,
                    referenceId: renewalIdemKey, ct);
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("FulfillSubscriptionRenewalAsync: renewal processed for subscription {SubId}.", stripeSubscriptionId);
    }

    public async Task RevokeAccessAsync(string userId, string reason, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var activeSubs = await _db.CustomerSubscriptions
            .Where(s => s.UserId == userId && (s.Status == "active" || s.Status == "trialing" || s.Status == "paused"))
            .ToListAsync(ct);

        foreach (var sub in activeSubs)
        {
            sub.Status = "canceled";
            sub.CanceledAt = now;
            sub.UpdatedAt = now;
        }

        if (activeSubs.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("RevokeAccessAsync: revoked access for user {UserId} (reason: {Reason}). {Count} subscriptions canceled.",
            userId, reason, activeSubs.Count);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task UpsertCustomerSubscriptionAsync(
        string userId, string stripeSubId, BillingPrice price, Guid productId, CancellationToken ct)
    {
        var existing = await _db.CustomerSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubId, ct);

        var stripeSub = await _stripe.RetrieveSubscriptionAsync(stripeSubId, ct);
        var now = DateTimeOffset.UtcNow;

        if (existing is not null)
        {
            existing.Status = stripeSub.Status;
            existing.CurrentPeriodStart = new DateTimeOffset(stripeSub.CurrentPeriodStart, TimeSpan.Zero);
            existing.CurrentPeriodEnd = new DateTimeOffset(stripeSub.CurrentPeriodEnd, TimeSpan.Zero);
            existing.UpdatedAt = now;
        }
        else
        {
            _db.CustomerSubscriptions.Add(new CustomerSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                StripeSubscriptionId = stripeSubId,
                StripePriceId = price.StripePriceId ?? string.Empty,
                BillingProductId = productId,
                Status = stripeSub.Status,
                CurrentPeriodStart = new DateTimeOffset(stripeSub.CurrentPeriodStart, TimeSpan.Zero),
                CurrentPeriodEnd = new DateTimeOffset(stripeSub.CurrentPeriodEnd, TimeSpan.Zero),
                CancelAtPeriodEnd = stripeSub.CancelAtPeriodEnd,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    /// <summary>
    /// Grant wallet credits for a product purchase. Reads the product's MetadataJson for
    /// a "credits" field. If none found, logs a warning and skips the credit grant.
    /// </summary>
    private async Task GrantCreditsForProductAsync(
        string userId, BillingProduct product, BillingPrice price, int quantity,
        string referenceId, CancellationToken ct)
    {
        int creditsPerUnit = ResolveProductCredits(product);
        if (creditsPerUnit <= 0)
        {
            _logger.LogDebug(
                "GrantCreditsForProductAsync: product {Code} has no credit grant configured — skipping.",
                product.Code);
            return;
        }

        int totalCredits = creditsPerUnit * quantity;

        // Look up or create the wallet for this user
        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null)
        {
            _logger.LogWarning(
                "GrantCreditsForProductAsync: no wallet found for user {UserId} — cannot grant credits.", userId);
            return;
        }

        var idempotencyKey = $"checkout-credit:{userId}:{referenceId}:{product.Code}";
        await _walletService.CreditAsync(
            walletId: wallet.Id,
            amount: totalCredits,
            transactionType: "credit_purchase",
            referenceType: "payment",
            referenceId: referenceId,
            description: $"Credits for {product.Name} (x{quantity})",
            createdBy: "system",
            idempotencyKey: idempotencyKey,
            ct: ct);
    }

    private static int ResolveProductCredits(BillingProduct product)
    {
        if (string.IsNullOrWhiteSpace(product.MetadataJson))
            return 0;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(product.MetadataJson);
            if (doc.RootElement.TryGetProperty("credits", out var credits))
                return credits.TryGetInt32(out var v) ? v : 0;
        }
        catch
        {
            // Malformed JSON — treat as no credits
        }

        return 0;
    }
}
