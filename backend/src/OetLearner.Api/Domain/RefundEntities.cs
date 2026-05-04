using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// A refund issued against a <see cref="PaymentTransaction"/>. Supports both
/// partial and full refunds. Idempotent on (Gateway, GatewayRefundId).
/// </summary>
[Index(nameof(PaymentTransactionId))]
[Index(nameof(Gateway), nameof(GatewayRefundId), IsUnique = true)]
[Index(nameof(IdempotencyKey), IsUnique = true)]
public class OrderRefund
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(256)]
    public string PaymentTransactionId { get; set; } = default!;

    [MaxLength(64)]
    public string LearnerUserId { get; set; } = default!;

    [MaxLength(16)]
    public string Gateway { get; set; } = default!;

    [MaxLength(256)]
    public string GatewayRefundId { get; set; } = default!;

    [MaxLength(64)]
    public string IdempotencyKey { get; set; } = default!;

    [MaxLength(16)]
    public string RefundType { get; set; } = "partial"; // partial | full

    public decimal Amount { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "AUD";

    [MaxLength(32)]
    public string Status { get; set; } = "pending";
    // pending | succeeded | failed | reversed

    [MaxLength(64)]
    public string? Reason { get; set; }

    [MaxLength(1024)]
    public string? AdminNote { get; set; }

    [MaxLength(64)]
    public string? RequestedByAdminId { get; set; }

    [MaxLength(128)]
    public string? RequestedByAdminName { get; set; }

    public bool ReversedWalletCredits { get; set; }

    public bool ReversedEntitlements { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A chargeback/dispute lifecycle record sourced from the payment gateway.
/// State transitions: opened -> funds_withdrawn -> closed_won | closed_lost.
/// While not closed_won, entitlements tied to the underlying subscription are
/// frozen via <see cref="DisputeFreeze"/>.
/// </summary>
[Index(nameof(PaymentTransactionId))]
[Index(nameof(Gateway), nameof(GatewayDisputeId), IsUnique = true)]
public class PaymentDispute
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(256)]
    public string PaymentTransactionId { get; set; } = default!;

    [MaxLength(64)]
    public string LearnerUserId { get; set; } = default!;

    [MaxLength(64)]
    public string? SubscriptionId { get; set; }

    [MaxLength(16)]
    public string Gateway { get; set; } = default!;

    [MaxLength(256)]
    public string GatewayDisputeId { get; set; } = default!;

    [MaxLength(32)]
    public string Status { get; set; } = "opened";
    // opened | funds_withdrawn | funds_reinstated | closed_won | closed_lost

    [MaxLength(64)]
    public string? Reason { get; set; }

    public decimal AmountDisputed { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "AUD";

    public bool EntitlementsFrozen { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset? FundsWithdrawnAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
