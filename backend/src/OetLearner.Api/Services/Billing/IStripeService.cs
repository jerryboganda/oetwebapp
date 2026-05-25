using Stripe;
using Stripe.Checkout;

namespace OetLearner.Api.Services.Billing;

public interface IStripeService
{
    /// <summary>Resolve or create a Stripe Customer for the given user. Stores StripeCustomerId on user row.</summary>
    Task<string> EnsureCustomerAsync(string userId, string email, CancellationToken ct = default);

    /// <summary>Create a Stripe Checkout Session and return (sessionId, url).</summary>
    Task<(string SessionId, string Url)> CreateCheckoutSessionAsync(CreateCheckoutSessionRequest request, CancellationToken ct = default);

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

    /// <summary>Pause a subscription by setting pause_collection.</summary>
    Task PauseSubscriptionAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>Resume a paused subscription by clearing pause_collection.</summary>
    Task ResumeSubscriptionAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>List invoices for a Stripe Customer.</summary>
    Task<IEnumerable<Invoice>> ListInvoicesAsync(string stripeCustomerId, int limit = 24, CancellationToken ct = default);

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
    string StripePriceId,
    int Quantity = 1
);

public sealed record CreateStripeCouponRequest(
    string? Name,
    decimal? PercentOff,
    long? AmountOff,
    string? Currency,
    string? Duration,  // "once" | "repeating" | "forever"
    int? DurationInMonths
);
