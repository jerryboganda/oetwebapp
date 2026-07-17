namespace OetWithDrHesham.Api.Services.Billing;

public interface ISubscriptionService
{
    Task<CustomerSubscriptionDto?> GetActiveSubscriptionAsync(string userId, CancellationToken ct = default);
    Task<CustomerSubscriptionDto> CreateFromStripeAsync(string userId, string stripeSubscriptionId, CancellationToken ct = default);
    Task SyncFromStripeAsync(string stripeSubscriptionId, CancellationToken ct = default);
    Task CancelAsync(string userId, bool cancelAtPeriodEnd = true, CancellationToken ct = default);
    /// <summary>
    /// Wave A5 — cancel with an audit-grade reason recorded against the customer
    /// subscription / billing event log. Used by dunning to record
    /// <c>dunning_exhausted</c> when the smart-retry cascade gives up.
    /// </summary>
    Task CancelAsync(string userId, bool cancelAtPeriodEnd, string reason, CancellationToken ct = default);
    Task ChangePlanAsync(string userId, string newStripePriceId, CancellationToken ct = default);

    /// <summary>Wave A4 — change plan with explicit proration control. Records a BillingEvent audit row.</summary>
    Task ChangePlanAsync(string userId, string newStripePriceId, bool prorate, CancellationToken ct = default);

    Task PauseAsync(string userId, CancellationToken ct = default);

    /// <summary>Wave A4 — pause with an explicit resume-at. Capped at 90 days per OET policy.</summary>
    Task PauseAsync(string userId, DateTimeOffset? pauseUntil, CancellationToken ct = default);

    Task ResumeAsync(string userId, CancellationToken ct = default);

    /// <summary>Wave A4 — attach (or clear, when <paramref name="couponId"/> is null) a coupon on the active subscription.</summary>
    Task ApplyDiscountAsync(string userId, string? couponId, CancellationToken ct = default);

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
