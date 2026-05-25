namespace OetLearner.Api.Services.Billing;

public interface ICheckoutService
{
    /// <summary>Create a Stripe Checkout Session from the user's cart. Idempotent per cart+user.</summary>
    Task<CheckoutSessionDto> CreateCheckoutSessionAsync(string userId, string email, string cartId, CancellationToken ct = default);

    /// <summary>Get status of a checkout session (pending | fulfilled | failed | expired).</summary>
    Task<CheckoutSessionStatusDto?> GetSessionStatusAsync(Guid sessionId, CancellationToken ct = default);

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
    Guid Id,
    string Status,
    DateTimeOffset? FulfilledAt
);
