using Stripe;
using Stripe.Checkout;

namespace OetWithDrHesham.Api.Services.Billing;

public interface IStripeService
{
    /// <summary>Resolve or create a Stripe Customer for the given user. Stores StripeCustomerId on user row.</summary>
    Task<string> EnsureCustomerAsync(string userId, string email, CancellationToken ct = default);

    /// <summary>Create a Stripe Checkout Session and return (sessionId, url).</summary>
    Task<(string SessionId, string Url)> CreateCheckoutSessionAsync(CreateCheckoutSessionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Create a one-off (ad-hoc) variable-amount Stripe Checkout Session in "payment"
    /// mode using inline price_data, and return (sessionId, url). Unlike
    /// <see cref="CreateCheckoutSessionAsync"/> this does not require a pre-made
    /// Stripe Price; the amount is supplied directly. Used for same-day reschedule
    /// penalties where the charge is computed at runtime.
    /// </summary>
    Task<(string SessionId, string Url)> CreateAdHocPaymentCheckoutSessionAsync(
        string stripeCustomerId, string userId, string userEmail,
        string currency, long amountMinorUnits, string productName,
        string successUrl, string cancelUrl, string? idempotencyKey,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default);

    /// <summary>Retrieve a Stripe Checkout Session by ID.</summary>
    Task<Session> RetrieveCheckoutSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Create a Stripe Customer Portal session for subscription self-service.</summary>
    Task<string> CreatePortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken ct = default);

    /// <summary>Create a refund for a PaymentIntent.</summary>
    Task<string> CreateRefundAsync(string paymentIntentId, long? amountCents, string? reason, CancellationToken ct = default);

    /// <summary>Construct and verify a Stripe webhook event from the raw body and signature header.</summary>
    Event ConstructWebhookEvent(string requestBody, string signatureHeader, string webhookSecret);

    /// <summary>Retrieve a Stripe Subscription.</summary>
    Task<Subscription> RetrieveSubscriptionAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>Cancel a subscription at period end (or immediately if cancelAtPeriodEnd=false).</summary>
    Task CancelSubscriptionAsync(string subscriptionId, bool cancelAtPeriodEnd = true, CancellationToken ct = default);

    /// <summary>Update a subscription to a new price (plan change).</summary>
    Task UpdateSubscriptionAsync(string subscriptionId, string newPriceId, CancellationToken ct = default);

    /// <summary>Update a subscription to a new price, with explicit proration control.</summary>
    Task UpdateSubscriptionAsync(string subscriptionId, string newPriceId, bool prorate, CancellationToken ct = default);

    /// <summary>Pause a subscription by setting pause_collection.</summary>
    Task PauseSubscriptionAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>Pause a subscription with an explicit resume timestamp.</summary>
    Task PauseSubscriptionAsync(string subscriptionId, DateTimeOffset? resumeAt, CancellationToken ct = default);

    /// <summary>Resume a paused subscription by clearing pause_collection.</summary>
    Task ResumeSubscriptionAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>Apply (or clear, when <paramref name="couponId"/> is null) a coupon on an existing subscription.</summary>
    Task ApplyCouponToSubscriptionAsync(string subscriptionId, string? couponId, CancellationToken ct = default);

    /// <summary>List invoices for a Stripe Customer.</summary>
    Task<IEnumerable<Invoice>> ListInvoicesAsync(string stripeCustomerId, int limit = 24, CancellationToken ct = default);

    /// <summary>Retrieve a single invoice and surface its subscription id (if any).</summary>
    Task<string?> GetInvoiceSubscriptionIdAsync(string invoiceId, CancellationToken ct = default);

    /// <summary>
    /// Wave A5 — retry collection on an invoice via Stripe's
    /// <c>InvoiceService.PayAsync</c>. Used by the smart-retry dunning ladder
    /// (T+24h / T+72h / T+168h). The returned tuple reports the Stripe
    /// outcome status and any payment-failure code; the caller is expected
    /// to roll over to the next attempt when <c>Succeeded</c> is false.
    /// Throws when Stripe is not configured (no SecretKey) in production.
    /// </summary>
    Task<PayInvoiceResult> PayInvoiceAsync(string stripeInvoiceId, CancellationToken ct = default);

    /// <summary>Create a Stripe Coupon.</summary>
    Task<string> CreateCouponAsync(CreateStripeCouponRequest request, CancellationToken ct = default);

    /// <summary>Create a Stripe Promotion Code for an existing coupon.</summary>
    Task<string> CreatePromotionCodeAsync(string couponId, string code, CancellationToken ct = default);
}

public sealed record CreateCheckoutSessionRequest(
    string StripeCustomerId,
    string UserId,
    string UserEmail,
    IReadOnlyList<CheckoutLineItem> LineItems,
    string Mode,   // "payment" or "subscription"
    string SuccessUrl,
    string CancelUrl,
    string? IdempotencyKey = null,
    string? Currency = "aud",
    bool AutomaticTax = true,
    string? PromotionCodeId = null
);

public sealed record CheckoutLineItem(
    string? StripePriceId,
    int Quantity = 1,
    long? UnitAmount = null,
    string? Currency = null,
    string? ProductName = null,
    string? Interval = null,
    long? IntervalCount = null
);

public sealed record CreateStripeCouponRequest(
    string? Name,
    decimal? PercentOff,
    long? AmountOff,
    string? Currency,
    string? Duration,  // "once" | "repeating" | "forever"
    int? DurationInMonths
);

/// <summary>
/// Outcome of <see cref="IStripeService.PayInvoiceAsync"/>. Stripe returns 402
/// when the card declines; the dunning service treats <c>Succeeded == false</c>
/// as the trigger to schedule the next retry attempt.
/// </summary>
public sealed record PayInvoiceResult(
    bool Succeeded,
    string Status,
    string? FailureCode,
    string? FailureReason);
