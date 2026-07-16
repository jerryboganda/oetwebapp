namespace OetLearner.Api.Services.Billing;

public interface ICheckoutService
{
    /// <summary>Create a Stripe Checkout Session from the user's cart. Idempotent per cart+user.</summary>
    Task<CheckoutSessionDto> CreateCheckoutSessionAsync(string userId, string email, string cartId, string? successUrl = null, string? cancelUrl = null, CancellationToken ct = default);

    /// <summary>
    /// Create a PayPal (embedded) order from the user's cart and persist a paypal-gateway
    /// CheckoutSession the browser SDK approves and the capture/webhook map back to. Rejects
    /// carts containing recurring items (PayPal one-time capture cannot open a subscription).
    /// Idempotent per cart+user.
    /// </summary>
    Task<PayPalCartOrderDto> CreatePayPalCartOrderAsync(string userId, string email, string cartId, CancellationToken ct = default);

    /// <summary>Get status of a checkout session (pending | fulfilled | failed | expired). Scoped to the owning user.</summary>
    Task<CheckoutSessionStatusDto?> GetSessionStatusAsync(string userId, string sessionId, CancellationToken ct = default);

    /// <summary>Mark a session as expired.</summary>
    Task ExpireSessionAsync(Guid sessionId, CancellationToken ct = default);
}

public sealed record CheckoutSessionDto(
    Guid Id,
    string StripeSessionId,
    string Url,
    string Status,
    decimal TotalAmount,
    string Currency
);

public sealed record PayPalCartOrderDto(
    string OrderId,
    Guid CheckoutSessionId,
    decimal TotalAmount,
    string Currency
);

/// <param name="DeliveryMethod">
/// <see cref="OetLearner.Api.Domain.DeliveryMethods"/> — how the purchased package is handed
/// over. A session whose status is <c>fulfilled</c> means the PAYMENT completed; for anything
/// other than <c>automatic_web</c> it does NOT mean access is live, because the subscription
/// stays Pending until an admin marks it fulfilled (spec 2026-07-15 §2/§6.6). The success page
/// must branch on this before claiming entitlements were added.
/// <para><b>Null means UNKNOWN, not automatic.</b> Delivery is a <c>BillingPlan</c> property and
/// a cart order references no plan, so this endpoint cannot resolve it — see
/// <c>CheckoutService.ResolveDelivery</c> for why. Callers must render a neutral
/// payment-cleared state for null rather than claiming entitlements were added.</para>
/// </param>
/// <param name="FulfilmentStatus">
/// <see cref="OetLearner.Api.Domain.FulfilmentStatuses"/> for the subscription this order opened,
/// or null when the order granted no course subscription (credit/add-on carts) and on every
/// cart order, which never opens a domain <c>Subscription</c> at all.
/// </param>
public sealed record CheckoutSessionStatusDto(
    string SessionId,
    Guid LocalSessionId,
    string? StripeSessionId,
    string Status,
    DateTimeOffset? FulfilledAt,
    decimal TotalAmount,
    string Currency,
    IReadOnlyList<CheckoutSessionStatusItemDto> Items,
    string? FailureReason,
    string? DeliveryMethod,
    string? FulfilmentStatus
);

public sealed record CheckoutSessionStatusItemDto(
    string ProductCode,
    string ProductName,
    int Quantity,
    string? Description
);
