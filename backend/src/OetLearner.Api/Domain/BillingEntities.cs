using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>Managed add-on that can be sold alongside a billing plan.</summary>
public class BillingAddOn
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(1024)]
    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "AUD";

    [MaxLength(32)]
    public string Interval { get; set; } = "one_time";

    public BillingAddOnStatus Status { get; set; } = BillingAddOnStatus.Active;

    public bool IsRecurring { get; set; }

    public int DurationDays { get; set; }

    public int GrantCredits { get; set; }

    [MaxLength(2048)]
    public string GrantEntitlementsJson { get; set; } = "{}";

    [MaxLength(2048)]
    public string CompatiblePlanCodesJson { get; set; } = "[]";

    [MaxLength(64)]
    public string? ActiveVersionId { get; set; }

    [MaxLength(64)]
    public string? LatestVersionId { get; set; }

    public bool AppliesToAllPlans { get; set; } = true;

    public bool IsStackable { get; set; } = true;

    public int QuantityStep { get; set; } = 1;

    public int? MaxQuantity { get; set; }

    public int DisplayOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Immutable managed add-on catalog snapshot.</summary>
public class BillingAddOnVersion
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AddOnId { get; set; } = default!;

    public int VersionNumber { get; set; }

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(1024)]
    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "AUD";

    [MaxLength(32)]
    public string Interval { get; set; } = "one_time";

    public BillingAddOnStatus Status { get; set; } = BillingAddOnStatus.Active;

    public bool IsRecurring { get; set; }

    public int DurationDays { get; set; }

    public int GrantCredits { get; set; }

    [MaxLength(2048)]
    public string GrantEntitlementsJson { get; set; } = "{}";

    [MaxLength(2048)]
    public string CompatiblePlanCodesJson { get; set; } = "[]";

    public bool AppliesToAllPlans { get; set; } = true;

    public bool IsStackable { get; set; } = true;

    public int QuantityStep { get; set; } = 1;

    public int? MaxQuantity { get; set; }

    public int DisplayOrder { get; set; }

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }

    [MaxLength(128)]
    public string? CreatedByAdminName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Promo code or discount rule that can be applied at checkout.</summary>
public class BillingCoupon
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(1024)]
    public string Description { get; set; } = string.Empty;

    public BillingDiscountType DiscountType { get; set; } = BillingDiscountType.Percentage;

    public decimal DiscountValue { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "AUD";

    public BillingCouponStatus Status { get; set; } = BillingCouponStatus.Draft;

    public DateTimeOffset? StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public int? UsageLimitTotal { get; set; }

    public int? UsageLimitPerUser { get; set; }

    public decimal? MinimumSubtotal { get; set; }

    [MaxLength(2048)]
    public string ApplicablePlanCodesJson { get; set; } = "[]";

    [MaxLength(2048)]
    public string ApplicableAddOnCodesJson { get; set; } = "[]";

    [MaxLength(64)]
    public string? ActiveVersionId { get; set; }

    [MaxLength(64)]
    public string? LatestVersionId { get; set; }

    public bool IsStackable { get; set; }

    [MaxLength(1024)]
    public string? Notes { get; set; }

    public int RedemptionCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Immutable promo code or discount rule catalog snapshot.</summary>
public class BillingCouponVersion
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string CouponId { get; set; } = default!;

    public int VersionNumber { get; set; }

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(1024)]
    public string Description { get; set; } = string.Empty;

    public BillingDiscountType DiscountType { get; set; } = BillingDiscountType.Percentage;

    public decimal DiscountValue { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "AUD";

    public BillingCouponStatus Status { get; set; } = BillingCouponStatus.Draft;

    public DateTimeOffset? StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public int? UsageLimitTotal { get; set; }

    public int? UsageLimitPerUser { get; set; }

    public decimal? MinimumSubtotal { get; set; }

    [MaxLength(2048)]
    public string ApplicablePlanCodesJson { get; set; } = "[]";

    [MaxLength(2048)]
    public string ApplicableAddOnCodesJson { get; set; } = "[]";

    public bool IsStackable { get; set; }

    [MaxLength(1024)]
    public string? Notes { get; set; }

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }

    [MaxLength(128)]
    public string? CreatedByAdminName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Redemption record for a coupon applied to a checkout or billing quote.</summary>
public class BillingCouponRedemption
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string CouponCode { get; set; } = default!;

    [MaxLength(64)]
    public string? CouponId { get; set; }

    [MaxLength(64)]
    public string? CouponVersionId { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string QuoteId { get; set; } = default!;

    [MaxLength(256)]
    public string? CheckoutSessionId { get; set; }

    [MaxLength(64)]
    public string? SubscriptionId { get; set; }

    public decimal DiscountAmount { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "AUD";

    public BillingRedemptionStatus Status { get; set; } = BillingRedemptionStatus.Reserved;

    public DateTimeOffset RedeemedAt { get; set; }
}

/// <summary>Persisted pricing snapshot used for checkout and reconciliation.</summary>
public class BillingQuote
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string? SubscriptionId { get; set; }

    [MaxLength(64)]
    public string? PlanCode { get; set; }

    [MaxLength(64)]
    public string? PlanVersionId { get; set; }

    [MaxLength(1024)]
    public string AddOnCodesJson { get; set; } = "[]";

    [MaxLength(1024)]
    public string AddOnVersionIdsJson { get; set; } = "{}";

    [MaxLength(64)]
    public string? CouponCode { get; set; }

    [MaxLength(64)]
    public string? CouponVersionId { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "AUD";

    public decimal SubtotalAmount { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public BillingQuoteStatus Status { get; set; } = BillingQuoteStatus.Created;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    [MaxLength(256)]
    public string? CheckoutSessionId { get; set; }

    [MaxLength(64)]
    public string? IdempotencyKey { get; set; }

    [MaxLength(4096)]
    public string SnapshotJson { get; set; } = "{}";
}

/// <summary>Billing event ledger for lifecycle and operational history.</summary>
public class BillingEvent
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string? UserId { get; set; }

    [MaxLength(64)]
    public string? SubscriptionId { get; set; }

    [MaxLength(64)]
    public string? QuoteId { get; set; }

    [MaxLength(64)]
    public string EventType { get; set; } = default!;

    [MaxLength(64)]
    public string EntityType { get; set; } = default!;

    [MaxLength(256)]
    public string? EntityId { get; set; }

    [MaxLength(4096)]
    public string PayloadJson { get; set; } = "{}";

    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Attached billing item such as an add-on on top of an active subscription.</summary>
public class SubscriptionItem
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SubscriptionId { get; set; } = default!;

    [MaxLength(64)]
    public string ItemCode { get; set; } = default!;

    [MaxLength(64)]
    public string ItemType { get; set; } = "addon";

    [MaxLength(64)]
    public string? AddOnVersionId { get; set; }

    public int Quantity { get; set; } = 1;

    public SubscriptionItemStatus Status { get; set; } = SubscriptionItemStatus.Active;

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    [MaxLength(64)]
    public string? QuoteId { get; set; }

    [MaxLength(256)]
    public string? CheckoutSessionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

// ── Wallet Transaction Ledger ──

[Index(nameof(WalletId))]
public class WalletTransaction
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string WalletId { get; set; } = default!;

    [MaxLength(32)]
    public string TransactionType { get; set; } = default!;
    // credit_purchase | plan_grant | review_deduction | mock_deduction |
    // top_up | refund | expiration | manual_adjustment

    public int Amount { get; set; } // Positive = credit, Negative = debit
    public int BalanceAfter { get; set; }

    [MaxLength(32)]
    public string? ReferenceType { get; set; } // payment | subscription | addon | review | mock | manual

    [MaxLength(128)]
    public string? ReferenceId { get; set; }

    [MaxLength(256)]
    public string? Description { get; set; }

    [MaxLength(64)]
    public string? CreatedBy { get; set; } // system | admin user id

    public DateTimeOffset CreatedAt { get; set; }
}

// ── Payment Gateway Integration ──

[Index(nameof(LearnerUserId))]
[Index(nameof(GatewayTransactionId), IsUnique = true)]
public class PaymentTransaction
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string LearnerUserId { get; set; } = default!;

    [MaxLength(16)]
    public string Gateway { get; set; } = default!; // stripe | paypal

    [MaxLength(256)]
    public string GatewayTransactionId { get; set; } = default!;

    [MaxLength(32)]
    public string TransactionType { get; set; } = default!;
    // subscription_payment | one_time_purchase | wallet_top_up | refund

    [MaxLength(32)]
    public string Status { get; set; } = "pending";
    // pending | completed | failed | refunded | disputed

    public decimal Amount { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "AUD";

    [MaxLength(32)]
    public string? ProductType { get; set; } // plan | addon | wallet_top_up

    [MaxLength(128)]
    public string? ProductId { get; set; }

    [MaxLength(64)]
    public string? QuoteId { get; set; }

    [MaxLength(64)]
    public string? PlanVersionId { get; set; }

    [MaxLength(1024)]
    public string AddOnVersionIdsJson { get; set; } = "{}";

    [MaxLength(64)]
    public string? CouponVersionId { get; set; }

    public string? MetadataJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[Index(nameof(GatewayEventId), IsUnique = true)]
public class PaymentWebhookEvent
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(16)]
    public string Gateway { get; set; } = default!; // stripe | paypal

    [MaxLength(128)]
    public string EventType { get; set; } = default!;

    [MaxLength(256)]
    public string GatewayEventId { get; set; } = default!;

    [MaxLength(32)]
    public string ProcessingStatus { get; set; } = "received";
    // received | processing | completed | failed | ignored

    [MaxLength(32)]
    public string VerificationStatus { get; set; } = "legacy";
    // legacy | verified | failed

    public DateTimeOffset? VerifiedAt { get; set; }

    [MaxLength(64)]
    public string? PayloadSha256 { get; set; }

    [MaxLength(32)]
    public string? ParserVersion { get; set; }

    [MaxLength(256)]
    public string? GatewayTransactionId { get; set; }

    [MaxLength(32)]
    public string? NormalizedStatus { get; set; }

    public int AttemptCount { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? LastAttemptedAt { get; set; }
    public DateTimeOffset? LastRetriedAt { get; set; }

    [MaxLength(64)]
    public string? LastRetriedByAdminId { get; set; }

    [MaxLength(128)]
    public string? LastRetriedByAdminName { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public string? ErrorMessage { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

/// <summary>Score guarantee pledge — learner claims money-back if score doesn't improve.</summary>
[Index(nameof(UserId))]
public class ScoreGuaranteePledge
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string SubscriptionId { get; set; } = default!;

    /// <summary>Baseline OET score at pledge activation (0-500).</summary>
    public int BaselineScore { get; set; }

    /// <summary>Guaranteed minimum improvement (points).</summary>
    public int GuaranteedImprovement { get; set; } = 50;

    /// <summary>OET score after exam (uploaded by learner).</summary>
    public int? ActualScore { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "active";
    // active | claim_submitted | claim_approved | claim_rejected | expired

    [MaxLength(512)]
    public string? ProofDocumentUrl { get; set; }

    [MaxLength(512)]
    public string? ClaimNote { get; set; }

    [MaxLength(512)]
    public string? ReviewNote { get; set; }

    [MaxLength(64)]
    public string? ReviewedBy { get; set; }

    public DateTimeOffset ActivatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ClaimSubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

/// <summary>Referral tracking for the referral program.</summary>
[Index(nameof(ReferrerUserId))]
[Index(nameof(ReferralCode), IsUnique = true)]
public class ReferralRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReferrerUserId { get; set; } = default!;

    [MaxLength(32)]
    public string ReferralCode { get; set; } = default!;

    [MaxLength(64)]
    public string? ReferredUserId { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "pending";
    // pending | activated | rewarded | expired

    public decimal ReferrerCreditAmount { get; set; } = 10m;
    public decimal ReferredDiscountPercent { get; set; } = 10m;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? RewardedAt { get; set; }
}