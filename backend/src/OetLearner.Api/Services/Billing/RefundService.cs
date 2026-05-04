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
    private readonly PaymentGatewayService _gateways;

    public RefundService(LearnerDbContext db, PaymentGatewayService gateways)
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

        // Idempotency short-circuit: replays of the same key return the same outcome.
        var existing = await _db.Set<OrderRefund>()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null)
        {
            var remaining = await ComputeRemainingAuthorisedAsync(existing.PaymentTransactionId, existing.Currency, ct);
            return new RefundResponse(
                existing.Id,
                existing.Status,
                existing.RefundType,
                existing.Amount,
                remaining,
                existing.ReversedWalletCredits,
                existing.ReversedEntitlements,
                Idempotent: true);
        }

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

        var gateway = _gateways.GetGateway(transaction.Gateway);
        var providerResult = await gateway.ProcessRefundAsync(
            transaction.GatewayTransactionId,
            request.Amount,
            transaction.Currency,
            request.Reason ?? "requested_by_customer",
            ct);
        var providerSucceeded = string.Equals(providerResult.Status, "succeeded", StringComparison.OrdinalIgnoreCase);

        var refund = new OrderRefund
        {
            Id = Guid.NewGuid(),
            PaymentTransactionId = transaction.GatewayTransactionId,
            LearnerUserId = transaction.LearnerUserId,
            Gateway = transaction.Gateway,
            GatewayRefundId = providerResult.RefundId,
            IdempotencyKey = request.IdempotencyKey,
            RefundType = isFull ? "full" : "partial",
            Amount = request.Amount,
            Currency = transaction.Currency,
            Status = providerSucceeded ? "succeeded" : "pending",
            Reason = Truncate(request.Reason, 64),
            AdminNote = Truncate(request.AdminNote, 1024),
            RequestedByAdminId = Truncate(request.AdminId, 64),
            RequestedByAdminName = Truncate(request.AdminName, 128),
            CreatedAt = now,
            UpdatedAt = now
        };

        if (isFull && providerSucceeded)
        {
            refund.ReversedWalletCredits = await ReverseWalletCreditsAsync(transaction, ct);
            refund.ReversedEntitlements = await ReverseEntitlementsAsync(transaction, ct);
            transaction.Status = "refunded";
            transaction.UpdatedAt = now;
        }

        _db.Set<OrderRefund>().Add(refund);
        _db.BillingEvents.Add(new BillingEvent
        {
            Id = $"bill-evt-refund-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            EventType = isFull ? "refund_full_issued" : "refund_partial_issued",
            EntityType = nameof(OrderRefund),
            EntityId = refund.Id.ToString(),
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

        return new RefundResponse(
            refund.Id,
            refund.Status,
            refund.RefundType,
            refund.Amount,
            transaction.Amount - newTotal,
            refund.ReversedWalletCredits,
            refund.ReversedEntitlements,
            Idempotent: false);
    }

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
            .Where(i => i.CheckoutSessionId == transaction.GatewayTransactionId
                        && i.Status == SubscriptionItemStatus.Active)
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

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }
}
