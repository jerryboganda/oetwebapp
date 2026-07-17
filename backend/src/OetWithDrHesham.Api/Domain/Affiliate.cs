using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

/// <summary>
/// Education agent / institute partner with a referral code, commission rate, and payout schedule.
/// </summary>
[Index(nameof(Code), IsUnique = true)]
[Index(nameof(Status))]
public class Affiliate
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string OwnerName { get; set; } = default!;

    [MaxLength(256)]
    public string ContactEmail { get; set; } = default!;

    public decimal CommissionPercent { get; set; }

    public int CookieDays { get; set; } = 30;

    public decimal PayoutThresholdAmount { get; set; }

    [MaxLength(8)]
    public string PayoutCurrency { get; set; } = "USD";

    /// <summary>bank_transfer | wise | paypal.</summary>
    [MaxLength(32)]
    public string PayoutMethod { get; set; } = "bank_transfer";

    /// <summary>Encrypted JSON containing bank/Wise/PayPal account details.</summary>
    [MaxLength(4096)]
    public string PayoutDetailsEncrypted { get; set; } = string.Empty;

    /// <summary>active | paused | terminated.</summary>
    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Records the click-to-signup attribution that earns commission for an affiliate.</summary>
[Index(nameof(UserId), nameof(AffiliateId), IsUnique = true)]
[Index(nameof(AffiliateId), nameof(ConvertedAt))]
public class AffiliateAttribution
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string AffiliateId { get; set; } = default!;

    public DateTimeOffset ClickedAt { get; set; }
    public DateTimeOffset AttributedAt { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }

    [MaxLength(64)]
    public string? FirstPaymentTransactionId { get; set; }
}

/// <summary>Earned commission ledger entry. One row per qualifying payment transaction.</summary>
[Index(nameof(AffiliateId), nameof(Status))]
[Index(nameof(PaymentTransactionId), IsUnique = true)]
public class AffiliateCommission
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AffiliateId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string PaymentTransactionId { get; set; } = default!;

    public decimal AmountAmount { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = default!;

    /// <summary>accrued | pending_payout | paid | reversed.</summary>
    [MaxLength(16)]
    public string Status { get; set; } = "accrued";

    public DateTimeOffset AccruedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }

    [MaxLength(256)]
    public string? PayoutBatchId { get; set; }
}
