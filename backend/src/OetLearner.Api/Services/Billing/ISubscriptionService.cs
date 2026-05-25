namespace OetLearner.Api.Services.Billing;

public interface ISubscriptionService
{
    Task<CustomerSubscriptionDto?> GetActiveSubscriptionAsync(string userId, CancellationToken ct = default);
    Task<CustomerSubscriptionDto> CreateFromStripeAsync(string userId, string stripeSubscriptionId, CancellationToken ct = default);
    Task SyncFromStripeAsync(string stripeSubscriptionId, CancellationToken ct = default);
    Task CancelAsync(string userId, bool cancelAtPeriodEnd = true, CancellationToken ct = default);
    Task ChangePlanAsync(string userId, string newStripePriceId, CancellationToken ct = default);
    Task PauseAsync(string userId, CancellationToken ct = default);
    Task ResumeAsync(string userId, CancellationToken ct = default);
    Task<IEnumerable<SubscriptionInvoiceDto>> ListInvoicesAsync(string userId, CancellationToken ct = default);
    Task<string> CreatePortalSessionAsync(string userId, string returnUrl, CancellationToken ct = default);
}

public sealed record CustomerSubscriptionDto(
    Guid Id,
    string UserId,
    string StripeSubscriptionId,
    string StripePriceId,
    string Status,
    DateTimeOffset CurrentPeriodStart,
    DateTimeOffset CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CanceledAt,
    DateTimeOffset? PausedAt
);

public sealed record SubscriptionInvoiceDto(
    string Id,
    string Status,
    long AmountDue,
    string Currency,
    DateTimeOffset Created,
    string? InvoicePdfUrl,
    string? HostedInvoiceUrl
);
