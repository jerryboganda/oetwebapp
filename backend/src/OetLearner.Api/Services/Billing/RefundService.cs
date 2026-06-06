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

    public RefundService(LearnerDbContext db, IPaymentGatewayProvider gateways)
    {
        _db = db;
        _gateways = gateways;
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
            if (!Idempotent)
            {
                refund.Status = "failed";
                refund.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

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

        // End any subscription items that were activated by this transaction.
        var items = await _db.SubscriptionItems
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
        }

        // For subscription payments, downgrade the active subscription.
        if (string.Equals(transaction.TransactionType, "subscription_payment", StringComparison.OrdinalIgnoreCase))
        {
            var sub = await _db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == transaction.LearnerUserId, ct);
            if (sub is not null && sub.Status == SubscriptionStatus.Active)
            {
                SubscriptionStateMachine.Transition(sub, SubscriptionStatus.Cancelled, "payment_refund_full");
                changed = true;
            }
        }

        return changed;
    }

    private async Task<bool> ReverseAiPackageCreditsAsync(PaymentTransaction transaction, string refundId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(transaction.QuoteId)) return false;

        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == transaction.LearnerUserId, ct);
        if (subscription is null) return false;

        var purchaseEntries = await _db.AiCreditLedger.AsNoTracking()
            .Where(entry => entry.UserId == transaction.LearnerUserId
                            && entry.Source == AiCreditSource.Purchase
                            && entry.TokensDelta > 0
                            && entry.ReferenceId != null
                            && (entry.ReferenceId.StartsWith("addon:" + transaction.QuoteId + ":")
                                || entry.ReferenceId.StartsWith("plan:" + transaction.QuoteId + ":")))
            .ToListAsync(ct);
        var changed = false;
        var reversedTotal = 0;
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

            subscription.AiCreditsRemaining = Math.Max(0, subscription.AiCreditsRemaining - purchase.TokensDelta);
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
                SubscriptionId = subscription.Id,
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
