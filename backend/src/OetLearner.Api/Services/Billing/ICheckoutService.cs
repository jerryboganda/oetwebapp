namespace OetLearner.Api.Services.Billing;

public interface ICheckoutService
{
    /// <summary>Create a Stripe Checkout Session from the user's cart. Idempotent per cart+user.</summary>
    Task<CheckoutSessionDto> CreateCheckoutSessionAsync(string userId, string email, string cartId, string? successUrl = null, string? cancelUrl = null, CancellationToken ct = default);

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

public sealed record CheckoutSessionStatusDto(
    string SessionId,
    Guid LocalSessionId,
    string? StripeSessionId,
    string Status,
    DateTimeOffset? FulfilledAt,
    decimal TotalAmount,
    string Currency,
    IReadOnlyList<CheckoutSessionStatusItemDto> Items,
    string? FailureReason
);

public sealed record CheckoutSessionStatusItemDto(
    string ProductCode,
    string ProductName,
    int Quantity,
    string? Description
);
