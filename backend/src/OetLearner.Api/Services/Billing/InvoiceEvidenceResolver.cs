using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Single source of truth for how a subscription's "Paid" invoice evidence is
/// resolved. Every invoice-creation call site (lazy backfill, checkout/wallet
/// webhook fulfillment, legacy reconciliation) must go through
/// <see cref="ResolveAsync"/> instead of re-deriving this logic, so the
/// gateway / manual-proof / admin-grant distinction is defined exactly once.
/// Static and stateless -- no DI registration needed, callers pass in the
/// already-scoped <see cref="LearnerDbContext"/>.
/// </summary>
public static class InvoiceEvidenceResolver
{
    /// <summary>
    /// The evidence found for a subscription's payment, if any. Exactly one
    /// of (<see cref="Quote"/> + <see cref="Payment"/>) or <see cref="Proof"/>
    /// is populated, matching <see cref="Source"/>; all are null for
    /// <see cref="InvoiceSources.AdminGrant"/>.
    /// </summary>
    public sealed record Result(BillingQuote? Quote, PaymentTransaction? Payment, ManualPaymentRequest? Proof, string Source);

    /// <summary>
    /// Looks up the real payment evidence for <paramref name="subscription"/>,
    /// preferring a completed gateway payment, then an admin-approved payment
    /// proof, and finally falling back to an unevidenced admin grant.
    /// </summary>
    public static async Task<Result> ResolveAsync(LearnerDbContext db, Subscription subscription, CancellationToken ct)
    {
        var gatewayMatch = await (
            from quote in db.BillingQuotes
            join payment in db.PaymentTransactions on quote.Id equals payment.QuoteId
            // Gateway="manual" is ManualPaymentService's own tag for a proof-approved
            // PaymentTransaction (see ManualPaymentService.ApproveAsync) -- it is a
            // completed transaction, but it is NOT an online-gateway confirmation, so it
            // must never satisfy the Gateway evidence branch here. Excluding it is what
            // makes the ManualProof fallback below actually reachable for a proof-approved
            // subscription purchase instead of every completed transaction (manual
            // included) being misread as a real gateway payment.
            where quote.SubscriptionId == subscription.Id
                && payment.Status == "completed"
                && payment.Gateway != "manual"
            orderby payment.CreatedAt descending
            select new { quote, payment })
            .FirstOrDefaultAsync(ct);

        if (gatewayMatch is not null)
        {
            return new Result(gatewayMatch.quote, gatewayMatch.payment, null, InvoiceSources.Gateway);
        }

        var manualProof = await db.ManualPaymentRequests
            .Where(r => r.AccessGrantedSubscriptionId == subscription.Id
                && (r.Status == "paid" || r.Status == "approved"))
            .OrderByDescending(r => r.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        if (manualProof is not null)
        {
            return new Result(null, null, manualProof, InvoiceSources.ManualProof);
        }

        return new Result(null, null, null, InvoiceSources.AdminGrant);
    }
}
