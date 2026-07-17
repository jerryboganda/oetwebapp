namespace OetWithDrHesham.Api.Services.Billing;

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

    /// <summary>
    /// Deterministic per-(learner, subtest, paper) reference for Reading /
    /// Listening objective-practice debits. The paper (sample) is the billing
    /// unit: the first attempt on any part or the full paper consumes one test
    /// credit, and every other part / re-attempt of that same paper dedupes to
    /// this reference and is free. Encodes the userId because
    /// <see cref="IAiPackageCreditService.DeductObjectivePracticeAsync"/>
    /// checks reference idempotency globally (not per-user).
    /// </summary>
    public static string ObjectivePaperReference(string subtest, string userId, string paperId)
        => $"objective:{subtest.Trim().ToLowerInvariant()}:{userId}:{paperId}";
}
