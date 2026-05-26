namespace OetLearner.Api.Services.Billing;

public interface IFulfillmentService
{
    /// <summary>
    /// Fulfill a completed Stripe Checkout Session. Idempotent on
    /// <paramref name="stripeSessionId"/>. Grants wallet credits, mocks,
    /// class credits, tbook entitlement, tutor minutes per the product metadata,
    /// upserts <c>CustomerSubscription</c> rows for subscription line items,
    /// writes a <c>BillingEvent</c> audit row, and dispatches a confirmation
    /// notification.
    /// </summary>
    Task FulfillAsync(string stripeSessionId, CancellationToken ct = default);

    /// <summary>
    /// Refresh monthly entitlements when an <c>invoice.paid</c> arrives for a
    /// recurring subscription. Idempotent on
    /// <c>(stripeSubscriptionId, currentPeriodStart)</c>. Extends
    /// <c>CustomerSubscription.CurrentPeriodEnd</c> and grants the credits
    /// declared in the product metadata for the new period.
    /// </summary>
    Task FulfillRenewalAsync(string stripeInvoiceId, CancellationToken ct = default);

    /// <summary>Legacy alias kept for inline webhook callers.</summary>
    Task FulfillCheckoutAsync(string stripeSessionId, CancellationToken ct = default);

    /// <summary>Legacy alias for the renewal pipeline (subscription-id keyed).</summary>
    Task FulfillSubscriptionRenewalAsync(string stripeSubscriptionId, CancellationToken ct = default);

    /// <summary>Revoke premium access (chargeback or cancellation).</summary>
    Task RevokeAccessAsync(string userId, string reason, CancellationToken ct = default);
}
