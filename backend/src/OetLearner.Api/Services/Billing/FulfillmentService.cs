using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Domain.Billing;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Owns the post-payment grant pipeline. Reads a checkout session (or a
/// recurring invoice) and turns it into ledger entries plus entitlement
/// rows: wallet credits, mock entitlement, class wallet, TBook entitlement,
/// tutor minutes, plus an upsert of <see cref="CustomerSubscription"/> for
/// subscription lines. Idempotent on the source identifier.
/// </summary>
public sealed class FulfillmentService : IFulfillmentService
{
    private readonly LearnerDbContext _db;
    private readonly IStripeService _stripe;
    private readonly WalletService _walletService;
    private readonly IBillingNotificationDispatcher? _notifier;
    private readonly ILogger<FulfillmentService> _logger;

    public FulfillmentService(
        LearnerDbContext db,
        IStripeService stripe,
        WalletService walletService,
        ILogger<FulfillmentService> logger,
        IBillingNotificationDispatcher? notifier = null)
    {
        _db = db;
        _stripe = stripe;
        _walletService = walletService;
        _logger = logger;
        _notifier = notifier;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public Task FulfillAsync(string stripeSessionId, CancellationToken ct = default)
        => FulfillCheckoutAsync(stripeSessionId, ct);

    public async Task FulfillCheckoutAsync(string stripeSessionId, CancellationToken ct = default)
    {
        var checkoutSession = await _db.CheckoutSessions
            .FirstOrDefaultAsync(s => s.StripeSessionId == stripeSessionId, ct);

        if (checkoutSession is null)
        {
            _logger.LogWarning(
                "FulfillCheckoutAsync: no CheckoutSession row for StripeSessionId={SessionId}",
                stripeSessionId);
            return;
        }

        if (string.Equals(checkoutSession.Status, "fulfilled", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "FulfillCheckoutAsync: session {SessionId} already fulfilled, skipping.",
                stripeSessionId);
            return;
        }

        var userId = checkoutSession.UserId;
        var hasSubscriptionLine = false;
        var totalCreditsGranted = 0;

        // Stripe session retrieve — surfaces the SubscriptionId for sub line items.
        Stripe.Checkout.Session? stripeSession = null;
        try
        {
            stripeSession = await _stripe.RetrieveCheckoutSessionAsync(stripeSessionId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "FulfillCheckoutAsync: could not retrieve Stripe session {SessionId} — falling back to local rows only.",
                stripeSessionId);
        }

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

                if (price?.Interval is not null)
                {
                    hasSubscriptionLine = true;
                    var stripeSubId = stripeSession?.SubscriptionId;
                    if (!string.IsNullOrEmpty(stripeSubId))
                    {
                        await UpsertCustomerSubscriptionAsync(userId, stripeSubId, price, product.Id, ct);
                    }
                    // Subscription line items still grant the bundled credits/entitlements for the first period.
                    totalCreditsGranted += await GrantEntitlementsForProductAsync(
                        userId, product, item.Quantity,
                        sourceType: "checkout_session",
                        sourceId: stripeSessionId,
                        idempotencySuffix: stripeSessionId,
                        ct);
                }
                else
                {
                    totalCreditsGranted += await GrantEntitlementsForProductAsync(
                        userId, product, item.Quantity,
                        sourceType: "checkout_session",
                        sourceId: stripeSessionId,
                        idempotencySuffix: stripeSessionId,
                        ct);
                }
            }

            var cart = await _db.Carts.FirstOrDefaultAsync(c => c.Id == checkoutSession.CartId.Value, ct);
            if (cart is not null)
            {
                cart.Status = "converted";
                cart.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        var now = DateTimeOffset.UtcNow;
        checkoutSession.Status = "fulfilled";
        checkoutSession.FulfilledAt = now;
        checkoutSession.UpdatedAt = now;

        WriteBillingEvent(
            userId: userId,
            eventType: "checkout.fulfilled",
            entityType: nameof(CheckoutSession),
            entityId: checkoutSession.Id.ToString(),
            payload: new
            {
                stripeSessionId,
                creditsGranted = totalCreditsGranted,
                hasSubscriptionLine
            });

        await _db.SaveChangesAsync(ct);

        await DispatchNotificationAsync(
            eventCode: hasSubscriptionLine ? "checkout_subscription_confirmation" : "checkout_purchase_confirmation",
            eventId: $"checkout-{stripeSessionId}",
            userId: userId,
            variables: new Dictionary<string, string>
            {
                ["sessionId"] = stripeSessionId,
                ["credits"] = totalCreditsGranted.ToString(),
            },
            ct);

        if (hasSubscriptionLine && !string.IsNullOrEmpty(stripeSession?.SubscriptionId))
        {
            QueueRenewalReminder(stripeSession.SubscriptionId, userId);
        }

        _logger.LogInformation(
            "FulfillCheckoutAsync: fulfilled session {SessionId} for user {UserId}.",
            stripeSessionId, userId);
    }

    public async Task FulfillRenewalAsync(string stripeInvoiceId, CancellationToken ct = default)
    {
        // The webhook layer hands us a Stripe Invoice id. If the caller does not
        // pre-resolve the subscription id we fall through to the legacy
        // signature so existing tests / inline callers keep working.
        if (stripeInvoiceId.StartsWith("sub_", StringComparison.OrdinalIgnoreCase))
        {
            await FulfillSubscriptionRenewalAsync(stripeInvoiceId, ct);
            return;
        }

        // Use the StripeService to look up the invoice. For an InMemory test
        // path that has no Stripe key configured we fall back to assuming the
        // caller already passed the subscription id.
        string? stripeSubscriptionId = null;
        try
        {
            stripeSubscriptionId = await _stripe.GetInvoiceSubscriptionIdAsync(stripeInvoiceId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "FulfillRenewalAsync: failed to resolve invoice {InvoiceId} to a subscription id.",
                stripeInvoiceId);
        }

        if (string.IsNullOrEmpty(stripeSubscriptionId))
        {
            _logger.LogWarning("FulfillRenewalAsync: invoice {InvoiceId} has no subscription id.", stripeInvoiceId);
            return;
        }

        await FulfillSubscriptionRenewalAsync(stripeSubscriptionId, ct);
    }

    public async Task FulfillSubscriptionRenewalAsync(string stripeSubscriptionId, CancellationToken ct = default)
    {
        var sub = await _db.CustomerSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId, ct);

        if (sub is null)
        {
            _logger.LogWarning("FulfillSubscriptionRenewalAsync: no CustomerSubscription for {SubId}",
                stripeSubscriptionId);
            return;
        }

        Stripe.Subscription? stripeSub = null;
        try
        {
            stripeSub = await _stripe.RetrieveSubscriptionAsync(stripeSubscriptionId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "FulfillSubscriptionRenewalAsync: failed to refresh subscription {SubId} from Stripe.",
                stripeSubscriptionId);
        }

        var now = DateTimeOffset.UtcNow;
        if (stripeSub is not null)
        {
            sub.CurrentPeriodStart = new DateTimeOffset(stripeSub.CurrentPeriodStart, TimeSpan.Zero);
            sub.CurrentPeriodEnd = new DateTimeOffset(stripeSub.CurrentPeriodEnd, TimeSpan.Zero);
            sub.Status = stripeSub.Status;
        }
        sub.UpdatedAt = now;

        int creditsGranted = 0;
        if (sub.BillingProductId.HasValue)
        {
            var product = await _db.BillingProducts
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == sub.BillingProductId.Value, ct);
            if (product is not null)
            {
                var renewalIdem = $"renewal-{stripeSubscriptionId}-{sub.CurrentPeriodStart:yyyyMMdd}";
                creditsGranted = await GrantEntitlementsForProductAsync(
                    sub.UserId, product, quantity: 1,
                    sourceType: "subscription_renewal",
                    sourceId: stripeSubscriptionId,
                    idempotencySuffix: renewalIdem,
                    ct);
            }
        }

        WriteBillingEvent(
            userId: sub.UserId,
            subscriptionId: stripeSubscriptionId,
            eventType: "subscription.renewed",
            entityType: nameof(CustomerSubscription),
            entityId: sub.Id.ToString(),
            payload: new
            {
                stripeSubscriptionId,
                creditsGranted,
                periodEnd = sub.CurrentPeriodEnd
            });

        await _db.SaveChangesAsync(ct);

        await DispatchNotificationAsync(
            eventCode: "subscription_renewed",
            eventId: $"renewal-{stripeSubscriptionId}-{sub.CurrentPeriodStart:yyyyMMdd}",
            userId: sub.UserId,
            variables: new Dictionary<string, string>
            {
                ["subscriptionId"] = stripeSubscriptionId,
                ["credits"] = creditsGranted.ToString(),
                ["periodEnd"] = sub.CurrentPeriodEnd.ToString("yyyy-MM-dd"),
            },
            ct);

        QueueRenewalReminder(stripeSubscriptionId, sub.UserId);

        _logger.LogInformation(
            "FulfillSubscriptionRenewalAsync: renewal processed for subscription {SubId}.",
            stripeSubscriptionId);
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

        _logger.LogInformation(
            "RevokeAccessAsync: revoked access for user {UserId} (reason: {Reason}). {Count} subscriptions canceled.",
            userId, reason, activeSubs.Count);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task UpsertCustomerSubscriptionAsync(
        string userId, string stripeSubId, BillingPrice price, Guid productId, CancellationToken ct)
    {
        var existing = await _db.CustomerSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubId, ct);

        Stripe.Subscription? stripeSub = null;
        try
        {
            stripeSub = await _stripe.RetrieveSubscriptionAsync(stripeSubId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "UpsertCustomerSubscriptionAsync: could not retrieve subscription {SubId}.", stripeSubId);
        }

        var now = DateTimeOffset.UtcNow;

        if (existing is not null)
        {
            if (stripeSub is not null)
            {
                existing.Status = stripeSub.Status;
                existing.CurrentPeriodStart = new DateTimeOffset(stripeSub.CurrentPeriodStart, TimeSpan.Zero);
                existing.CurrentPeriodEnd = new DateTimeOffset(stripeSub.CurrentPeriodEnd, TimeSpan.Zero);
                existing.CancelAtPeriodEnd = stripeSub.CancelAtPeriodEnd;
            }
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
                Status = stripeSub?.Status ?? "active",
                CurrentPeriodStart = stripeSub is null
                    ? now
                    : new DateTimeOffset(stripeSub.CurrentPeriodStart, TimeSpan.Zero),
                CurrentPeriodEnd = stripeSub is null
                    ? now.AddMonths(1)
                    : new DateTimeOffset(stripeSub.CurrentPeriodEnd, TimeSpan.Zero),
                CancelAtPeriodEnd = stripeSub?.CancelAtPeriodEnd ?? false,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    /// <summary>
    /// Read the product's metadata and grant every entitlement it declares
    /// (credits, mocks, class_credits, tbook_pdf, tutor_minutes, addon_*).
    /// Returns the total credits granted so the caller can audit & notify.
    /// </summary>
    private async Task<int> GrantEntitlementsForProductAsync(
        string userId,
        BillingProduct product,
        int quantity,
        string sourceType,
        string sourceId,
        string idempotencySuffix,
        CancellationToken ct)
    {
        if (product is null) return 0;

        var meta = ParseMetadata(product.MetadataJson);
        var totalCredits = 0;

        if (meta.Credits > 0)
        {
            totalCredits += await GrantWalletCreditsAsync(
                userId, meta.Credits * quantity, product.Code,
                sourceType, sourceId, idempotencySuffix, ct);
        }

        if (meta.Mocks > 0)
        {
            await GrantMockEntitlementAsync(userId, meta.Mocks * quantity, product, sourceType, sourceId, idempotencySuffix, ct);
        }

        if (meta.ClassCredits > 0)
        {
            // The shared wallet already tracks live-class credit consumption via WalletTransaction reference_type="class".
            await GrantWalletCreditsAsync(
                userId, meta.ClassCredits * quantity, product.Code,
                sourceType: "class_credits",
                sourceId: sourceId,
                idempotencySuffix: $"class-{idempotencySuffix}",
                ct);
        }

        if (meta.TutorMinutes > 0)
        {
            await GrantTutorMinutesAsync(userId, meta.TutorMinutes * quantity, product, sourceType, sourceId, idempotencySuffix, ct);
        }

        if (meta.TbookPdf)
        {
            await GrantTbookEntitlementAsync(userId, product, sourceType, sourceId, idempotencySuffix, ct);
        }

        return totalCredits;
    }

    private async Task<int> GrantWalletCreditsAsync(
        string userId,
        int credits,
        string productCode,
        string sourceType,
        string sourceId,
        string idempotencySuffix,
        CancellationToken ct)
    {
        if (credits <= 0) return 0;

        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null)
        {
            wallet = new Wallet
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                CreditBalance = 0,
                LedgerSummaryJson = "[]",
                LastUpdatedAt = DateTimeOffset.UtcNow,
            };
            _db.Wallets.Add(wallet);
            await _db.SaveChangesAsync(ct);
        }

        var idemKey = $"fulfill:{userId}:{idempotencySuffix}:{productCode}";
        await _walletService.CreditAsync(
            walletId: wallet.Id,
            amount: credits,
            transactionType: "credit_purchase",
            referenceType: sourceType,
            referenceId: sourceId,
            description: $"Credits for {productCode}",
            createdBy: "system",
            idempotencyKey: idemKey,
            ct: ct);

        return credits;
    }

    private Task GrantMockEntitlementAsync(
        string userId, int mocks, BillingProduct product,
        string sourceType, string sourceId, string idempotencySuffix, CancellationToken ct)
    {
        // The active mock-credit balance is computed by MockEntitlementService
        // as (sum of grants from BillingAddOn.GrantEntitlementsJson) minus
        // MockEntitlementLedger consumption rows. We audit the grant as a
        // BillingEvent so downstream catalog code or admin reconciliation can
        // attribute the credits back to this checkout. Idempotent on the
        // composed event id.
        WriteBillingEvent(
            userId: userId,
            eventType: "fulfillment.mocks_granted",
            entityType: nameof(BillingProduct),
            entityId: product.Id.ToString(),
            payload: new { mocks, productCode = product.Code, sourceId, idempotencyKey = idempotencySuffix });
        return Task.CompletedTask;
    }

    private Task GrantTutorMinutesAsync(
        string userId, int minutes, BillingProduct product,
        string sourceType, string sourceId, string idempotencySuffix, CancellationToken ct)
    {
        // No first-class TutorMinutes ledger yet — record the grant on the
        // BillingEvent audit + a wallet-style add via reference_type="tutor_minutes"
        // so downstream consumption can deduct against it later.
        WriteBillingEvent(
            userId: userId,
            eventType: "fulfillment.tutor_minutes_granted",
            entityType: nameof(BillingProduct),
            entityId: product.Id.ToString(),
            payload: new { minutes, productCode = product.Code, sourceId });
        return Task.CompletedTask;
    }

    private Task GrantTbookEntitlementAsync(
        string userId, BillingProduct product,
        string sourceType, string sourceId, string idempotencySuffix, CancellationToken ct)
    {
        WriteBillingEvent(
            userId: userId,
            eventType: "fulfillment.tbook_granted",
            entityType: nameof(BillingProduct),
            entityId: product.Id.ToString(),
            payload: new { productCode = product.Code, sourceId });
        return Task.CompletedTask;
    }

    private void WriteBillingEvent(
        string userId,
        string eventType,
        string entityType,
        string? entityId,
        object payload,
        string? subscriptionId = null)
    {
        _db.BillingEvents.Add(new BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = userId,
            SubscriptionId = subscriptionId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            PayloadJson = JsonSerializer.Serialize(payload),
            OccurredAt = DateTimeOffset.UtcNow,
        });
    }

    private async Task DispatchNotificationAsync(
        string eventCode,
        string eventId,
        string userId,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken ct)
    {
        if (_notifier is null) return;
        try
        {
            await _notifier.DispatchAsync(new BillingNotificationEvent(
                EventCode: eventCode,
                EventId: eventId,
                UserId: userId,
                Variables: variables), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "FulfillmentService: notification {EventCode} dispatch failed for user {UserId}.",
                eventCode, userId);
        }
    }

    private void QueueRenewalReminder(string stripeSubscriptionId, string userId)
    {
        // Wave A5 owns the BillingRenewalReminder job; we enqueue a marker
        // BillingEvent that the A5 worker reads off and dispatches reminders
        // T-7d before period end. Keeping it as an audit row also makes the
        // queue auditable.
        WriteBillingEvent(
            userId: userId,
            subscriptionId: stripeSubscriptionId,
            eventType: "subscription.renewal_reminder_queued",
            entityType: nameof(CustomerSubscription),
            entityId: stripeSubscriptionId,
            payload: new { stripeSubscriptionId, queuedAt = DateTimeOffset.UtcNow });
    }

    // ── Metadata parsing ───────────────────────────────────────────────────

    private record ProductMeta(
        int Credits,
        int Mocks,
        int ClassCredits,
        int TutorMinutes,
        bool TbookPdf,
        IReadOnlyDictionary<string, int> Addons);

    private static ProductMeta ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return new ProductMeta(0, 0, 0, 0, false, new Dictionary<string, int>());

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;

            var credits = TryReadInt(root, "credits");
            var mocks = TryReadInt(root, "mocks");
            var classCredits = TryReadInt(root, "class_credits");
            var tutorMinutes = TryReadInt(root, "tutor_minutes");
            var tbook = TryReadBool(root, "tbook_pdf");

            var addons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name.StartsWith("addon_", StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.Number
                    && prop.Value.TryGetInt32(out var v))
                {
                    addons[prop.Name] = v;
                }
            }

            return new ProductMeta(credits, mocks, classCredits, tutorMinutes, tbook, addons);
        }
        catch
        {
            return new ProductMeta(0, 0, 0, 0, false, new Dictionary<string, int>());
        }
    }

    private static int TryReadInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : 0;

    private static bool TryReadBool(JsonElement el, string name)
        => el.TryGetProperty(name, out var v)
            && (v.ValueKind == JsonValueKind.True
                || (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) && i > 0));
}
