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

    private async Task ApplyWalletTopUpCompletionAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        var metadata = ReadObject(JsonSupport.Deserialize<object?>(transaction.MetadataJson ?? "{}", null));
        var credits = ReadInt(metadata?.GetValueOrDefault("credits")) ?? 0;
        var bonus = ReadInt(metadata?.GetValueOrDefault("bonus")) ?? 0;
        var totalCredits = ReadInt(metadata?.GetValueOrDefault("totalCredits")) ?? credits + bonus;
        if (totalCredits <= 0)
        {
            return;
        }

        await CreditWalletForPaymentAsync(
            transaction.LearnerUserId,
            totalCredits,
            "top_up",
            "payment",
            transaction.GatewayTransactionId,
            $"Wallet top-up: {totalCredits} credits",
            ct);

        var invoiceId = TruncateIdentifier($"inv-topup-{transaction.GatewayTransactionId}");
        // NOTE: intentionally flow-local, not via InvoiceEvidenceResolver — wallet
        // top-ups carry no subscription scope (the resolver is per-subscription),
        // and this path only runs for completed payments. See
        // ManualPaymentService.ApproveAsync for the resolver-routed promotion.
        var existingInvoice = await db.Invoices.FirstOrDefaultAsync(x => x.Id == invoiceId, ct);
        if (existingInvoice is null)
        {
            db.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                UserId = transaction.LearnerUserId,
                IssuedAt = DateTimeOffset.UtcNow,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Status = "Paid",
                Description = $"Wallet top-up: {credits} credits + {bonus} bonus credits",
                Number = await AllocateInvoiceNumberAsync(transaction.LearnerUserId, invoiceId, ct),
                CheckoutSessionId = transaction.GatewayTransactionId,
                Source = InvoiceSources.Gateway,
                ReconciledAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existingInvoice.CheckoutSessionId ??= transaction.GatewayTransactionId;
        }

        await AddBillingEventIfMissingAsync(new BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            EventType = "wallet_top_up_completed",
            EntityType = "PaymentTransaction",
            EntityId = transaction.GatewayTransactionId,
            PayloadJson = JsonSupport.Serialize(new
            {
                totalCredits,
                credits,
                bonus,
                amount = transaction.Amount,
                currency = transaction.Currency,
                gateway = transaction.Gateway
            }),
            OccurredAt = DateTimeOffset.UtcNow
        }, ct);
    }

    /// <summary>
    /// Delivery method for a purchased plan, read from the immutable BillingPlanVersion
    /// locked to the quote so a mid-flight admin edit cannot change how an already-paid
    /// order is delivered. Falls back to the live plan, then to automatic_web.
    /// </summary>
    private async Task<string> ResolvePlanDeliveryMethodAsync(string? planVersionId, string? planCode, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(planVersionId))
        {
            var version = await db.BillingPlanVersions.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == planVersionId, ct);
            if (version is not null)
            {
                return DeliveryMethods.IsValid(version.DeliveryMethod)
                    ? version.DeliveryMethod
                    : DeliveryMethods.AutomaticWeb;
            }
        }

        if (!string.IsNullOrWhiteSpace(planCode))
        {
            var livePlan = await FindBillingPlanAsync(planCode, ct);
            if (livePlan is not null)
            {
                return DeliveryMethods.IsValid(livePlan.DeliveryMethod)
                    ? livePlan.DeliveryMethod
                    : DeliveryMethods.AutomaticWeb;
            }
        }

        return DeliveryMethods.AutomaticWeb;
    }

    /// <summary>
    /// Mint the system receipt that stands in for a learner-uploaded proof file on a card
    /// gateway order, so every order carries exactly one proof row for the admin dashboard.
    /// Idempotent on PaymentTransactionId (completion fires from both the webhook and the
    /// synchronous capture path). This is part of the authoritative payment unit of work:
    /// a successful payment must not disappear from the admin queue because receipt
    /// persistence failed.
    /// </summary>
    private async Task TryWriteGatewayReceiptAsync(
        PaymentTransaction transaction,
        BillingQuote quote,
        string courseName,
        string? subscriptionId,
        CancellationToken ct)
    {
        if (manualPaymentService is null)
        {
            throw new InvalidOperationException("Gateway payment receipt service is not available.");
        }

        try
        {
            var receipt = await manualPaymentService.CreateGatewayReceiptAsync(
                transaction.LearnerUserId,
                transaction,
                courseName,
                quote.PlanCode,
                subscriptionId,
                ct);
            if (string.IsNullOrWhiteSpace(receipt.AccessGrantedSubscriptionId)
                && !string.IsNullOrWhiteSpace(quote.SubscriptionId))
            {
                receipt.AccessGrantedSubscriptionId = quote.SubscriptionId;
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(
                ex,
                "Failed to write gateway payment receipt for transaction {GatewayTransactionId} (quote {QuoteId}). Access was granted regardless.",
                transaction.GatewayTransactionId,
                quote.Id);
        }
    }

    private async Task ApplyCheckoutCompletionAsync(PaymentTransaction transaction, CancellationToken ct, PaymentWebhookEvent? webhookEvent = null)
    {
        var quote = await GetQuoteForTransactionAsync(transaction, ct);
        if (quote is null || quote.Status == BillingQuoteStatus.Completed)
        {
            return;
        }

        EnsureWebhookMatchesAuthoritativeOrder(webhookEvent, quote, transaction);

        var user = await EnsureUserAsync(transaction.LearnerUserId, ct);
        var subscription = !string.IsNullOrWhiteSpace(quote.SubscriptionId)
            ? await db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == transaction.LearnerUserId && x.Id == quote.SubscriptionId, ct)
            : await db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == transaction.LearnerUserId, ct);
        if (subscription is null)
        {
            throw ApiException.Validation(
                "billing_quote_parent_missing",
                "The enrolment selected for this checkout could not be found.");
        }
        var quoteResponse = DeserializeQuoteResponse(quote);
        var catalogSnapshot = DeserializeQuoteCatalogSnapshot(quote);
        var addOnVersionIds = DeserializeAddOnVersionIds(quote);
        var now = DateTimeOffset.UtcNow;
        var planPendingVerification = false;
        // Post-payment: both callers of this method are gated on a verified
        // "completed" gateway status, so the charge already stands. The quote's
        // expiry window must not void it — see EnsureQuoteIsFulfillable.
        EnsureQuoteIsFulfillable(quote, now, paymentAlreadySettled: true);
        await EnsureQuoteSnapshotMatchesCurrentCatalogAsync(quote, ct);

        if (!string.IsNullOrWhiteSpace(quote.PlanCode) && string.Equals(transaction.TransactionType, "subscription_payment", StringComparison.OrdinalIgnoreCase))
        {
            var targetPlan = catalogSnapshot?.Plan;
            if (targetPlan is null)
            {
                var livePlan = await FindBillingPlanAsync(quote.PlanCode, ct);
                if (livePlan is not null)
                {
                    targetPlan = new BillingQuotePlanSnapshot
                    {
                        Code = livePlan.Code,
                        Name = livePlan.Name,
                        Price = livePlan.Price,
                        Currency = livePlan.Currency,
                        Interval = livePlan.Interval,
                        DurationMonths = livePlan.DurationMonths,
                        IncludedCredits = livePlan.IncludedCredits,
                        BundledAiCredits = livePlan.BundledAiCredits
                    };
                }
            }

            if (targetPlan is not null)
            {
                subscription.PlanId = targetPlan.Code;
                subscription.PlanVersionId = quote.PlanVersionId;

                // Master Catalogue §6 Flow A: regular packages (Products 1-29)
                // use a verification flow. Payment alone never releases access:
                // the order parks at Pending Verification and a gateway-receipt
                // proof row (written below via TryWriteGatewayReceiptAsync) waits
                // in Admin > Billing > Orders & Payments for Approve/Accept.
                // Only plans explicitly configured for Telegram/manual delivery
                // take the older pending_manual hand-over path instead.
                // EXCEPTION — standalone Listening Recalls (code
                // "listening-recalls" ONLY): verified successful payment grants
                // immediately with no admin step. Bound to the stable plan code,
                // never price/label, and never triggered by bundles that merely
                // contain recalls content (their plan code differs).
                var isStandaloneListeningRecalls = ListeningRecallsPolicy.IsStandaloneListeningRecalls(quote.PlanCode)
                    || ListeningRecallsPolicy.IsStandaloneListeningRecalls(targetPlan.Code);
                var deliveryMethod = await ResolvePlanDeliveryMethodAsync(quote.PlanVersionId, quote.PlanCode, ct);
                if (isStandaloneListeningRecalls)
                {
                    subscription.FulfilmentStatus = FulfilmentStatuses.Auto;
                }
                else if (DeliveryMethods.RequiresManualFulfilment(deliveryMethod))
                {
                    // WhatsApp / manual-web / manual-material: the learner has paid, but an
                    // admin still has to hand the package over. Park it at pending_manual and
                    // leave Status alone — a scaffold subscription stays Pending (which the
                    // entitlement resolver treats as FREE, so this grants nothing), and an
                    // existing Active subscription is never downgraded out from under a
                    // learner who already has access. Admin "Mark Fulfilled" flips both.
                    subscription.FulfilmentStatus = FulfilmentStatuses.PendingManual;
                }
                else
                {
                    subscription.FulfilmentStatus = FulfilmentStatuses.PendingVerification;
                }

                planPendingVerification = string.Equals(
                    subscription.FulfilmentStatus,
                    FulfilmentStatuses.PendingVerification,
                    StringComparison.OrdinalIgnoreCase);

                subscription.PriceAmount = targetPlan.Price;
                subscription.Currency = targetPlan.Currency;
                subscription.Interval = targetPlan.Interval;
                subscription.ChangedAt = now;
                if (subscription.StartedAt == default)
                {
                    subscription.StartedAt = now;
                }

                if (subscription.NextRenewalAt <= now)
                {
                    subscription.NextRenewalAt = now.AddMonths(Math.Max(1, targetPlan.DurationMonths));
                }

                if (!planPendingVerification)
                {
                    user.CurrentPlanId = targetPlan.Code;

                    if (targetPlan.IncludedCredits > 0)
                    {
                        await CreditWalletForPaymentAsync(
                            transaction.LearnerUserId,
                            targetPlan.IncludedCredits,
                            "plan_grant",
                            "subscription",
                            quote.Id,
                            $"Included credits for {targetPlan.Name}",
                            ct);
                    }

                    if (targetPlan.BundledAiCredits > 0)
                    {
                        var inserted = await CreditAiLedgerForPlanPaymentAsync(
                            transaction.LearnerUserId,
                            targetPlan,
                            targetPlan.BundledAiCredits,
                            quote.Id,
                            now,
                            ct);
                        if (inserted)
                        {
                            subscription.AiCreditsRemaining = checked(subscription.AiCreditsRemaining + targetPlan.BundledAiCredits);
                        }

                        if (aiPackageCreditService is not null)
                        {
                            var giftExpiry = targetPlan.DurationMonths > 0
                                ? now.AddMonths(targetPlan.DurationMonths)
                                : now.AddDays(180);
                            await aiPackageCreditService.GrantCourseGiftCreditsAsync(
                                transaction.LearnerUserId,
                                 targetPlan.Code,
                                 targetPlan.Name,
                                 targetPlan.BundledAiCredits,
                                 $"plan:{quote.Id}:{targetPlan.Code}",
                                 giftExpiry,
                                 ct,
                                 AiPackageCreditSources.Plan(subscription.Id, targetPlan.Code),
                                 subscription.StartedAt);
                        }
                    }
                }

                // Provision the rest of the bundled entitlement template (writing
                // assessments, speaking sessions, Tutor Book / Basic English
                // unlocks, and the access-duration expiry). The plan snapshot the
                // method already has (`targetPlan`) only carries AI credits, so we
                // read these fields from the immutable BillingPlanVersion locked to
                // this purchase (`quote.PlanVersionId`) and fall back to the live
                // BillingPlan. AI credits are intentionally left untouched here —
                // they are granted exactly once above via the AI-credit ledger, so
                // ApplyPlanEntitlements must NOT re-touch AiCreditsRemaining. This
                // runs once per completion (the whole method early-returns when the
                // quote is already Completed), so replays never re-stamp.
                var planVersion = string.IsNullOrWhiteSpace(quote.PlanVersionId)
                    ? null
                    : await db.BillingPlanVersions.AsNoTracking()
                        .FirstOrDefaultAsync(v => v.Id == quote.PlanVersionId, ct);
                if (planVersion is not null)
                {
                    SubscriptionBundleInitializer.ApplyPlanEntitlements(subscription, planVersion, now);
                }
                else
                {
                    var livePlanForBundle = await FindBillingPlanAsync(quote.PlanCode, ct);
                    if (livePlanForBundle is not null)
                    {
                        SubscriptionBundleInitializer.ApplyPlanEntitlements(subscription, livePlanForBundle, now);
                    }
                }

                // Freeze rule is "once per subscription purchase" — buying a new plan
                // renews the learner's one-time self-service freeze entitlement.
                await ResetFreezeEntitlementForNewPurchaseAsync(transaction.LearnerUserId, ct);

                // Final rule: Before payment = Draft only. Successful payment +
                // admin approval required = Pending; automatic = Active.
                // Move it to Pending so the admin queue shows "payment succeeded, awaiting approval".
                // This is the gateway from the pre-payment state to the paid Pending state.
                if (planPendingVerification || DeliveryMethods.RequiresManualFulfilment(deliveryMethod))
                {
                    SubscriptionStateMachine.Transition(subscription, SubscriptionStatus.Pending, "checkout_pending_approval");
                }
                else
                {
                    SubscriptionStateMachine.Transition(subscription, SubscriptionStatus.Active, "checkout_completed");
                }
            }
        }

        // Final subscription/payment logic:
        // - Before successful payment = Draft only (hidden, no entitlements).
        // - Successful payment + admin approval required = Draft → Pending (payment
        //   succeeded, waiting for admin). Pending → Active only via admin action.
        // - Successful payment + automatic access = Draft → Active immediately.
        // Pending is never created before a successful payment.
        // This covers add-on / credit / review-pack purchases by a first-time
        // learner whose scaffold was Draft and never ran the plan block. Guarded
        // so it is a no-op for Active/Cancelled/Frozen subscriptions. Both pending
        // conjuncts are load-bearing: without them we would activate an order an
        // admin has not yet verified/fulfilled. Add-on-only AI packages (Products
        // 30-47) keep instant activation per Flow B.
        if ((subscription.Status == SubscriptionStatus.Pending || subscription.Status == SubscriptionStatus.Draft)
            && !string.Equals(subscription.FulfilmentStatus, FulfilmentStatuses.PendingManual, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(subscription.FulfilmentStatus, FulfilmentStatuses.PendingVerification, StringComparison.OrdinalIgnoreCase))
        {
            SubscriptionStateMachine.Transition(subscription, SubscriptionStatus.Active, "checkout_completed");
        }
        if (subscription.Status == SubscriptionStatus.Active
            && string.Equals(transaction.TransactionType, "subscription_payment", StringComparison.OrdinalIgnoreCase))
        {
            var replacedSubscriptions = await db.Subscriptions
                .Where(row => row.UserId == subscription.UserId
                    && row.Id != subscription.Id
                    && SubscriptionStateMachine.CurrentOwnershipStatuses.Contains(row.Status))
                .ToListAsync(ct);
            foreach (var replaced in replacedSubscriptions)
            {
                SubscriptionStateMachine.Transition(
                    replaced,
                    SubscriptionStatus.Cancelled,
                    "replaced_by_verified_plan_purchase");
                replaced.ChangedAt = now;
            }
        }

        foreach (var item in quoteResponse.Items.Where(x => string.Equals(x.Kind, "addon", StringComparison.OrdinalIgnoreCase)))
        {
            var addOn = catalogSnapshot?.AddOns.FirstOrDefault(snapshot => string.Equals(snapshot.Code, item.Code, StringComparison.OrdinalIgnoreCase));
            if (addOn is null)
            {
                var liveAddOn = await FindBillingAddOnAsync(item.Code, ct);
                if (liveAddOn is null)
                {
                    continue;
                }

                addOn = new BillingQuoteAddOnSnapshot
                {
                    Code = liveAddOn.Code,
                    Name = liveAddOn.Name,
                    Price = liveAddOn.Price,
                    Currency = liveAddOn.Currency,
                    Interval = liveAddOn.Interval,
                    IsRecurring = liveAddOn.IsRecurring,
                    DurationDays = liveAddOn.DurationDays,
                    GrantCredits = liveAddOn.GrantCredits,
                    GrantEntitlementsJson = liveAddOn.GrantEntitlementsJson
                };
            }

            var existingItem = await db.SubscriptionItems.FirstOrDefaultAsync(
                x => x.SubscriptionId == subscription.Id
                     && x.ItemCode == addOn.Code
                     && x.QuoteId == quote.Id,
                ct);

            if (existingItem is null)
            {
                // Resolve the immutable add-on version id once (snapshot id, else the
                // quote's add-on version map) — reused for the SubscriptionItem row
                // and the entitlement grant below.
                var resolvedAddOnVersionId = addOn.VersionId
                    ?? (addOnVersionIds.TryGetValue(addOn.Code, out var mappedAddOnVersionId) ? mappedAddOnVersionId : null);

                db.SubscriptionItems.Add(new SubscriptionItem
                {
                    Id = TruncateIdentifier($"subitem-{Guid.NewGuid():N}"),
                    SubscriptionId = subscription.Id,
                    ItemCode = addOn.Code,
                    ItemType = addOn.IsRecurring ? "recurring_addon" : "addon",
                    AddOnVersionId = resolvedAddOnVersionId,
                    Quantity = Math.Max(1, item.Quantity),
                    Status = SubscriptionItemStatus.Active,
                    StartsAt = now,
                    EndsAt = addOn.DurationDays > 0 ? now.AddDays(addOn.DurationDays) : null,
                    QuoteId = quote.Id,
                    CheckoutSessionId = transaction.GatewayTransactionId,
                    CreatedAt = now,
                    UpdatedAt = now
                });

                // Mocks Module Phase 8b — record an audit + billing-event trail
                // when an add-on whose GrantEntitlementsJson contains mock-type
                // entitlements is first applied to the subscription. The existing
                // `MockEntitlementService` already derives "granted" totals
                // dynamically from active SubscriptionItems joined to the
                // BillingAddOn catalog, so no separate ledger insert is needed
                // for grants. The audit/event emission here gives ops a
                // searchable trail of "mock_entitlements_granted" signals and is
                // idempotent on (BillingAddOnId, UserId, paymentTransactionId)
                // by virtue of running only inside this `existingItem == null`
                // branch.
                await RecordMockEntitlementGrantsAsync(
                    transaction,
                    quote,
                    subscription.Id,
                    addOn.Code,
                    Math.Max(1, item.Quantity),
                    now,
                    ct);

                // Provision the non-AI add-on entitlements (writing assessments,
                // speaking sessions, Tutor Book unlock). The add-on snapshot
                // (`addOn`) only carries AI/credit fields, so read the granted
                // letters/sessions/kind from the immutable BillingAddOnVersion
                // locked to this purchase and fall back to the live BillingAddOn.
                // AI credits are intentionally NOT touched here — they are granted
                // separately below via the AI-credit ledger, so
                // ApplyAddOnEntitlements must NOT re-touch AiCreditsRemaining. This
                // sits strictly inside the `existingItem is null` branch so a
                // replayed/duplicate completion never double-grants.
                var addOnVersion = string.IsNullOrWhiteSpace(resolvedAddOnVersionId)
                    ? null
                    : await db.BillingAddOnVersions.AsNoTracking()
                        .FirstOrDefaultAsync(v => v.Id == resolvedAddOnVersionId, ct);
                // Phase 6a — resolve the add-on kind + extension days from the same
                // version/live row used for ApplyAddOnEntitlements, so the guarded
                // access-extension branch below can advance ExpiresAt.
                string resolvedAddonKind = string.Empty;
                int resolvedExtensionDays = 0;
                if (addOnVersion is not null)
                {
                    resolvedAddonKind = addOnVersion.AddonKind;
                    resolvedExtensionDays = addOnVersion.ExtensionDays;
                    if (!string.Equals(resolvedAddonKind, "tutor_book", StringComparison.OrdinalIgnoreCase))
                    {
                        SubscriptionBundleInitializer.ApplyAddOnEntitlements(subscription, addOnVersion);
                    }
                }
                else
                {
                    var liveAddOnForEntitlements = await FindBillingAddOnAsync(addOn.Code, ct);
                    if (liveAddOnForEntitlements is not null)
                    {
                        resolvedAddonKind = liveAddOnForEntitlements.AddonKind;
                        resolvedExtensionDays = liveAddOnForEntitlements.ExtensionDays;
                        if (!string.Equals(resolvedAddonKind, "tutor_book", StringComparison.OrdinalIgnoreCase))
                        {
                            SubscriptionBundleInitializer.ApplyAddOnEntitlements(subscription, liveAddOnForEntitlements);
                        }
                    }
                }

                // Phase 6a — Extend Access add-on. Strictly additive and guarded on
                // the new `access_extension` kind: a no-op for every other add-on /
                // plan purchase. Sits inside the once-only `existingItem is null`
                // guard so a replayed completion never double-extends. Pushes the
                // course expiry out from the later of (now, current expiry).
                if (string.Equals(resolvedAddonKind, "access_extension", StringComparison.Ordinal)
                    && resolvedExtensionDays > 0)
                {
                    var extensionUnits = Math.Max(1, item.Quantity);
                    var totalExtensionDays = resolvedExtensionDays * extensionUnits;
                    var baseline = subscription.ExpiresAt is { } currentExpiry && currentExpiry > now
                        ? currentExpiry
                        : now;
                    subscription.ExpiresAt = baseline.AddDays(totalExtensionDays);
                }

                if (string.Equals(resolvedAddonKind, "ai_package", StringComparison.Ordinal)
                    && aiPackageCreditService is not null)
                {
                    var liveAddOnForPackage = await FindBillingAddOnAsync(addOn.Code, ct);
                    if (liveAddOnForPackage is not null)
                    {
                        await aiPackageCreditService.GrantPackageAsync(
                            transaction.LearnerUserId,
                            liveAddOnForPackage,
                             Math.Max(1, item.Quantity),
                             transaction.GatewayTransactionId,
                             quote.Id,
                             ct,
                             AiPackageCreditSources.Addon(subscription.Id, addOn.Code),
                             now);
                    }
                }
            }

            var isAiPackageAddOn = await IsAiPackageAddOnAsync(addOn.Code, ct);
            if (addOn.GrantCredits > 0 && !isAiPackageAddOn)
            {
                var creditAmount = addOn.GrantCredits * Math.Max(1, item.Quantity);
                if (AddOnGrantsAiCredits(addOn.GrantEntitlementsJson))
                {
                    if (existingItem is null)
                    {
                        subscription.AiCreditsRemaining = checked(subscription.AiCreditsRemaining + creditAmount);
                    }

                    await CreditAiLedgerForAddOnPaymentAsync(
                        transaction.LearnerUserId,
                        addOn,
                        creditAmount,
                        quote.Id,
                        now,
                        ct);
                }
                else
                {
                    await CreditWalletForPaymentAsync(
                        transaction.LearnerUserId,
                        creditAmount,
                        "credit_purchase",
                        "addon",
                        $"{quote.Id}:{addOn.Code}",
                        $"{addOn.Name} credits",
                        ct);
                }
            }
        }

        var redemptions = await db.BillingCouponRedemptions
            .Where(x => x.QuoteId == quote.Id && x.Status == BillingRedemptionStatus.Reserved)
            .ToListAsync(ct);
        foreach (var redemption in redemptions)
        {
            redemption.Status = BillingRedemptionStatus.Applied;
            redemption.CheckoutSessionId = transaction.GatewayTransactionId;
            redemption.SubscriptionId = subscription.Id;
            redemption.CouponVersionId ??= quote.CouponVersionId;
            if (string.IsNullOrWhiteSpace(redemption.CouponId) && !string.IsNullOrWhiteSpace(quote.CouponCode))
            {
                var coupon = await FindBillingCouponAsync(quote.CouponCode, ct);
                redemption.CouponId = coupon?.Id;
            }
        }

        // Apply coupon variant side effects (trial extension, free months date shifts)
        if (couponVariantApplicator is not null && !string.IsNullOrWhiteSpace(quote.CouponCode))
        {
            var variantCoupon = await FindBillingCouponAsync(quote.CouponCode, ct);
            if (variantCoupon is not null)
            {
                couponVariantApplicator.Apply(variantCoupon, subscription);
            }
        }

        var invoiceId = TruncateIdentifier($"inv-{quote.Id}");
        // Paid-only invoice gate: a candidate-visible Paid invoice requires final
        // successful fulfilment (subscription Active). Orders parked at Pending /
        // PendingVerification / PendingManual (awaiting admin verification or
        // hand-over) mint a Pending invoice row for internal tracking only — the
        // learner surface filters to Paid, so failed/pending payments never expose
        // a downloadable invoice or trigger invoice notifications.
        // NOTE: intentionally flow-local, not via InvoiceEvidenceResolver — this
        // path derives the verdict from live fulfilment state (including rows
        // assembled above that are not yet persisted), while the resolver reads
        // committed evidence. See ManualPaymentService.ApproveAsync for the
        // resolver-routed promotion once evidence is persisted.
        var invoiceStatus = subscription.Status == SubscriptionStatus.Active ? "Paid" : "Pending";
        var existingInvoice = await db.Invoices.FirstOrDefaultAsync(x => x.Id == invoiceId, ct);
        if (existingInvoice is null)
        {
            db.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                UserId = transaction.LearnerUserId,
                Number = await AllocateInvoiceNumberAsync(transaction.LearnerUserId, invoiceId, ct),
                IssuedAt = now,
                Amount = quote.TotalAmount,
                Currency = quote.Currency,
                Status = invoiceStatus,
                Description = quoteResponse.Summary,
                PlanVersionId = quote.PlanVersionId,
                AddOnVersionIdsJson = quote.AddOnVersionIdsJson,
                CouponVersionId = quote.CouponVersionId,
                QuoteId = quote.Id,
                CheckoutSessionId = transaction.GatewayTransactionId,
                SubscriptionId = quote.SubscriptionId,
                Source = InvoiceSources.Gateway,
                ReconciledAt = now
            });
        }
        else
        {
            existingInvoice.PlanVersionId ??= quote.PlanVersionId;
            if (string.IsNullOrWhiteSpace(existingInvoice.AddOnVersionIdsJson) || existingInvoice.AddOnVersionIdsJson == "{}")
            {
                existingInvoice.AddOnVersionIdsJson = quote.AddOnVersionIdsJson;
            }
            existingInvoice.CouponVersionId ??= quote.CouponVersionId;
            existingInvoice.QuoteId ??= quote.Id;
            existingInvoice.CheckoutSessionId ??= transaction.GatewayTransactionId;
            existingInvoice.Number ??= await AllocateInvoiceNumberAsync(transaction.LearnerUserId, invoiceId, ct);
            // Replay convergence: if this completion now leaves the order Active
            // (e.g. listening-recalls auto grant), promote a previously-Pending
            // row to Paid exactly once. Never downgrade Paid → Pending.
            if (string.Equals(existingInvoice.Status, "Pending", StringComparison.OrdinalIgnoreCase)
                && string.Equals(invoiceStatus, "Paid", StringComparison.OrdinalIgnoreCase))
            {
                existingInvoice.Status = "Paid";
            }
        }

        quote.Status = BillingQuoteStatus.Completed;
        quote.CheckoutSessionId = transaction.GatewayTransactionId;

        await TryWriteGatewayReceiptAsync(transaction, quote, quoteResponse.Summary, subscription.Id, ct);

        // Mark pricing-experiment conversion if this quote was assigned to a variant.
        if (!string.IsNullOrEmpty(quote.ExperimentAssignmentId))
        {
            var assignment = await db.PricingExperimentAssignments
                .FirstOrDefaultAsync(a => a.Id == quote.ExperimentAssignmentId, ct);
            if (assignment is not null && !assignment.Converted)
            {
                assignment.Converted = true;
                assignment.ConvertedAt = now;
                assignment.ConvertedAmount = quote.TotalAmount;
            }
        }

        await AddBillingEventIfMissingAsync(new BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            SubscriptionId = subscription.Id,
            QuoteId = quote.Id,
            EventType = "checkout_completed",
            EntityType = "PaymentTransaction",
            EntityId = transaction.GatewayTransactionId,
            PayloadJson = JsonSupport.Serialize(new
            {
                quoteId = quote.Id,
                planCode = quote.PlanCode,
                items = quoteResponse.Items,
                totalAmount = quote.TotalAmount,
                currency = quote.Currency,
                gateway = transaction.Gateway
            }),
            OccurredAt = now
        }, ct);

        await RecordEventAsync(transaction.LearnerUserId, "checkout_completed", new
        {
            quoteId = quote.Id,
            gateway = transaction.Gateway,
            planCode = quote.PlanCode,
            totalAmount = quote.TotalAmount
        }, ct);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerPaymentSucceeded,
            transaction.LearnerUserId,
            "PaymentTransaction",
            transaction.GatewayTransactionId,
            now.UtcDateTime.ToString("yyyy-MM-dd"),
            new Dictionary<string, object?>
            {
                ["amount"] = quote.TotalAmount,
                ["currency"] = quote.Currency,
                ["planName"] = quote.PlanCode
            },
            ct);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerSubscriptionChanged,
            transaction.LearnerUserId,
            "Subscription",
            subscription.Id,
            now.UtcDateTime.ToString("yyyy-MM-dd"),
            new Dictionary<string, object?>
            {
                ["message"] = $"Your subscription to {quote.PlanCode} is now active.",
                ["planName"] = quote.PlanCode,
                ["status"] = "active"
            },
            ct);
    }

    private async Task MarkCheckoutFailedAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        var quote = await GetQuoteForTransactionAsync(transaction, ct);
        if (quote is null || quote.Status is BillingQuoteStatus.Completed or BillingQuoteStatus.Cancelled)
        {
            return;
        }

        quote.Status = BillingQuoteStatus.Cancelled;
        quote.CheckoutSessionId ??= transaction.GatewayTransactionId;

        var redemptions = await db.BillingCouponRedemptions
            .Where(x => x.QuoteId == quote.Id && x.Status == BillingRedemptionStatus.Reserved)
            .ToListAsync(ct);

        foreach (var redemption in redemptions)
        {
            redemption.Status = BillingRedemptionStatus.Voided;
            redemption.CheckoutSessionId = transaction.GatewayTransactionId;

            var coupon = !string.IsNullOrWhiteSpace(redemption.CouponId)
                ? await db.BillingCoupons.FirstOrDefaultAsync(x => x.Id == redemption.CouponId, ct)
                : await db.BillingCoupons.FirstOrDefaultAsync(x => x.Code == redemption.CouponCode, ct);
            if (coupon is not null && coupon.RedemptionCount > 0)
            {
                coupon.RedemptionCount -= 1;
                coupon.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await AddBillingEventIfMissingAsync(new BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            QuoteId = quote.Id,
            EventType = "checkout_failed",
            EntityType = "PaymentTransaction",
            EntityId = transaction.GatewayTransactionId,
            PayloadJson = JsonSupport.Serialize(new
            {
                quoteId = quote.Id,
                gateway = transaction.Gateway,
                totalAmount = quote.TotalAmount,
                currency = quote.Currency
            }),
            OccurredAt = DateTimeOffset.UtcNow
        }, ct);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerPaymentFailed,
            transaction.LearnerUserId,
            "PaymentTransaction",
            transaction.GatewayTransactionId,
            DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-dd"),
            new Dictionary<string, object?>
            {
                ["amount"] = quote.TotalAmount,
                ["currency"] = quote.Currency,
                ["message"] = "Your payment could not be processed. Update your billing details to avoid subscription interruption."
            },
            ct);
    }

    private async Task ApplyCheckoutRefundAsync(PaymentTransaction transaction, string refundEventId, CancellationToken ct)
    {
        var quote = await GetQuoteForTransactionAsync(transaction, ct);
        if (quote is null)
        {
            return;
        }

        var refundIdempotencyKey = $"webhook-refund:{refundEventId}";
        var existingRefund = await db.Set<OrderRefund>().AsNoTracking()
            .AnyAsync(refund => refund.IdempotencyKey == refundIdempotencyKey
                                || (refund.Gateway == transaction.Gateway
                                    && refund.GatewayRefundId == refundEventId)
                                || (refund.PaymentTransactionId == transaction.GatewayTransactionId
                                    && refund.Status == "succeeded"
                                    && refund.RefundType == "full"),
                ct);
        if (existingRefund)
        {
            return;
        }

        var reversedWalletCredits = await ReverseCheckoutWalletCreditsAsync(transaction, ct);
        var reversedEntitlements = await ReverseCheckoutEntitlementsAsync(transaction, ct);

        var subscription = await db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == transaction.LearnerUserId, ct);
        var reversedAiCredits = false;

        if (subscription is not null)
        {
            var purchaseEntries = await db.AiCreditLedger.AsNoTracking()
                .Where(entry => entry.UserId == transaction.LearnerUserId
                                && entry.Source == AiCreditSource.Purchase
                                && entry.TokensDelta > 0
                                && entry.ReferenceId != null
                                && (entry.ReferenceId.StartsWith("addon:" + quote.Id + ":")
                                    || entry.ReferenceId.StartsWith("plan:" + quote.Id + ":")))
                .ToListAsync(ct);

            foreach (var purchase in purchaseEntries)
            {
                var reversalReferenceId = AiPackageCreditSources.RefundReference(purchase.ReferenceId!);
                if (reversalReferenceId is null) continue;
                var alreadyReversed = await db.AiCreditLedger.AsNoTracking()
                    .AnyAsync(entry => entry.UserId == transaction.LearnerUserId
                                       && entry.Source == AiCreditSource.AdminAdjustment
                                       && entry.ReferenceId == reversalReferenceId,
                        ct);
                if (alreadyReversed) continue;

                subscription.AiCreditsRemaining = Math.Max(0, subscription.AiCreditsRemaining - purchase.TokensDelta);
                db.AiCreditLedger.Add(new AiCreditLedgerEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = transaction.LearnerUserId,
                    TokensDelta = -purchase.TokensDelta,
                    CostDeltaUsd = 0m,
                    Source = AiCreditSource.AdminAdjustment,
                    Description = "Refund reversal for AI grading credits",
                    ReferenceId = reversalReferenceId,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                reversedAiCredits = true;

                await AddBillingEventIfMissingAsync(new BillingEvent
                {
                    Id = $"bill-evt-{Guid.NewGuid():N}",
                    UserId = transaction.LearnerUserId,
                    SubscriptionId = subscription.Id,
                    QuoteId = quote.Id,
                    EventType = "ai_package_credits_refunded",
                    EntityType = "PaymentWebhookEvent",
                    EntityId = $"{refundEventId}:{reversalReferenceId}",
                    PayloadJson = JsonSupport.Serialize(new
                    {
                        paymentTransactionId = transaction.GatewayTransactionId,
                        sourceReferenceId = purchase.ReferenceId,
                        creditsReversed = purchase.TokensDelta,
                        referenceId = reversalReferenceId
                    }),
                    OccurredAt = DateTimeOffset.UtcNow,
                }, ct);
            }
        }

        db.Set<OrderRefund>().Add(new OrderRefund
        {
            Id = Guid.NewGuid(),
            PaymentTransactionId = transaction.GatewayTransactionId,
            LearnerUserId = transaction.LearnerUserId,
            Gateway = transaction.Gateway,
            GatewayRefundId = refundEventId,
            IdempotencyKey = refundIdempotencyKey,
            RefundType = "full",
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            Status = "succeeded",
            Reason = "gateway_webhook",
            RequestedByAdminId = null,
            RequestedByAdminName = null,
            ReversedWalletCredits = reversedWalletCredits,
            ReversedEntitlements = reversedEntitlements || reversedAiCredits,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await AddBillingEventIfMissingAsync(new BillingEvent
        {
            Id = $"bill-evt-refund-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            SubscriptionId = subscription?.Id,
            QuoteId = quote.Id,
            EventType = "refund_full_received",
            EntityType = nameof(OrderRefund),
            EntityId = refundIdempotencyKey,
            PayloadJson = JsonSupport.Serialize(new
            {
                paymentTransactionId = transaction.GatewayTransactionId,
                amount = transaction.Amount,
                currency = transaction.Currency,
                reversedWalletCredits,
                reversedEntitlements,
                reversedAiCredits
            }),
            OccurredAt = DateTimeOffset.UtcNow,
        }, ct);
    }

    private async Task ApplyPartialCheckoutRefundAsync(PaymentTransaction transaction, PaymentWebhookEvent webhookEvent, string? gatewayObjectId, CancellationToken ct)
    {
        var parsedAmount = ReadRefundAmountFromWebhook(webhookEvent.PayloadJson);
        if (parsedAmount is null || parsedAmount.Value.Amount <= 0m || parsedAmount.Value.Amount >= transaction.Amount)
        {
            return;
        }

        var refundIdempotencyKey = $"webhook-refund:{webhookEvent.Id:N}";
        var gatewayRefundId = webhookEvent.GatewayEventId;

        var existingRefund = await db.Set<OrderRefund>().AsNoTracking()
            .AnyAsync(refund => refund.IdempotencyKey == refundIdempotencyKey
                                || (refund.Gateway == transaction.Gateway && refund.GatewayRefundId == gatewayRefundId),
                ct);
        if (existingRefund)
        {
            return;
        }

        var alreadyRefunded = await db.Set<OrderRefund>().AsNoTracking()
            .Where(refund => refund.PaymentTransactionId == transaction.GatewayTransactionId
                             && refund.Status != "failed"
                             && refund.Status != "reversed")
            .SumAsync(refund => (decimal?)refund.Amount, ct) ?? 0m;
        var remaining = Math.Max(0m, transaction.Amount - alreadyRefunded);
        var refundAmount = parsedAmount.Value.IsCumulative
            ? parsedAmount.Value.Amount - alreadyRefunded
            : parsedAmount.Value.Amount;
        refundAmount = Math.Min(refundAmount, remaining);
        if (refundAmount <= 0m)
        {
            return;
        }

        db.Set<OrderRefund>().Add(new OrderRefund
        {
            Id = Guid.NewGuid(),
            PaymentTransactionId = transaction.GatewayTransactionId,
            LearnerUserId = transaction.LearnerUserId,
            Gateway = transaction.Gateway,
            GatewayRefundId = gatewayRefundId,
            IdempotencyKey = refundIdempotencyKey,
            RefundType = "partial",
            Amount = refundAmount,
            Currency = transaction.Currency,
            Status = "succeeded",
            Reason = "gateway_webhook",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await AddBillingEventIfMissingAsync(new BillingEvent
        {
            Id = $"bill-evt-refund-partial-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            QuoteId = transaction.QuoteId,
            EventType = "refund_partial_received",
            EntityType = nameof(OrderRefund),
            EntityId = refundIdempotencyKey,
            PayloadJson = JsonSupport.Serialize(new
            {
                paymentTransactionId = transaction.GatewayTransactionId,
                amount = refundAmount,
                currency = transaction.Currency,
                gatewayRefundId
            }),
            OccurredAt = DateTimeOffset.UtcNow,
        }, ct);
    }

    private async Task<bool> ReverseCheckoutWalletCreditsAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        var entries = await db.WalletTransactions
            .Where(w => w.Amount > 0
                        && ((w.ReferenceType == "payment" && w.ReferenceId == transaction.GatewayTransactionId)
                            || (transaction.QuoteId != null
                                && ((w.ReferenceType == "subscription" && w.ReferenceId == transaction.QuoteId)
                                    || (w.ReferenceType == "addon" && w.ReferenceId != null && w.ReferenceId.StartsWith(transaction.QuoteId + ":"))))))
            .ToListAsync(ct);
        if (entries.Count == 0) return false;

        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == transaction.LearnerUserId, ct);
        if (wallet is null) return false;

        var totalReverse = entries.Sum(e => e.Amount);
        if (totalReverse <= 0) return false;
        if (wallet.CreditBalance < totalReverse)
        {
            throw new InvalidOperationException(
                $"Cannot reverse {totalReverse} wallet credits because only {wallet.CreditBalance} credits remain.");
        }

        wallet.CreditBalance -= totalReverse;
        wallet.LastUpdatedAt = DateTimeOffset.UtcNow;
        db.WalletTransactions.Add(new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            TransactionType = "refund",
            Amount = -totalReverse,
            BalanceAfter = wallet.CreditBalance,
            ReferenceType = "payment",
            ReferenceId = transaction.GatewayTransactionId,
            Description = $"Reversed by refund of payment {transaction.GatewayTransactionId}",
            CreatedBy = "system",
            CreatedAt = DateTimeOffset.UtcNow
        });
        return true;
    }

    private async Task<bool> ReverseCheckoutEntitlementsAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        var changed = false;
        var items = await db.SubscriptionItems
            .Where(i => i.Status == SubscriptionItemStatus.Active
                        && (i.CheckoutSessionId == transaction.GatewayTransactionId
                            || (transaction.QuoteId != null && i.QuoteId == transaction.QuoteId)))
            .ToListAsync(ct);
        foreach (var item in items)
        {
            item.Status = SubscriptionItemStatus.Cancelled;
            item.EndsAt = DateTimeOffset.UtcNow;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            changed = true;

            // Phase 6a — symmetric reversal of an Extend Access add-on. Guarded on
            // the `access_extension` kind, so it is a no-op for every other add-on.
            // Subtracts the granted days from ExpiresAt but never pulls it below
            // now. The grant flow has no other symmetric entitlement reversal, so
            // this is the only place the extension is undone.
            var reverseExtensionDays = 0;
            if (!string.IsNullOrWhiteSpace(item.AddOnVersionId))
            {
                var ver = await db.BillingAddOnVersions.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == item.AddOnVersionId, ct);
                if (ver is not null && string.Equals(ver.AddonKind, "access_extension", StringComparison.Ordinal))
                {
                    reverseExtensionDays = ver.ExtensionDays;
                }
            }
            if (reverseExtensionDays <= 0)
            {
                var liveAddOn = await FindBillingAddOnAsync(item.ItemCode, ct);
                if (liveAddOn is not null && string.Equals(liveAddOn.AddonKind, "access_extension", StringComparison.Ordinal))
                {
                    reverseExtensionDays = liveAddOn.ExtensionDays;
                }
            }
            if (reverseExtensionDays > 0)
            {
                var now = DateTimeOffset.UtcNow;
                var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == item.SubscriptionId, ct);
                if (sub is not null && sub.ExpiresAt is { } currentExpiry)
                {
                    var totalDays = reverseExtensionDays * Math.Max(1, item.Quantity);
                    var reduced = currentExpiry.AddDays(-totalDays);
                    sub.ExpiresAt = reduced < now ? now : reduced;
                }
            }
        }

        if (string.Equals(transaction.TransactionType, "subscription_payment", StringComparison.OrdinalIgnoreCase))
        {
            var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == transaction.LearnerUserId, ct);
            if (sub is not null && sub.Status == SubscriptionStatus.Active)
            {
                SubscriptionStateMachine.Transition(sub, SubscriptionStatus.Cancelled, "payment_refund_full");
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Mocks Module Phase 8b — when a paid <see cref="BillingAddOn"/> grants
    /// per-mock-type credits via its <see cref="BillingAddOn.GrantEntitlementsJson"/>
    /// catalog field, emit one <see cref="AuditEvent"/> per mock-type bucket
    /// (action <c>mock_entitlements_granted</c>) and one
    /// <see cref="BillingEvent"/> aggregating all granted buckets. Both writes
    /// are idempotent on (BillingAddOnId, UserId, paymentTransactionId) — the
    /// audit guard checks for an existing row with the same resource id and
    /// payload, and the billing-event guard piggybacks on
    /// <see cref="AddBillingEventIfMissingAsync"/>.
    /// </summary>
    private async Task RecordMockEntitlementGrantsAsync(
        PaymentTransaction transaction,
        BillingQuote quote,
        string subscriptionId,
        string addOnCode,
        int quantity,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var liveAddOn = await FindBillingAddOnAsync(addOnCode, ct);
        if (liveAddOn is null || string.IsNullOrWhiteSpace(liveAddOn.GrantEntitlementsJson))
        {
            return;
        }

        Dictionary<string, JsonElement>? grantMap;
        try
        {
            grantMap = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                liveAddOn.GrantEntitlementsJson, JsonSupport.Options);
        }
        catch (JsonException)
        {
            return;
        }
        if (grantMap is null || grantMap.Count == 0)
        {
            return;
        }

        var grantedBuckets = new List<(string MockType, int Count)>();
        foreach (var (key, value) in grantMap)
        {
            var ledgerType = MockEntitlementKeys.LedgerTypeForGrantKey(key);
            if (ledgerType is null) continue;
            var perUnit = ReadGrantCount(value);
            if (perUnit <= 0) continue;
            grantedBuckets.Add((ledgerType, perUnit * Math.Max(1, quantity)));
        }

        if (grantedBuckets.Count == 0)
        {
            return;
        }

        // Idempotent audit emission — guard by checking for an existing
        // (Action, ResourceId, ActorId) tuple recorded for this txn.
        var paymentTxnId = transaction.GatewayTransactionId;
        var alreadyAudited = await db.AuditEvents.AnyAsync(a =>
            a.Action == "mock_entitlements_granted"
            && a.ActorId == transaction.LearnerUserId
            && a.ResourceType == "BillingAddOn"
            && a.ResourceId == liveAddOn.Id
            && a.Details != null
            && a.Details.Contains(paymentTxnId), ct);

        if (!alreadyAudited)
        {
            foreach (var bucket in grantedBuckets)
            {
                db.AuditEvents.Add(new AuditEvent
                {
                    Id = $"AUD-{Guid.NewGuid():N}",
                    OccurredAt = occurredAt,
                    ActorId = transaction.LearnerUserId,
                    ActorName = $"system:billing:{transaction.Gateway}",
                    Action = "mock_entitlements_granted",
                    ResourceType = "BillingAddOn",
                    ResourceId = liveAddOn.Id,
                    Details = JsonSupport.Serialize(new
                    {
                        paymentTransactionId = paymentTxnId,
                        mockType = bucket.MockType,
                        count = bucket.Count,
                        addOnId = liveAddOn.Id,
                        addOnCode = liveAddOn.Code,
                        quantity,
                        subscriptionId,
                        quoteId = quote.Id,
                        gateway = transaction.Gateway
                    })
                });
            }
        }

        await AddBillingEventIfMissingAsync(new BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            SubscriptionId = subscriptionId,
            QuoteId = quote.Id,
            EventType = "mock_entitlements_granted",
            EntityType = "BillingAddOn",
            EntityId = liveAddOn.Id,
            PayloadJson = JsonSupport.Serialize(new
            {
                paymentTransactionId = paymentTxnId,
                addOnId = liveAddOn.Id,
                addOnCode = liveAddOn.Code,
                quantity,
                buckets = grantedBuckets.Select(b => new { mockType = b.MockType, count = b.Count }).ToArray()
            }),
            OccurredAt = occurredAt
        }, ct);
    }

    /// <summary>
    /// Reads a non-negative int count from a JSON element that may be a
    /// number, numeric string, or boolean (true=1). Used by the Phase 8b
    /// audit pipeline so it stays tolerant of the same loose JSON shapes
    /// already accepted by <see cref="MockEntitlementService"/>.
    /// </summary>
    private static int ReadGrantCount(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var i) => Math.Max(0, i),
            JsonValueKind.String when int.TryParse(element.GetString(), out var s) => Math.Max(0, s),
            JsonValueKind.True => 1,
            _ => 0,
        };
    }

    private async Task AddBillingEventIfMissingAsync(BillingEvent billingEvent, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(billingEvent.EntityId))
        {
            var exists = await db.BillingEvents.AnyAsync(x =>
                x.EventType == billingEvent.EventType
                && x.EntityType == billingEvent.EntityType
                && x.EntityId == billingEvent.EntityId
                && x.UserId == billingEvent.UserId
                && x.QuoteId == billingEvent.QuoteId,
                ct);

            if (exists)
            {
                return;
            }
        }

        db.BillingEvents.Add(billingEvent);
    }

    private async Task CreditWalletForPaymentAsync(
        string userId,
        int amount,
        string transactionType,
        string referenceType,
        string referenceId,
        string description,
        CancellationToken ct)
    {
        if (amount <= 0)
        {
            return;
        }

        var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId, ct);
        var existing = await db.WalletTransactions.FirstOrDefaultAsync(
            x => x.WalletId == wallet.Id
                 && x.TransactionType == transactionType
                 && x.ReferenceType == referenceType
                 && x.ReferenceId == referenceId,
            ct);

        if (existing is not null)
        {
            return;
        }

        wallet.CreditBalance += amount;
        wallet.LastUpdatedAt = DateTimeOffset.UtcNow;

        db.WalletTransactions.Add(new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            TransactionType = transactionType,
            Amount = amount,
            BalanceAfter = wallet.CreditBalance,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Description = description,
            CreatedBy = "system",
            CreatedAt = wallet.LastUpdatedAt
        });
    }

    private async Task CreditAiLedgerForAddOnPaymentAsync(
        string userId,
        BillingQuoteAddOnSnapshot addOn,
        int amount,
        string quoteId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (amount <= 0 || !AddOnGrantsAiCredits(addOn.GrantEntitlementsJson))
        {
            return;
        }

        var referenceId = $"addon:{quoteId}:{addOn.Code}";
        var existing = await db.AiCreditLedger.AsNoTracking().AnyAsync(
            entry => entry.UserId == userId
                     && entry.Source == AiCreditSource.Purchase
                     && entry.ReferenceId == referenceId,
            ct);
        if (existing)
        {
            return;
        }

        db.AiCreditLedger.Add(new AiCreditLedgerEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            TokensDelta = amount,
            CostDeltaUsd = 0m,
            Source = AiCreditSource.Purchase,
            Description = $"{addOn.Name} AI grading credits",
            ReferenceId = referenceId,
            ExpiresAt = addOn.DurationDays > 0 ? now.AddDays(addOn.DurationDays) : null,
            CreatedAt = now,
        });
    }

    private async Task<bool> CreditAiLedgerForPlanPaymentAsync(
        string userId,
        BillingQuotePlanSnapshot plan,
        int amount,
        string quoteId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (amount <= 0) return false;

        var referenceId = $"plan:{quoteId}:{plan.Code}";
        var existing = await db.AiCreditLedger.AsNoTracking().AnyAsync(
            entry => entry.UserId == userId
                     && entry.Source == AiCreditSource.Purchase
                     && entry.ReferenceId == referenceId,
            ct);
        if (existing) return false;

        db.AiCreditLedger.Add(new AiCreditLedgerEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            TokensDelta = amount,
            CostDeltaUsd = 0m,
            Source = AiCreditSource.Purchase,
            Description = $"{plan.Name} bundled AI grading credits",
            ReferenceId = referenceId,
            ExpiresAt = plan.DurationMonths > 0 ? now.AddMonths(plan.DurationMonths) : null,
            CreatedAt = now,
        });

        return true;
    }

    private static bool IsFullRefundWebhook(string? safePayloadJson, decimal transactionAmount)
    {
        if (transactionAmount <= 0m || string.IsNullOrWhiteSpace(safePayloadJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(safePayloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("object", out var obj)
                && obj.ValueKind == JsonValueKind.Object)
            {
                var refundedMinor = ReadLong(obj, "amount_refunded");
                var amountMinor = ReadLong(obj, "amount") ?? ReadLong(obj, "amount_total");
                if (refundedMinor is not null && amountMinor is not null && amountMinor > 0)
                {
                    return refundedMinor >= amountMinor;
                }
            }

            if (root.TryGetProperty("resource", out var resource)
                && resource.ValueKind == JsonValueKind.Object
                && resource.TryGetProperty("amount", out var amount)
                && amount.ValueKind == JsonValueKind.Object
                && amount.TryGetProperty("value", out var valueElement)
                && decimal.TryParse(valueElement.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var refundedMajor))
            {
                return refundedMajor >= transactionAmount;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;

        static long? ReadLong(JsonElement obj, string propertyName)
            => obj.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt64(out var number)
                ? number
                : null;
    }

    private static RefundWebhookAmount? ReadRefundAmountFromWebhook(string? safePayloadJson)
    {
        if (string.IsNullOrWhiteSpace(safePayloadJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(safePayloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("object", out var obj)
                && obj.ValueKind == JsonValueKind.Object)
            {
                var minorAmount = ReadLong(obj, "amount_refunded") ?? ReadLong(obj, "amount");
                if (minorAmount is not null)
                {
                    return new RefundWebhookAmount(
                        decimal.Round(minorAmount.Value / 100m, 2, MidpointRounding.AwayFromZero),
                        obj.TryGetProperty("amount_refunded", out _));
                }
            }

            if (root.TryGetProperty("resource", out var resource)
                && resource.ValueKind == JsonValueKind.Object
                && resource.TryGetProperty("amount", out var amount)
                && amount.ValueKind == JsonValueKind.Object
                && amount.TryGetProperty("value", out var valueElement)
                && decimal.TryParse(valueElement.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var majorAmount))
            {
                return new RefundWebhookAmount(majorAmount, IsCumulative: false);
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;

        static long? ReadLong(JsonElement obj, string propertyName)
            => obj.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt64(out var number)
                ? number
                : null;
    }

    private readonly record struct RefundWebhookAmount(decimal Amount, bool IsCumulative);

    private static bool AddOnGrantsAiCredits(string? grantEntitlementsJson)
    {
        if (string.IsNullOrWhiteSpace(grantEntitlementsJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(grantEntitlementsJson);
                 return doc.RootElement.ValueKind == JsonValueKind.Object
                     && TryReadAiCreditGrantValue(doc.RootElement, out var value)
                     && value > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadAiCreditGrantValue(JsonElement root, out int value)
    {
        if (root.TryGetProperty("ai_credits", out var aiCredits) && aiCredits.TryGetInt32(out value))
        {
            return true;
        }

        if (root.TryGetProperty("reviewCredits", out var reviewCredits) && reviewCredits.TryGetInt32(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private async Task<BillingQuote?> GetQuoteForTransactionAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(transaction.QuoteId))
        {
            var quoteByTransactionRef = await db.BillingQuotes.FirstOrDefaultAsync(x => x.Id == transaction.QuoteId, ct);
            if (quoteByTransactionRef is not null)
            {
                return quoteByTransactionRef;
            }
        }

        var metadata = ReadObject(JsonSupport.Deserialize<object?>(transaction.MetadataJson ?? "{}", null));
        var quoteId = ReadString(metadata?.GetValueOrDefault("quoteId"))
            ?? ReadString(metadata?.GetValueOrDefault("quote_id"));

        if (!string.IsNullOrWhiteSpace(quoteId))
        {
            var quoteById = await db.BillingQuotes.FirstOrDefaultAsync(x => x.Id == quoteId, ct);
            if (quoteById is not null)
            {
                return quoteById;
            }
        }

        return await db.BillingQuotes.FirstOrDefaultAsync(x => x.CheckoutSessionId == transaction.GatewayTransactionId, ct);
    }
}
