using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Services;

public partial class LearnerService
{

    public async Task<object> GetBillingSummaryAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        await EnsureSubscriptionInvoiceAsync(userId, cancellationToken);
        var hasInvoices = await db.Invoices.AnyAsync(x => x.UserId == userId, cancellationToken);
        // Fresh accounts have no Subscription/Wallet rows until their first purchase —
        // the summary must degrade to a well-formed "no subscription" payload, not 500.
        // Historical cancelled rows must never become the learner's visible summary.
        var currentPlanId = await db.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => x.CurrentPlanId)
            .FirstOrDefaultAsync(cancellationToken);
        var currentSubscriptions = await db.Subscriptions.AsNoTracking()
            .Where(x => x.UserId == userId
                && (x.Status == SubscriptionStatus.Active
                    || x.Status == SubscriptionStatus.Trial
                    || x.Status == SubscriptionStatus.FreezeRequested
                    || x.Status == SubscriptionStatus.Frozen))
            .OrderByDescending(x => x.ChangedAt)
            .ThenByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        var subscription = currentSubscriptions.FirstOrDefault(x =>
            string.Equals(x.PlanId, currentPlanId, StringComparison.OrdinalIgnoreCase))
            ?? currentSubscriptions.FirstOrDefault();
        var wallet = await db.Wallets.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var freeze = await GetFreezeStatusAsync(userId, cancellationToken);
        var walletPayload = new
        {
            walletId = wallet?.Id,
            creditBalance = wallet?.CreditBalance ?? 0,
            ledgerSummary = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(wallet?.LedgerSummaryJson ?? "[]", [])
        };
        if (subscription is null)
        {
            return new
            {
                subscriptionId = (string?)null,
                planId = (string?)null,
                planCode = "none",
                planName = "No subscription",
                planDescription = (string?)null,
                profession = "all",
                status = "none",
                nextRenewalAt = (DateTimeOffset?)null,
                startedAt = (DateTimeOffset?)null,
                changedAt = (DateTimeOffset?)null,
                freeze,
                price = new { amount = 0m, currency = "GBP", interval = "none" },
                wallet = walletPayload,
                activeAddOns = Enumerable.Empty<object>(),
                entitlements = new
                {
                    productiveSkillReviewsEnabled = false,
                    supportedReviewSubtests = new List<string> { "writing", "speaking" },
                    invoiceDownloadsAvailable = hasInvoices
                },
                plan = (object?)null
            };
        }
        var now = DateTimeOffset.UtcNow;
        var currentPlan = await FindBillingPlanAsync(subscription.PlanId, cancellationToken);
        var activeAddOns = await db.SubscriptionItems.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id
                && x.Status == SubscriptionItemStatus.Active
                && x.StartsAt <= now
                && (x.EndsAt == null || x.EndsAt > now))
            .ToListAsync(cancellationToken);
        var addOnCodes = activeAddOns.Select(x => x.ItemCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var addOnCatalog = addOnCodes.Count == 0
            ? []
            : await db.BillingAddOns.AsNoTracking()
                .Where(x => addOnCodes.Contains(x.Code))
                .ToListAsync(cancellationToken);
        return new
        {
            subscriptionId = subscription.Id,
            planId = subscription.PlanId,
            planCode = currentPlan?.Code ?? subscription.PlanId,
            planName = currentPlan?.Name ?? subscription.PlanId,
            planDescription = currentPlan?.Description,
            profession = currentPlan?.Profession ?? "all",
            status = ToSubscriptionState(subscription.Status),
            nextRenewalAt = subscription.NextRenewalAt,
            startedAt = subscription.StartedAt,
            changedAt = subscription.ChangedAt,
            freeze,
            price = new { amount = subscription.PriceAmount, currency = subscription.Currency, interval = subscription.Interval },
            wallet = walletPayload,
            activeAddOns = activeAddOns.Select(item =>
            {
                var addOn = addOnCatalog.FirstOrDefault(x => string.Equals(x.Code, item.ItemCode, StringComparison.OrdinalIgnoreCase));
                return new
                {
                    id = item.Id,
                    code = item.ItemCode,
                    name = addOn?.Name ?? item.ItemCode,
                    type = item.ItemType,
                    quantity = item.Quantity,
                    status = item.Status.ToString().ToLowerInvariant(),
                    startsAt = item.StartsAt,
                    endsAt = item.EndsAt,
                    quoteId = item.QuoteId,
                    checkoutSessionId = item.CheckoutSessionId,
                    grantCredits = addOn?.GrantCredits ?? 0,
                    price = addOn is null
                        ? null
                        : new { amount = addOn.Price, currency = addOn.Currency, interval = addOn.Interval },
                    description = addOn?.Description
                };
            }),
            entitlements = new
            {
                productiveSkillReviewsEnabled = subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial,
                supportedReviewSubtests = currentPlan is null
                    ? new List<string> { "writing", "speaking" }
                    : JsonSupport.Deserialize<List<string>>(currentPlan.IncludedSubtestsJson, new List<string> { "writing", "speaking" }),
                invoiceDownloadsAvailable = hasInvoices
            },
            plan = currentPlan is null
                ? null
                : new
                {
                    code = currentPlan.Code,
                    name = currentPlan.Name,
                    description = currentPlan.Description,
                    includedCredits = currentPlan.IncludedCredits,
                    durationMonths = currentPlan.DurationMonths,
                    isRenewable = currentPlan.IsRenewable,
                    status = currentPlan.Status.ToString().ToLowerInvariant()
                }
        };
    }

    public async Task<object> GetBillingPlansAsync(string userId, CancellationToken cancellationToken)
    {
        var isPublic = string.IsNullOrWhiteSpace(userId);
        Subscription? subscription = null;
        if (!isPublic)
        {
            await EnsureUserAsync(userId, cancellationToken);
            // Null for accounts that never purchased — the plan list below already handles it.
            subscription = await db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        }

        var normalizedSubscriptionPlanId = subscription is not null ? NormalizeBillingCode(subscription.PlanId) : string.Empty;
        var plans = await db.BillingPlans.AsNoTracking()
            .Where(plan => plan.IsVisible || (subscription != null && plan.Code.ToLower() == normalizedSubscriptionPlanId))
            .OrderBy(plan => plan.DisplayOrder)
            .ThenBy(plan => plan.Price)
            .ToListAsync(cancellationToken);
        var currentPlan = subscription is not null
            ? (plans.FirstOrDefault(plan => string.Equals(plan.Code, subscription.PlanId, StringComparison.OrdinalIgnoreCase))
                ?? plans.FirstOrDefault(plan => plan.Status == BillingPlanStatus.Active)
                ?? plans.FirstOrDefault())
            : plans.FirstOrDefault(plan => plan.Status == BillingPlanStatus.Active) ?? plans.FirstOrDefault();
        return new
        {
            currentPlanId = subscription?.PlanId,
            currentPlanCode = currentPlan?.Code,
            items = plans.Select(plan => new
            {
                planId = plan.Code,
                code = plan.Code,
                label = plan.Name,
                tier = plan.Code,
                description = plan.Description,
                price = new { amount = plan.Price, currency = plan.Currency, interval = plan.Interval },
                reviewCredits = plan.IncludedCredits,
                mockReportsIncluded = true,
                canChangeTo = plan.Status == BillingPlanStatus.Active && (subscription is null || !string.Equals(plan.Code, subscription.PlanId, StringComparison.OrdinalIgnoreCase)),
                changeDirection = subscription is not null && string.Equals(plan.Code, subscription.PlanId, StringComparison.OrdinalIgnoreCase)
                    ? "current"
                    : plan.Price > (currentPlan?.Price ?? plan.Price)
                        ? "upgrade"
                        : plan.Price < (currentPlan?.Price ?? plan.Price)
                            ? "downgrade"
                            : "current",
                badge = plan.Status.ToString().ToLowerInvariant(),
                status = plan.Status.ToString().ToLowerInvariant(),
                displayOrder = plan.DisplayOrder,
                durationMonths = plan.DurationMonths,
                isVisible = plan.IsVisible,
                isRenewable = plan.IsRenewable,
                trialDays = plan.TrialDays,
                includedSubtests = JsonSupport.Deserialize<List<string>>(plan.IncludedSubtestsJson, []),
                entitlements = JsonSupport.Deserialize<Dictionary<string, object?>>(plan.EntitlementsJson, new Dictionary<string, object?>()
                )
            })
        };
    }

      public async Task<object> GetBillingChangePreviewAsync(string userId, string targetPlanId, CancellationToken cancellationToken)
      {
          await EnsureUserAsync(userId, cancellationToken);
          var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId, cancellationToken);
          var currentPlan = await FindBillingPlanAsync(subscription.PlanId, cancellationToken)
              ?? throw ApiException.NotFound("billing_plan_not_found", "Your current billing plan could not be found.");
          var targetPlan = await FindPurchasableBillingPlanAsync(targetPlanId, cancellationToken)
              ?? throw ApiException.Validation(
                  "unknown_plan",
                  $"Unknown billing plan '{targetPlanId}'.",
                  [new ApiFieldError("targetPlanId", "unknown", "Choose a published billing plan.")]);

        var delta = targetPlan.Price - currentPlan.Price;
        var direction = delta >= 0 ? "upgrade" : "downgrade";
        return new
        {
            currentPlanId = currentPlan.Code,
            targetPlanId = targetPlan.Code,
            direction,
            proratedAmount = Math.Round(Math.Abs(delta) / 2m, 2),
            effectiveAt = subscription.NextRenewalAt,
            summary = direction == "upgrade"
                ? $"Switching to {targetPlan.Name} increases your billing amount by {Math.Abs(delta):0.00} {subscription.Currency}."
                : $"Switching to {targetPlan.Name} lowers your billing amount by {Math.Abs(delta):0.00} {subscription.Currency}.",
            currentCreditsIncluded = currentPlan.IncludedCredits,
            targetCreditsIncluded = targetPlan.IncludedCredits
        };
    }

    public async Task<object> CancelOwnSubscriptionAsync(string userId, bool immediate, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId, cancellationToken);

        if (subscription.Status == SubscriptionStatus.Cancelled)
        {
            throw ApiException.Validation("subscription_already_cancelled", "Your subscription is already cancelled.");
        }

        if (subscription.Status == SubscriptionStatus.Expired)
        {
            throw ApiException.Validation("subscription_expired", "Your subscription has already expired.");
        }

        var now = DateTimeOffset.UtcNow;
        var previousStatus = subscription.Status;

        if (immediate)
        {
            SubscriptionStateMachine.Transition(subscription, SubscriptionStatus.Cancelled, "learner_self_cancel_immediate");
            subscription.NextRenewalAt = now;
        }
        subscription.ChangedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerSubscriptionCancelled,
            userId,
            "Subscription",
            subscription.Id,
            now.UtcDateTime.ToString("yyyy-MM-dd"),
            new Dictionary<string, object?>
            {
                ["message"] = immediate
                    ? "Your subscription has been cancelled immediately."
                    : $"Your subscription is scheduled to cancel at the end of your current billing period ({subscription.NextRenewalAt:yyyy-MM-dd}).",
                ["planName"] = subscription.PlanId,
                ["status"] = subscription.Status.ToString().ToLowerInvariant()
            },
            cancellationToken);

        return new
        {
            subscriptionId = subscription.Id,
            status = ToSubscriptionState(subscription.Status),
            cancelledAt = now,
            effectiveEndAt = subscription.NextRenewalAt,
            immediate
        };
    }

    public async Task<object> ReactivateOwnSubscriptionAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId, cancellationToken);

        if (subscription.Status != SubscriptionStatus.Cancelled)
        {
            throw ApiException.Validation("subscription_not_cancelled", "Only cancelled subscriptions can be reactivated.");
        }

        var now = DateTimeOffset.UtcNow;
        SubscriptionStateMachine.ReactivateCancelled(subscription, "learner_self_reactivate", now);
        if (subscription.NextRenewalAt <= now)
        {
            subscription.NextRenewalAt = now.AddMonths(1);
        }

        await db.SaveChangesAsync(cancellationToken);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerSubscriptionChanged,
            userId,
            "Subscription",
            subscription.Id,
            now.UtcDateTime.ToString("yyyy-MM-dd"),
            new Dictionary<string, object?>
            {
                ["message"] = "Your subscription has been reactivated.",
                ["planName"] = subscription.PlanId,
                ["status"] = "active"
            },
            cancellationToken);

        return new
        {
            subscriptionId = subscription.Id,
            status = ToSubscriptionState(subscription.Status),
            reactivatedAt = now,
            nextRenewalAt = subscription.NextRenewalAt
        };
    }

    public Task<object> GetInvoicesAsync(string userId, CancellationToken cancellationToken)
        => GetInvoicesAsync(userId, cursor: null, limit: null, cancellationToken);

    public async Task<object> GetInvoicesAsync(string userId, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureSubscriptionInvoiceAsync(userId, cancellationToken);
        var pageSize = CursorPagination.NormalizeLimit(limit);
        // Paid-only candidate gate: internal Pending rows (awaiting admin
        // verification/hand-over) and Failed rows are never candidate-visible.
        // Case-insensitive because legacy rows use "Paid" capitalisation.
        var invoices = (await db.Invoices
            .Where(x => x.UserId == userId && x.Status.ToLower() == "paid")
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.IssuedAt)
            .ThenByDescending(x => x.Id, StringComparer.Ordinal)
            .ToList();

        IEnumerable<Invoice> window = invoices;
        if (CursorPagination.TryDecode(cursor, out var decoded))
        {
            window = invoices.Where(x =>
            {
                if (x.IssuedAt < decoded.Timestamp) return true;
                if (x.IssuedAt == decoded.Timestamp) return string.CompareOrdinal(x.Id, decoded.Id) < 0;
                return false;
            });
        }

        var page = window.Take(pageSize + 1).ToList();
        var hasMore = page.Count > pageSize;
        var pageInvoices = hasMore ? page.Take(pageSize).ToList() : page;
        string? nextCursor = null;
        if (hasMore)
        {
            var last = pageInvoices[^1];
            nextCursor = CursorPagination.Encode(last.IssuedAt, last.Id);
        }

        return new
        {
            items = pageInvoices.Select(x => new
            {
                invoiceId = x.Id,
                date = x.IssuedAt,
                amount = x.Amount,
                currency = x.Currency,
                status = x.Status,
                description = x.Description,
                downloadUrl = platformLinks.BuildApiUrl($"/v1/billing/invoices/{Uri.EscapeDataString(x.Id)}/download")
            }),
            nextCursor
        };
    }

    public async Task<GeneratedDownloadFile> GetInvoiceDownloadAsync(string userId, string invoiceId, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        var invoice = await db.Invoices.FirstOrDefaultAsync(x => x.UserId == userId && x.Id == invoiceId, cancellationToken)
            ?? throw ApiException.NotFound("invoice_not_found", "Invoice not found.");
        // Paid-only download gate: a Pending/Failed invoice must not be
        // downloadable via a direct URL even if its id is known.
        if (!string.Equals(invoice.Status, "Paid", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.NotFound("invoice_not_found", "Invoice not found.");
        }

        var pdf = (invoicePdfService ?? new InvoicePdfService()).Generate(new InvoicePdfModel(
            InvoiceId: invoice.Id,
            Number: invoice.Number,
            IssuedAt: invoice.IssuedAt,
            Amount: invoice.Amount,
            Currency: invoice.Currency,
            Status: invoice.Status,
            Description: invoice.Description,
            BillToName: user.DisplayName,
            BillToEmail: user.Email));

        return new GeneratedDownloadFile(new MemoryStream(pdf.Bytes), "application/pdf", pdf.Filename);
    }

    /// <summary>
    /// Guarantees the learner's current paid subscription has a downloadable invoice.
    /// Invoices are normally created inside payment-webhook fulfillment
    /// (<see cref="ApplyCheckoutCompletionAsync"/>); subscriptions created another way
    /// (admin grant, complimentary, or a purchase that never ran fulfillment) would
    /// otherwise have none. This backfill now resolves real payment evidence via
    /// <see cref="InvoiceEvidenceResolver.ResolveAsync"/> before minting the invoice, so
    /// the row's QuoteId/CheckoutSessionId/SubscriptionId/Source genuinely reflect
    /// whatever evidence exists (gateway payment, approved manual proof, or none — an
    /// admin grant) instead of being left blank. Idempotent and safe to call on every
    /// billing read:
    ///   • no-op for free plans (PriceAmount &lt;= 0),
    ///   • no-op when an invoice already covers the current purchase (matched by plan
    ///     version or by amount) so it never duplicates a real checkout invoice,
    ///   • deterministic per-purchase id so concurrent reads converge on one row.
    /// </summary>
    private async Task<bool> EnsureSubscriptionInvoiceAsync(string userId, CancellationToken cancellationToken)
    {
        var subscription = await db.Subscriptions
            .Where(x => x.UserId == userId && x.Status != SubscriptionStatus.Draft && x.PriceAmount > 0)
            .OrderByDescending(x => x.Status == SubscriptionStatus.Active)
            .ThenByDescending(x => x.ChangedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null || subscription.PriceAmount <= 0)
        {
            return false;
        }

        // Paid-only backfill gate: never mint a candidate-visible Paid invoice
        // without real payment evidence. Pending/unpaid orders (no completed
        // gateway payment and no approved manual proof) and pure admin grants
        // must not gain an invoice from a mere billing-page read. Failed,
        // pending, processing, cancelled or abandoned payments therefore expose
        // nothing here; the webhook/approval paths mint the Paid row on final
        // verified success instead.
        var gateEvidence = await InvoiceEvidenceResolver.ResolveAsync(db, subscription, cancellationToken);
        if (string.Equals(gateEvidence.Source, InvoiceSources.AdminGrant, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        // Verification flow: a Pending order (awaiting admin approval/hand-over)
        // is paid at the gateway but not yet verified — do not release the
        // candidate invoice until Approve/MarkFulfilled flips it Active.
        // Frozen is a paid, paused subscription — not an unverified order —
        // so it keeps invoice access like Active.
        if (subscription.Status != SubscriptionStatus.Active
            && subscription.Status != SubscriptionStatus.Frozen)
        {
            return false;
        }

        var alreadyCovered = await db.Invoices.AnyAsync(
            x => x.UserId == userId
                 && (x.Amount == subscription.PriceAmount
                     || (subscription.PlanVersionId != null && x.PlanVersionId == subscription.PlanVersionId)),
            cancellationToken);
        if (alreadyCovered)
        {
            return false;
        }

        // Compact, deterministic id keyed to this exact purchase (subscription + plan +
        // start instant). A later course purchase changes StartedAt and yields a new
        // invoice; repeat reads of the same purchase converge on this id.
        var compositeKey = $"{subscription.Id}|{subscription.PlanId}|{subscription.StartedAt.UtcTicks}";
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(compositeKey)))[..24]
            .ToLowerInvariant();
        var invoiceId = $"inv-sub-{hash}";

        if (await db.Invoices.AnyAsync(x => x.Id == invoiceId, cancellationToken))
        {
            return false;
        }

        var currentPlan = await FindBillingPlanAsync(subscription.PlanId, cancellationToken);
        var issuedAt = subscription.StartedAt != default
            ? subscription.StartedAt
            : (subscription.ChangedAt != default ? subscription.ChangedAt : DateTimeOffset.UtcNow);
        var planName = currentPlan?.Name ?? subscription.PlanId;
        var description = string.IsNullOrWhiteSpace(subscription.Interval)
            ? planName
            : $"{planName} ({subscription.Interval})";

        var evidence = gateEvidence;

        db.Invoices.Add(new Invoice
        {
            Id = invoiceId,
            UserId = userId,
            Number = await AllocateInvoiceNumberAsync(userId, invoiceId, cancellationToken),
            IssuedAt = issuedAt,
            Amount = subscription.PriceAmount,
            Currency = subscription.Currency,
            Status = "Paid",
            Description = description,
            PlanVersionId = subscription.PlanVersionId,
            SubscriptionId = subscription.Id,
            Source = evidence.Source,
            QuoteId = evidence.Quote?.Id,
            CheckoutSessionId = evidence.Quote?.CheckoutSessionId ?? evidence.Payment?.GatewayTransactionId,
            ReconciledAt = DateTimeOffset.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // A concurrent billing read created the same deterministic invoice first.
            // The invoice now exists either way — drop our pending insert and move on.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>
    /// Admin one-shot: ensures every learner on a paid subscription has a downloadable
    /// invoice. Idempotent — reuses <see cref="EnsureSubscriptionInvoiceAsync"/>, so it
    /// only creates the invoices that are genuinely missing. Returns how many users were
    /// scanned and how many invoices were created.
    /// </summary>
    public async Task<object> BackfillSubscriptionInvoicesAsync(CancellationToken cancellationToken)
    {
        var userIds = await db.Subscriptions
            .AsNoTracking()
            .Where(x => x.PriceAmount > 0 && x.Status != SubscriptionStatus.Draft)
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var created = 0;
        foreach (var userId in userIds)
        {
            if (await EnsureSubscriptionInvoiceAsync(userId, cancellationToken))
            {
                created++;
            }
        }

        return new { scanned = userIds.Count, created };
    }

    /// <summary>
    /// Renews the learner's one-time self-service freeze entitlement when they buy a
    /// new subscription/plan. The freeze rule is "once per subscription purchase": a
    /// fresh course purchase should grant a fresh freeze. We reuse the existing
    /// <see cref="AccountFreezeEntitlement.ResetAt"/> semantics — eligibility treats a
    /// reset entitlement as available, and the next freeze re-consumes it
    /// (clearing ResetAt). No-op if the entitlement was never consumed.
    /// </summary>
    private async Task ResetFreezeEntitlementForNewPurchaseAsync(string userId, CancellationToken cancellationToken)
    {
        var entitlement = await db.AccountFreezeEntitlements.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (entitlement is null || entitlement.ConsumedAt is null || entitlement.ResetAt is not null)
        {
            return;
        }

        entitlement.ResetAt = DateTimeOffset.UtcNow;
        entitlement.ResetReason = "new_subscription_purchase";
    }
}
