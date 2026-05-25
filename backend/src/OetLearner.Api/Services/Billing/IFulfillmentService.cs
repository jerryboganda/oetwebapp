namespace OetLearner.Api.Services.Billing;

public interface IFulfillmentService
{
    /// <summary>Fulfill a completed Stripe Checkout Session. Idempotent.</summary>
    Task FulfillCheckoutAsync(string stripeSessionId, CancellationToken ct = default);

    /// <summary>Grant fresh credits on subscription renewal (invoice.paid event).</summary>
    Task FulfillSubscriptionRenewalAsync(string stripeSubscriptionId, CancellationToken ct = default);

    /// <summary>Revoke premium access (chargeback or cancellation).</summary>
    Task RevokeAccessAsync(string userId, string reason, CancellationToken ct = default);
}
