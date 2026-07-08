namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Shared "block before content is served" convention for every module's
/// attempt-start path: call the relevant AiPackageCreditService debit method
/// first, then EnsureDebited() before creating any attempt/session row.
/// </summary>
public static class CreditGateExtensions
{
    public static void EnsureDebited(this AiPackageDebitResult result)
    {
        if (result.Debited) return;
        throw ApiException.PaymentRequired(
            result.ErrorCode ?? "no_credits",
            result.ErrorMessage ?? "You have no credits remaining. Purchase a package to continue.");
    }
}
