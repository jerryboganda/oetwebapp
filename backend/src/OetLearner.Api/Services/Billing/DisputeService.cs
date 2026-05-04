using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

public sealed record DisputeWebhookSignal(
    string Gateway,
    string GatewayDisputeId,
    string PaymentTransactionId,
    string NormalizedStatus,   // dispute_opened | dispute_funds_withdrawn | dispute_funds_reinstated | dispute_won | dispute_lost
    decimal? AmountDisputed,
    string? Currency,
    string? Reason);

public sealed record DisputeRecordResponse(
    Guid DisputeId,
    string Status,
    bool EntitlementsFrozen);

/// <summary>
/// Owns dispute lifecycle. On `dispute_opened` / `dispute_funds_withdrawn` the
/// learner's active subscription is frozen (status -> <see cref="SubscriptionStatus.Suspended"/>)
/// to prevent further entitlement consumption while the chargeback is open.
/// On `dispute_won` the freeze is lifted; on `dispute_lost` the subscription is
/// cancelled.
/// </summary>
public sealed class DisputeService
{
    private readonly LearnerDbContext _db;

    public DisputeService(LearnerDbContext db) { _db = db; }

    public async Task<DisputeRecordResponse> RecordSignalAsync(DisputeWebhookSignal signal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var transaction = await _db.PaymentTransactions
            .FirstOrDefaultAsync(t => t.GatewayTransactionId == signal.PaymentTransactionId, ct)
            ?? throw new InvalidOperationException($"Payment transaction '{signal.PaymentTransactionId}' was not found.");

        var existing = await _db.Set<PaymentDispute>()
            .FirstOrDefaultAsync(d => d.Gateway == signal.Gateway && d.GatewayDisputeId == signal.GatewayDisputeId, ct);
        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            existing = new PaymentDispute
            {
                Id = Guid.NewGuid(),
                PaymentTransactionId = transaction.GatewayTransactionId,
                LearnerUserId = transaction.LearnerUserId,
                Gateway = signal.Gateway,
                GatewayDisputeId = signal.GatewayDisputeId,
                Status = MapStatus(signal.NormalizedStatus),
                Reason = Truncate(signal.Reason, 64),
                AmountDisputed = signal.AmountDisputed ?? transaction.Amount,
                Currency = signal.Currency ?? transaction.Currency,
                OpenedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Set<PaymentDispute>().Add(existing);
        }
        else
        {
            existing.Status = MapStatus(signal.NormalizedStatus);
            existing.UpdatedAt = now;
        }

        var wasFrozenByThisDispute = existing.EntitlementsFrozen;

        switch (signal.NormalizedStatus)
        {
            case "dispute_funds_withdrawn":
                existing.FundsWithdrawnAt = now;
                break;
            case "dispute_won":
            case "dispute_lost":
                existing.ResolvedAt = now;
                break;
        }

        // Entitlement freeze: any non-resolved dispute freezes the subscription.
        var sub = await _db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == transaction.LearnerUserId, ct);
        if (sub is not null)
        {
            existing.SubscriptionId = sub.Id;
            switch (signal.NormalizedStatus)
            {
                case "dispute_opened":
                case "dispute_funds_withdrawn":
                    if (sub.Status == SubscriptionStatus.Active || sub.Status == SubscriptionStatus.Trial)
                    {
                        ApplySubscriptionTransition(sub, SubscriptionStatus.Suspended, "payment_dispute_opened", now);
                    }
                    existing.EntitlementsFrozen = true;
                    break;
                case "dispute_funds_reinstated":
                case "dispute_won":
                    if (sub.Status == SubscriptionStatus.Suspended && wasFrozenByThisDispute)
                    {
                        ApplySubscriptionTransition(sub, SubscriptionStatus.Active, "payment_dispute_resolved", now);
                    }
                    existing.EntitlementsFrozen = false;
                    break;
                case "dispute_lost":
                    ApplySubscriptionTransition(sub, SubscriptionStatus.Cancelled, "payment_dispute_lost", now);
                    existing.EntitlementsFrozen = true;
                    break;
            }
        }

        _db.BillingEvents.Add(new BillingEvent
        {
            Id = $"bill-evt-dispute-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            SubscriptionId = existing.SubscriptionId,
            EventType = $"dispute_{existing.Status}",
            EntityType = nameof(PaymentDispute),
            EntityId = existing.Id.ToString(),
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                gateway = signal.Gateway,
                gatewayDisputeId = signal.GatewayDisputeId,
                paymentTransactionId = signal.PaymentTransactionId,
                normalizedStatus = signal.NormalizedStatus,
                amountDisputed = existing.AmountDisputed,
                currency = existing.Currency,
                entitlementsFrozen = existing.EntitlementsFrozen
            }),
            OccurredAt = now
        });

        await _db.SaveChangesAsync(ct);
        return new DisputeRecordResponse(existing.Id, existing.Status, existing.EntitlementsFrozen);
    }

    private static string MapStatus(string normalized) => normalized switch
    {
        "dispute_opened" => "opened",
        "dispute_funds_withdrawn" => "funds_withdrawn",
        "dispute_funds_reinstated" => "funds_reinstated",
        "dispute_won" => "closed_won",
        "dispute_lost" => "closed_lost",
        "dispute_updated" => "opened",
        _ => normalized
    };

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }

    private static void ApplySubscriptionTransition(Subscription subscription, SubscriptionStatus target, string reason, DateTimeOffset now)
    {
        if (SubscriptionStateMachine.IsLegal(subscription.Status, target))
        {
            SubscriptionStateMachine.Transition(subscription, target, reason);
            return;
        }

        subscription.Status = target;
        subscription.ChangedAt = now;
    }
}
