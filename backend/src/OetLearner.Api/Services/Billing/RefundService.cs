using System.Data;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

public sealed record RefundRequest(
    string PaymentTransactionId,
    decimal Amount,
    string Reason,
    string IdempotencyKey,
    string? AdminId = null,
    string? AdminName = null,
    string? AdminNote = null);

public sealed record RefundResponse(
    Guid RefundId,
    string Status,
    string RefundType,
    decimal Amount,
    decimal RemainingAuthorisedAmount,
    bool ReversedWalletCredits,
    bool ReversedEntitlements,
    bool Idempotent);

/// <summary>
/// Owns the lifecycle of <see cref="OrderRefund"/>. Validates the parent payment
/// transaction, prevents over-refunding, and on a fully refunded transaction
/// reverses any wallet credits and freezes/cancels entitlements that the
/// original payment had granted. Idempotent on the supplied key.
/// </summary>
public sealed class RefundService
{
    private readonly LearnerDbContext _db;
    private readonly IPaymentGatewayProvider _gateways;
    private readonly IAiPackageCreditService? _aiPackageCredits;

    public RefundService(
        LearnerDbContext db,
        IPaymentGatewayProvider gateways,
        IAiPackageCreditService? aiPackageCredits = null)
    {
        _db = db;
        _gateways = gateways;
        _aiPackageCredits = aiPackageCredits;
    }

    public async Task<RefundResponse> IssueRefundAsync(RefundRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required for refunds.", nameof(request));
        }

        if (request.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Refund amount must be positive.");
        }

        // Idempotency replays return the same outcome, resuming pending local finalization when needed.
        var existing = await _db.Set<OrderRefund>()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null)
        {
            return await ResumeOrReturnExistingRefundAsync(existing, request, ct);
        }

        await using var reservationTransaction = IsInMemoryProvider(_db)
            ? null
            : await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var transaction = await _db.PaymentTransactions
            .FirstOrDefaultAsync(t => t.GatewayTransactionId == request.PaymentTransactionId, ct)
            ?? throw new InvalidOperationException($"Payment transaction '{request.PaymentTransactionId}' was not found.");

        if (!string.Equals(transaction.Status, "completed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(transaction.Status, "refunded", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refunds may only be issued against completed transactions (current status: {transaction.Status}).");
        }

        var alreadyRefunded = await _db.Set<OrderRefund>()
            .Where(r => r.PaymentTransactionId == transaction.GatewayTransactionId
                        && (r.Status == "succeeded" || r.Status == "pending"))
            .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;

        var newTotal = alreadyRefunded + request.Amount;
        if (newTotal > transaction.Amount)
        {
            throw new InvalidOperationException(
                $"Refund amount ({request.Amount}) exceeds remaining authorised amount ({transaction.Amount - alreadyRefunded}).");
        }

        var isFull = newTotal >= transaction.Amount;
        var now = DateTimeOffset.UtcNow;

        if (isFull)
        {
            await EnsureWalletCreditsCanBeReversedAsync(transaction, ct);
        }

        var refund = new OrderRefund
        {
            Id = Guid.NewGuid(),
            PaymentTransactionId = transaction.GatewayTransactionId,
            LearnerUserId = transaction.LearnerUserId,
            Gateway = transaction.Gateway,
            GatewayRefundId = $"pending:{request.IdempotencyKey}",
            IdempotencyKey = request.IdempotencyKey,
            RefundType = isFull ? "full" : "partial",
            Amount = request.Amount,
            Currency = transaction.Currency,
            Status = "pending",
            Reason = Truncate(request.Reason, 64),
            AdminNote = Truncate(request.AdminNote, 1024),
            RequestedByAdminId = Truncate(request.AdminId, 64),
            RequestedByAdminName = Truncate(request.AdminName, 128),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Set<OrderRefund>().Add(refund);
        await _db.SaveChangesAsync(ct);
        if (reservationTransaction is not null)
        {
            await reservationTransaction.CommitAsync(ct);
        }

        return await ProcessGatewayAndFinalizeAsync(
            refund,
            transaction,
            request.Reason ?? "requested_by_customer",
            Idempotent: false,
            ct);
    }

    private async Task<RefundResponse> ResumeOrReturnExistingRefundAsync(
        OrderRefund refund,
        RefundRequest request,
        CancellationToken ct)
    {
        var transaction = await _db.PaymentTransactions
            .FirstOrDefaultAsync(t => t.GatewayTransactionId == refund.PaymentTransactionId, ct);
        if (transaction is not null)
        {
            if (string.Equals(refund.Status, "pending", StringComparison.OrdinalIgnoreCase))
            {
                return await ProcessGatewayAndFinalizeAsync(
                    refund,
                    transaction,
                    refund.Reason ?? request.Reason ?? "requested_by_customer",
                    Idempotent: true,
                    ct);
            }

            if (IsFullRefund(refund)
                && string.Equals(refund.Status, "succeeded", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(transaction.Status, "refunded", StringComparison.OrdinalIgnoreCase))
            {
                await FinalizeSuccessfulFullRefundAsync(refund, transaction, ct);
            }

            if (string.Equals(refund.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                await EnsureRefundBillingEventAsync(refund, transaction, IsFullRefund(refund), DateTimeOffset.UtcNow, ct);
            }
        }

        var remaining = await ComputeRemainingAuthorisedAsync(refund.PaymentTransactionId, refund.Currency, ct);
        return ToResponse(refund, remaining, Idempotent: true);
    }

    private async Task<RefundResponse> ProcessGatewayAndFinalizeAsync(
        OrderRefund refund,
        PaymentTransaction transaction,
        string reason,
        bool Idempotent,
        CancellationToken ct)
    {
        var gateway = _gateways.GetGateway(refund.Gateway);
        RefundResult providerResult;
        try
        {
            providerResult = await gateway.ProcessRefundAsync(
                transaction.GatewayTransactionId,
                refund.Amount,
                refund.Currency,
                reason,
                refund.IdempotencyKey,
                ct);
        }
        catch
        {
            refund.Status = "pending";
            refund.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            throw;
        }

        var providerSucceeded = string.Equals(providerResult.Status, "succeeded", StringComparison.OrdinalIgnoreCase);
        var isFull = IsFullRefund(refund);
        var now = DateTimeOffset.UtcNow;

        refund.GatewayRefundId = providerResult.RefundId;
        refund.Status = providerSucceeded ? "succeeded" : "pending";
        refund.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        if (isFull && providerSucceeded)
        {
            await FinalizeSuccessfulFullRefundAsync(refund, transaction, ct);
        }

        if (providerSucceeded)
        {
            await EnsureRefundBillingEventAsync(refund, transaction, isFull, DateTimeOffset.UtcNow, ct);
        }

        var remaining = await ComputeRemainingAuthorisedAsync(refund.PaymentTransactionId, refund.Currency, ct);
        return ToResponse(refund, remaining, Idempotent);
    }

    private async Task FinalizeSuccessfulFullRefundAsync(OrderRefund refund, PaymentTransaction transaction, CancellationToken ct)
    {
        await using var finalizationTransaction = IsInMemoryProvider(_db)
            ? null
            : await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // By design, partial refunds are monetary adjustments only; AI credits
        // and other entitlements are revoked when the purchase is fully refunded.
        refund.ReversedWalletCredits = refund.ReversedWalletCredits
                                       || await ReverseWalletCreditsAsync(transaction, ct);
        var reversedEntitlements = await ReverseEntitlementsAsync(transaction, ct);
        var reversedAiCredits = await ReverseAiPackageCreditsAsync(transaction, refund.Id.ToString("N"), ct);
        refund.ReversedEntitlements = refund.ReversedEntitlements || reversedEntitlements || reversedAiCredits;
        transaction.Status = "refunded";
        transaction.UpdatedAt = DateTimeOffset.UtcNow;
        refund.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        if (finalizationTransaction is not null)
        {
            await finalizationTransaction.CommitAsync(ct);
        }

        // Every state transition ends in Recalculate: reversal zeroes lots,
        // Recalculate converges derived allowances + sweeps orphans. This was
        // the one transition missing it (remove/cancel/reactivate/status/dates
        // all recalculate); reads self-heal via GetAccess, but the window
        // between refund and next read served stale allowances.
        if (_aiPackageCredits is not null)
        {
            await _aiPackageCredits.RecalculateObjectiveAllowancesAsync(transaction.LearnerUserId, ct);
        }
    }

    private async Task EnsureRefundBillingEventAsync(
        OrderRefund refund,
        PaymentTransaction transaction,
        bool isFull,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var entityId = refund.Id.ToString();
        var eventExists = await _db.BillingEvents.AnyAsync(e =>
            e.EntityType == nameof(OrderRefund)
            && e.EntityId == entityId
            && (e.EventType == "refund_full_issued" || e.EventType == "refund_partial_issued"),
            ct);
        if (eventExists)
        {
            return;
        }

        _db.BillingEvents.Add(new BillingEvent
        {
            Id = $"bill-evt-refund-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            EventType = isFull ? "refund_full_issued" : "refund_partial_issued",
            EntityType = nameof(OrderRefund),
            EntityId = entityId,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                paymentTransactionId = transaction.GatewayTransactionId,
                amount = refund.Amount,
                currency = refund.Currency,
                reason = refund.Reason,
                adminId = refund.RequestedByAdminId,
                isFull,
                reversedWalletCredits = refund.ReversedWalletCredits,
                reversedEntitlements = refund.ReversedEntitlements
            }),
            OccurredAt = now
        });
        await _db.SaveChangesAsync(ct);
    }

    private static RefundResponse ToResponse(OrderRefund refund, decimal remaining, bool Idempotent)
        => new(
            refund.Id,
            refund.Status,
            refund.RefundType,
            refund.Amount,
            remaining,
            refund.ReversedWalletCredits,
            refund.ReversedEntitlements,
            Idempotent);

    private async Task<decimal> ComputeRemainingAuthorisedAsync(string paymentTransactionId, string currency, CancellationToken ct)
    {
        var transaction = await _db.PaymentTransactions
            .FirstOrDefaultAsync(t => t.GatewayTransactionId == paymentTransactionId, ct);
        if (transaction is null) return 0m;
        var refunded = await _db.Set<OrderRefund>()
            .Where(r => r.PaymentTransactionId == paymentTransactionId && r.Status != "failed" && r.Status != "reversed")
            .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;
        return Math.Max(0m, transaction.Amount - refunded);
    }

    private async Task<bool> ReverseWalletCreditsAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        // Find the wallet ledger entries that originated from this payment.
        var entries = await _db.WalletTransactions
            .Where(w => w.Amount > 0
                        && ((w.ReferenceType == "payment" && w.ReferenceId == transaction.GatewayTransactionId)
                            || (transaction.QuoteId != null
                                && ((w.ReferenceType == "subscription" && w.ReferenceId == transaction.QuoteId)
                                    || (w.ReferenceType == "addon" && w.ReferenceId != null && w.ReferenceId.StartsWith(transaction.QuoteId + ":"))))))
            .ToListAsync(ct);
        if (entries.Count == 0) return false;

        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == transaction.LearnerUserId, ct);
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
        _db.WalletTransactions.Add(new WalletTransaction
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

    private async Task EnsureWalletCreditsCanBeReversedAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        var entries = await _db.WalletTransactions
            .Where(w => w.Amount > 0
                        && ((w.ReferenceType == "payment" && w.ReferenceId == transaction.GatewayTransactionId)
                            || (transaction.QuoteId != null
                                && ((w.ReferenceType == "subscription" && w.ReferenceId == transaction.QuoteId)
                                    || (w.ReferenceType == "addon" && w.ReferenceId != null && w.ReferenceId.StartsWith(transaction.QuoteId + ":"))))))
            .ToListAsync(ct);
        if (entries.Count == 0)
        {
            return;
        }

        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == transaction.LearnerUserId, ct);
        var totalReverse = entries.Sum(e => e.Amount);
        if (wallet is not null && wallet.CreditBalance < totalReverse)
        {
            throw new InvalidOperationException(
                $"Cannot refund this payment until {totalReverse - wallet.CreditBalance} consumed wallet credits are reconciled.");
        }
    }

    private async Task<bool> ReverseEntitlementsAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        var changed = false;
        var quote = string.IsNullOrWhiteSpace(transaction.QuoteId)
            ? null
            : await _db.BillingQuotes.AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == transaction.QuoteId, ct);

        // End any subscription items that were activated by this transaction.
        var items = await _db.SubscriptionItems
            .Where(i => i.CheckoutSessionId == transaction.GatewayTransactionId
                        || (transaction.QuoteId != null && i.QuoteId == transaction.QuoteId))
            .ToListAsync(ct);
        foreach (var item in items)
        {
            if (item.Status != SubscriptionItemStatus.Active)
            {
                continue;
            }

            item.Status = SubscriptionItemStatus.Cancelled;
            item.EndsAt = DateTimeOffset.UtcNow;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            changed = true;
        }

        // For subscription payments, downgrade the active subscription.
        if (string.Equals(transaction.TransactionType, "subscription_payment", StringComparison.OrdinalIgnoreCase))
        {
            var subscriptionIds = items.Select(item => item.SubscriptionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(quote?.SubscriptionId))
            {
                subscriptionIds.Add(quote.SubscriptionId);
            }

            List<Subscription> subscriptions;
            if (subscriptionIds.Count > 0)
            {
                subscriptions = await _db.Subscriptions
                    .Where(subscription => subscription.UserId == transaction.LearnerUserId
                        && subscriptionIds.Contains(subscription.Id))
                    .ToListAsync(ct);
            }
            else if (!string.IsNullOrWhiteSpace(transaction.ProductId))
            {
                // No SubscriptionItems and no BillingQuote to correlate from (e.g. a
                // manually-recorded payment — ManualPaymentService sets QuoteId only when an
                // online checkout quote actually exists — or any other plan-only payment that
                // never went through the add-on/quote pipeline). Fall back to the exact plan
                // this transaction paid for, matched by PlanId, so the refund still downgrades
                // the subscription it funded without touching any of the learner's other
                // packages the way a blanket "any active subscription for this user" match
                // would.
                subscriptions = await _db.Subscriptions
                    .Where(subscription => subscription.UserId == transaction.LearnerUserId
                        && subscription.PlanId == transaction.ProductId)
                    .ToListAsync(ct);
            }
            else
            {
                subscriptions = [];
            }

            foreach (var subscription in subscriptions.Where(row => row.Status == SubscriptionStatus.Active))
            {
                SubscriptionStateMachine.Transition(subscription, SubscriptionStatus.Cancelled, "payment_refund_full");
                changed = true;
            }
        }

        return changed;
    }

    private async Task<bool> ReverseAiPackageCreditsAsync(PaymentTransaction transaction, string refundId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(transaction.QuoteId)) return false;

        // The BillingQuote row is optional enrichment here (it supplies PlanCode for the
        // authoritative AiPackageCreditService source reference, and a SubscriptionId
        // fallback). It must not gate the whole method: manually-recorded payments
        // (ManualPaymentService sets PaymentTransaction.QuoteId from the admin request even
        // when no online checkout quote — and therefore no BillingQuotes row — ever existed)
        // and other legacy flows can carry a QuoteId with no matching quote. The legacy
        // AiCreditLedger reversal below keys purely off transaction.QuoteId and the
        // SubscriptionItems it activated, both independent of this row.
        var quote = await _db.BillingQuotes.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == transaction.QuoteId, ct);

        var items = await _db.SubscriptionItems.AsNoTracking()
            .Where(item => item.CheckoutSessionId == transaction.GatewayTransactionId
                || item.QuoteId == transaction.QuoteId)
            .ToListAsync(ct);
        var subscriptionIds = items.Select(item => item.SubscriptionId)
            .Append(quote?.SubscriptionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        List<Subscription> subscriptions;
        if (subscriptionIds.Count > 0)
        {
            subscriptions = await _db.Subscriptions
                .Where(subscription => subscription.UserId == transaction.LearnerUserId
                    && subscriptionIds.Contains(subscription.Id))
                .ToListAsync(ct);
        }
        else if (string.Equals(transaction.TransactionType, "subscription_payment", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(transaction.ProductId))
        {
            // Same legacy/manual-payment fallback as ReverseEntitlementsAsync: no items and
            // no quote row to correlate from, but the transaction still names the exact plan
            // it paid for.
            subscriptions = await _db.Subscriptions
                .Where(subscription => subscription.UserId == transaction.LearnerUserId
                    && subscription.PlanId == transaction.ProductId)
                .ToListAsync(ct);
        }
        else
        {
            subscriptions = [];
        }
        if (subscriptions.Count == 0) return false;

        var planCode = quote?.PlanCode
            ?? (string.Equals(transaction.ProductType, "plan", StringComparison.OrdinalIgnoreCase) ? transaction.ProductId : null);
        var sourceReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subscription in subscriptions)
        {
            if (string.Equals(transaction.TransactionType, "subscription_payment", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(planCode))
            {
                // Same plan-key set the remove path and the orphan sweep use:
                // direct reversal covers both, the trailing Recalculate sweep
                // converges anything this misses.
                foreach (var planKey in AiPackageCreditSources.PlanKeys(subscription.Id, planCode))
                {
                    sourceReferences.Add(planKey);
                }
            }
        }

        foreach (var item in items)
        {
            sourceReferences.Add(AiPackageCreditSources.Addon(item.SubscriptionId, item.ItemCode));
        }

        var changed = false;
        var authoritativeReversed = 0;
        if (_aiPackageCredits is not null)
        {
            foreach (var sourceReference in sourceReferences)
            {
                authoritativeReversed += await _aiPackageCredits.ReverseGrantsAsync(
                    transaction.LearnerUserId,
                    sourceReference,
                    ct);
            }
            changed = authoritativeReversed > 0;
        }

        var legacyPrefixes = new HashSet<string>(StringComparer.Ordinal)
        {
            $"addon:{transaction.QuoteId}:",
            $"plan:{transaction.QuoteId}:",
        };
        foreach (var item in items)
        {
            legacyPrefixes.Add($"addon:{item.SubscriptionId}:{item.ItemCode}:");
        }

        var purchaseEntries = (await _db.AiCreditLedger.AsNoTracking()
            .Where(entry => entry.UserId == transaction.LearnerUserId
                            && entry.Source == AiCreditSource.Purchase
                            && entry.TokensDelta > 0
                            && entry.ReferenceId != null)
            .ToListAsync(ct))
            .Where(entry => legacyPrefixes.Any(prefix => entry.ReferenceId!.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
        var reversedTotal = 0;
        var legacySubscription = subscriptions.FirstOrDefault();
        foreach (var purchase in purchaseEntries)
        {
            var reversalReferenceId = BuildAiCreditRefundReference(purchase.ReferenceId!);
            if (reversalReferenceId is null) continue;
            var alreadyReversed = await _db.AiCreditLedger.AsNoTracking()
                .AnyAsync(entry => entry.UserId == transaction.LearnerUserId
                                   && entry.Source == AiCreditSource.AdminAdjustment
                                   && entry.ReferenceId == reversalReferenceId,
                    ct);
            if (alreadyReversed) continue;

            if (legacySubscription is not null)
            {
                legacySubscription.AiCreditsRemaining = Math.Max(
                    0,
                    legacySubscription.AiCreditsRemaining - purchase.TokensDelta);
            }
            _db.AiCreditLedger.Add(new AiCreditLedgerEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = transaction.LearnerUserId,
                TokensDelta = -purchase.TokensDelta,
                CostDeltaUsd = 0m,
                Source = AiCreditSource.AdminAdjustment,
                Description = "Refund reversal for AI package credits",
                ReferenceId = reversalReferenceId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            reversedTotal += purchase.TokensDelta;
            changed = true;
        }

        if (changed)
        {
            _db.BillingEvents.Add(new BillingEvent
            {
                Id = $"bill-evt-ai-refund-{Guid.NewGuid():N}",
                UserId = transaction.LearnerUserId,
                SubscriptionId = quote?.SubscriptionId
                    ?? subscriptions.Select(row => row.Id).FirstOrDefault(),
                QuoteId = transaction.QuoteId,
                EventType = "ai_package_credits_refunded",
                EntityType = nameof(OrderRefund),
                EntityId = refundId,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    paymentTransactionId = transaction.GatewayTransactionId,
                    creditsReversed = reversedTotal,
                }),
                OccurredAt = DateTimeOffset.UtcNow,
            });
        }

        return changed;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }

    private static string? BuildAiCreditRefundReference(string purchaseReferenceId)
    {
        if (purchaseReferenceId.StartsWith("addon:", StringComparison.Ordinal))
        {
            return "addon-refund:" + purchaseReferenceId["addon:".Length..];
        }

        if (purchaseReferenceId.StartsWith("plan:", StringComparison.Ordinal))
        {
            return "plan-refund:" + purchaseReferenceId["plan:".Length..];
        }

        return null;
    }

    private static bool IsFullRefund(OrderRefund refund)
        => string.Equals(refund.RefundType, "full", StringComparison.OrdinalIgnoreCase);

    private static bool IsInMemoryProvider(LearnerDbContext context)
        => context.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true;
}
