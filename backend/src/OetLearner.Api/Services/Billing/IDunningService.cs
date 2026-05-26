namespace OetLearner.Api.Services.Billing;

public interface IDunningService
{
    Task OnInvoicePaymentFailedAsync(string stripeSubscriptionId, string userId, CancellationToken ct = default);
    Task OnInvoicePaymentSucceededAsync(string stripeSubscriptionId, string userId, CancellationToken ct = default);
    Task OnSubscriptionCanceledAsync(string stripeSubscriptionId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Wave A5 — accept a fresh <c>invoice.payment_failed</c> webhook payload
    /// and schedule the next-due smart-retry attempt (T+24h on first failure;
    /// the service computes the cadence from <see cref="OetLearner.Api.Domain.DunningAttempt"/>
    /// history). Idempotent on (invoiceId, attemptNumber).
    /// </summary>
    Task ScheduleInvoiceRetryAsync(string stripeSubscriptionId, string stripeInvoiceId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Wave A5 — execute the next pending <see cref="OetLearner.Api.Domain.DunningAttempt"/>
    /// row whose <c>ScheduledAt</c> is in the past. Calls Stripe
    /// <c>InvoiceService.PayAsync</c> and updates the row's outcome. On the
    /// 3rd consecutive failure cancels the subscription with
    /// <c>reason="dunning_exhausted"</c> and dispatches the subscription-lost
    /// notification.
    /// </summary>
    Task<DunningRetryExecutionResult> ExecutePendingRetryAsync(string attemptId, CancellationToken ct = default);
}

public sealed record DunningRetryExecutionResult(
    string AttemptId,
    int AttemptNumber,
    bool Succeeded,
    bool FinalAttemptExhausted,
    string? FailureCode,
    string? FailureReason);
