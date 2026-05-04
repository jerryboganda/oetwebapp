using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Slice D — Checkout / Quote / Subscription / Invoice flow hardening.
///
/// New helpers split out of <c>LearnerService.cs</c> so the existing 9k-line
/// orchestration file does not have to grow further. Existing fulfillment
/// code paths (<c>ApplyCheckoutCompletionAsync</c>, <c>CreateCheckoutSessionAsync</c>)
/// gain re-validation and monotonic invoice numbering by calling into these
/// helpers — see <c>docs/billing-hardening/D-checkout.md</c>.
/// </summary>
public partial class LearnerService
{
    /// <summary>Default quote validity window. The DB <c>BillingQuote.ExpiresAt</c>
    /// remains the source of truth; this is a fallback for legacy callers
    /// constructing quotes without an explicit expiry.</summary>
    public static readonly TimeSpan BillingQuoteDefaultLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Hard ceiling on quote validity to prevent indefinite snapshots.</summary>
    public static readonly TimeSpan BillingQuoteMaxLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// Verify a stored quote is still safe to fulfil. Rejects expired or
    /// already-completed quotes with a structured <see cref="ApiException"/>
    /// using the <c>billing_quote_expired</c> / <c>billing_quote_already_consumed</c>
    /// error codes the frontend expects.
    /// </summary>
    public static void EnsureQuoteIsFulfillable(BillingQuote quote, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(quote);

        if (quote.Status == BillingQuoteStatus.Completed)
        {
            throw ApiException.Conflict(
                "billing_quote_already_consumed",
                "This billing quote has already been fulfilled.");
        }

        if (quote.Status == BillingQuoteStatus.Cancelled)
        {
            throw ApiException.Conflict(
                "billing_quote_cancelled",
                "This billing quote was cancelled and cannot be fulfilled.");
        }

        if (quote.ExpiresAt < now)
        {
            throw ApiException.Validation(
                "billing_quote_expired",
                "This billing quote has expired. Refresh your cart and try again.");
        }
    }

    /// <summary>
    /// Re-validate the quote snapshot against the current catalog at
    /// fulfillment time. If the underlying plan / add-on / coupon has been
    /// re-versioned or re-priced since the quote was created, refuse the
    /// fulfillment instead of silently re-pricing the learner.
    /// </summary>
    public static void EnsureQuoteSnapshotMatchesCatalog(
        BillingQuote quote,
        BillingPlanVersion? livePlanVersion,
        IReadOnlyDictionary<string, BillingAddOnVersion?> liveAddOnVersions,
        BillingCouponVersion? liveCouponVersion)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(liveAddOnVersions);

        if (!string.IsNullOrWhiteSpace(quote.PlanVersionId))
        {
            if (livePlanVersion is null
                || !string.Equals(livePlanVersion.Id, quote.PlanVersionId, StringComparison.Ordinal))
            {
                throw ApiException.Conflict(
                    "billing_quote_snapshot_drift",
                    $"Quote plan version '{quote.PlanVersionId}' is no longer the active catalog version. Re-quote required.");
            }
        }

        var quoteAddOnVersionIds = JsonSupport.Deserialize<Dictionary<string, string>>(quote.AddOnVersionIdsJson, []);
        foreach (var (code, expectedVersionId) in quoteAddOnVersionIds)
        {
            if (string.IsNullOrWhiteSpace(expectedVersionId)) continue;
            liveAddOnVersions.TryGetValue(code, out var live);
            if (live is null
                || !string.Equals(live.Id, expectedVersionId, StringComparison.Ordinal))
            {
                throw ApiException.Conflict(
                    "billing_quote_snapshot_drift",
                    $"Quote add-on '{code}' version '{expectedVersionId}' is no longer the active catalog version. Re-quote required.");
            }
        }

        if (!string.IsNullOrWhiteSpace(quote.CouponVersionId))
        {
            if (liveCouponVersion is null
                || !string.Equals(liveCouponVersion.Id, quote.CouponVersionId, StringComparison.Ordinal))
            {
                throw ApiException.Conflict(
                    "billing_quote_snapshot_drift",
                    $"Quote coupon version '{quote.CouponVersionId}' is no longer the active catalog version. Re-quote required.");
            }
        }
    }

    private async Task EnsureQuoteSnapshotMatchesCurrentCatalogAsync(BillingQuote quote, CancellationToken cancellationToken)
    {
        BillingPlanVersion? livePlanVersion = null;
        if (!string.IsNullOrWhiteSpace(quote.PlanVersionId) && !string.IsNullOrWhiteSpace(quote.PlanCode))
        {
            var plan = await db.BillingPlans.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Code == quote.PlanCode, cancellationToken);
            if (plan is not null && !string.IsNullOrWhiteSpace(plan.ActiveVersionId))
            {
                livePlanVersion = await db.BillingPlanVersions.AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == plan.ActiveVersionId, cancellationToken);
            }
        }

        var liveAddOnVersions = new Dictionary<string, BillingAddOnVersion?>(StringComparer.OrdinalIgnoreCase);
        var quoteAddOnVersionIds = JsonSupport.Deserialize<Dictionary<string, string>>(quote.AddOnVersionIdsJson, []);
        foreach (var code in quoteAddOnVersionIds.Keys)
        {
            var addOn = await db.BillingAddOns.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
            liveAddOnVersions[code] = addOn is not null && !string.IsNullOrWhiteSpace(addOn.ActiveVersionId)
                ? await db.BillingAddOnVersions.AsNoTracking().FirstOrDefaultAsync(item => item.Id == addOn.ActiveVersionId, cancellationToken)
                : null;
        }

        BillingCouponVersion? liveCouponVersion = null;
        if (!string.IsNullOrWhiteSpace(quote.CouponVersionId) && !string.IsNullOrWhiteSpace(quote.CouponCode))
        {
            var coupon = await db.BillingCoupons.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Code == quote.CouponCode, cancellationToken);
            if (coupon is not null && !string.IsNullOrWhiteSpace(coupon.ActiveVersionId))
            {
                liveCouponVersion = await db.BillingCouponVersions.AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == coupon.ActiveVersionId, cancellationToken);
            }
        }

        EnsureQuoteSnapshotMatchesCatalog(quote, livePlanVersion, liveAddOnVersions, liveCouponVersion);
    }

    /// <summary>
    /// Allocate the next monotonic invoice number for a learner. Backed by the
    /// <c>InvoiceNumberAllocations</c> table created in migration
    /// <c>20260504160000_HardenCheckout</c>. Numbers are never reused; a unique
    /// constraint on <c>InvoiceId</c> makes the allocation idempotent for a
    /// given invoice (replays return the previously-allocated number).
    /// </summary>
    public async Task<int> AllocateInvoiceNumberAsync(string userId, string invoiceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId is required", nameof(userId));
        if (string.IsNullOrWhiteSpace(invoiceId)) throw new ArgumentException("invoiceId is required", nameof(invoiceId));

        if (!db.Database.IsNpgsql())
        {
            var existingNumber = await db.Invoices.AsNoTracking()
                .Where(invoice => invoice.Id == invoiceId)
                .Select(invoice => invoice.Number)
                .FirstOrDefaultAsync(cancellationToken);
            if (existingNumber is not null)
            {
                return existingNumber.Value;
            }

            var maxNumber = await db.Invoices.AsNoTracking()
                .Where(invoice => invoice.UserId == userId && invoice.Number != null)
                .MaxAsync(invoice => invoice.Number, cancellationToken) ?? 0;
            return maxNumber + 1;
        }

        // Idempotent path: invoice already has a number → return it.
        var existing = await db.Database
            .SqlQuery<int>($@"SELECT ""Sequence"" AS ""Value""
                              FROM ""InvoiceNumberAllocations""
                              WHERE ""InvoiceId"" = {invoiceId}
                              LIMIT 1")
            .ToListAsync(cancellationToken);
        if (existing.Count > 0) return existing[0];

        // Allocate next sequence atomically. We retry on the unique-key race
        // because two concurrent fulfillments for the same user could collide
        // on (UserId, Sequence).
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var maxRows = await db.Database
                .SqlQuery<int>($@"SELECT COALESCE(MAX(""Sequence""), 0) AS ""Value""
                                  FROM ""InvoiceNumberAllocations""
                                  WHERE ""UserId"" = {userId}")
                .ToListAsync(cancellationToken);
            var next = (maxRows.Count == 0 ? 0 : maxRows[0]) + 1;
            var now = DateTimeOffset.UtcNow.ToString("O");

            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO ""InvoiceNumberAllocations""
                        (""UserId"", ""Sequence"", ""InvoiceId"", ""AllocatedAt"")
                    VALUES ({userId}, {next}, {invoiceId}, {now})
                ", cancellationToken);
                return next;
            }
            catch (System.Data.Common.DbException)
            {
                // Either (UserId, Sequence) collision — retry with bumped seq —
                // or (InvoiceId) collision — re-read the existing allocation.
                var concurrent = await db.Database
                    .SqlQuery<int>($@"SELECT ""Sequence"" AS ""Value""
                                      FROM ""InvoiceNumberAllocations""
                                      WHERE ""InvoiceId"" = {invoiceId}
                                      LIMIT 1")
                    .ToListAsync(cancellationToken);
                if (concurrent.Count > 0) return concurrent[0];
            }
            catch (DbUpdateException)
            {
                var concurrent = await db.Database
                    .SqlQuery<int>($@"SELECT ""Sequence"" AS ""Value""
                                      FROM ""InvoiceNumberAllocations""
                                      WHERE ""InvoiceId"" = {invoiceId}
                                      LIMIT 1")
                    .ToListAsync(cancellationToken);
                if (concurrent.Count > 0) return concurrent[0];
            }
        }

        throw ApiException.Conflict(
            "invoice_number_allocation_failed",
            "Could not allocate a unique invoice number after multiple attempts.");
    }
}
