namespace OetLearner.Api.Services.Billing.Gateways;

/// <summary>
/// Trusted server-to-server confirmation of a payment, obtained by querying the
/// provider's own API rather than trusting a callback body or a browser redirect.
/// A gateway lookup that returns <c>null</c> means the payment state is
/// <em>unknown</em> and must never be treated as unpaid. <see cref="Paid"/> is
/// only true when the provider explicitly reports a settled payment.
/// </summary>
public sealed record PaymentConfirmation(
    bool Paid,
    decimal Amount,
    string Currency,
    string? ProviderTransactionId,
    string? RawStatus);
