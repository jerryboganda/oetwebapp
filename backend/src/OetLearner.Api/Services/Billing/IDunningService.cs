namespace OetLearner.Api.Services.Billing;

public interface IDunningService
{
    Task OnInvoicePaymentFailedAsync(string stripeSubscriptionId, string userId, CancellationToken ct = default);
    Task OnInvoicePaymentSucceededAsync(string stripeSubscriptionId, string userId, CancellationToken ct = default);
    Task OnSubscriptionCanceledAsync(string stripeSubscriptionId, string userId, CancellationToken ct = default);
}
