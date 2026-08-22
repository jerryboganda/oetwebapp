using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
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

    private async Task<object?> TryReuseUnpaidCheckoutSessionAsync(
        string userId,
        BillingQuote quoteEntity,
        BillingQuoteResponse quoteResponse,
        string productType,
        int quantity,
        string gatewayLabel,
        CancellationToken cancellationToken)
    {
        var pending = await FindLatestOwnerCheckoutTransactionAsync(userId, quoteEntity, cancellationToken);
        if (pending is null)
        {
            return null;
        }

        if (string.Equals(pending.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Conflict(
                "billing_quote_already_consumed",
                "This billing quote has already been fulfilled.");
        }

        if (!string.Equals(pending.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!string.Equals(pending.Gateway, gatewayLabel, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var metadata = JsonSupport.Deserialize<Dictionary<string, object?>>(pending.MetadataJson, []);
        if (ReadMetadataFlag(metadata, "superseded"))
        {
            return null;
        }

        var checkoutUrl = ResolveReusableCheckoutUrl(pending, metadata);
        if (string.IsNullOrWhiteSpace(checkoutUrl))
        {
            return null;
        }

        return BuildCheckoutSessionClientResponse(
            quoteEntity,
            quoteResponse,
            productType,
            quantity,
            pending.Gateway,
            pending.GatewayTransactionId,
            checkoutUrl,
            ReadMetadataString(metadata, "providerIntentId"),
            "open");
    }

    private async Task SupersedeUnpaidCheckoutSessionAsync(
        string userId,
        BillingQuote quoteEntity,
        CancellationToken cancellationToken)
    {
        var pending = await FindLatestOwnerCheckoutTransactionAsync(userId, quoteEntity, cancellationToken);
        if (pending is null || !string.Equals(pending.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var metadata = JsonSupport.Deserialize<Dictionary<string, object?>>(pending.MetadataJson, []);
        metadata["superseded"] = true;
        pending.MetadataJson = JsonSupport.Serialize(metadata);
        pending.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<PaymentTransaction?> FindLatestOwnerCheckoutTransactionAsync(
        string userId,
        BillingQuote quoteEntity,
        CancellationToken cancellationToken)
    {
        var matches = await db.PaymentTransactions
            .Where(transaction =>
                transaction.LearnerUserId == userId
                && (transaction.QuoteId == quoteEntity.Id
                    || transaction.GatewayTransactionId == quoteEntity.CheckoutSessionId))
            .OrderByDescending(transaction => transaction.UpdatedAt)
            .ThenByDescending(transaction => transaction.CreatedAt)
            .ToListAsync(cancellationToken);

        return matches.FirstOrDefault(transaction =>
                   string.Equals(transaction.GatewayTransactionId, quoteEntity.CheckoutSessionId, StringComparison.Ordinal))
               ?? matches.FirstOrDefault();
    }

    private static string? ResolveReusableCheckoutUrl(
        PaymentTransaction pending,
        IReadOnlyDictionary<string, object?> metadata)
    {
        var storedUrl = ReadMetadataString(metadata, "checkoutUrl");
        if (!string.IsNullOrWhiteSpace(storedUrl))
        {
            return storedUrl;
        }

        if (string.Equals(pending.Gateway, PaymentGatewayNames.Whop, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(pending.GatewayTransactionId))
        {
            return $"https://whop.com/embedded/checkout/{Uri.EscapeDataString(pending.GatewayTransactionId)}/";
        }

        return null;
    }

    private static string? ReadMetadataString(IReadOnlyDictionary<string, object?> metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static bool ReadMetadataFlag(IReadOnlyDictionary<string, object?> metadata, string key)
        => metadata.TryGetValue(key, out var value) && value switch
        {
            bool flag => flag,
            JsonElement element when element.ValueKind is JsonValueKind.True => true,
            JsonElement element when element.ValueKind is JsonValueKind.String
                && bool.TryParse(element.GetString(), out var parsed) => parsed,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => false
        };

    private static object BuildCheckoutSessionClientResponse(
        BillingQuote quoteEntity,
        BillingQuoteResponse quoteResponse,
        string productType,
        int quantity,
        string gatewayLabel,
        string checkoutSessionId,
        string checkoutUrl,
        string? clientSecret,
        string state)
        => new
        {
            checkoutSessionId,
            quoteId = quoteEntity.Id,
            productType,
            quantity,
            targetPlanId = quoteEntity.PlanCode,
            couponCode = quoteEntity.CouponCode,
            addOnCodes = JsonSupport.Deserialize<List<string>>(quoteEntity.AddOnCodesJson, []),
            subtotalAmount = quoteEntity.SubtotalAmount,
            discountAmount = quoteEntity.DiscountAmount,
            totalAmount = quoteEntity.TotalAmount,
            currency = quoteEntity.Currency,
            gateway = gatewayLabel,
            quote = quoteResponse,
            checkoutUrl,
            clientSecret,
            state
        };

    private static string SerializeCheckoutPaymentMetadata(
        BillingQuote quoteEntity,
        string productType,
        string? providerIntentId,
        string? purchaseTarget,
        string checkoutUrl)
        => JsonSupport.Serialize(new
        {
            quoteId = quoteEntity.Id,
            productType,
            providerIntentId,
            purchaseTarget,
            addOnCodes = JsonSupport.Deserialize<List<string>>(quoteEntity.AddOnCodesJson, []),
            planCode = quoteEntity.PlanCode,
            couponCode = quoteEntity.CouponCode,
            planVersionId = quoteEntity.PlanVersionId,
            addOnVersionIds = DeserializeAddOnVersionIds(quoteEntity),
            couponVersionId = quoteEntity.CouponVersionId,
            checkoutUrl
        });
}
