using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

/// <summary>
/// Captured the moment a learner clicks "Cancel" — admin or system may offer
/// a deflection coupon. Converts to actual cancellation only on confirm.
/// </summary>
[Index(nameof(SubscriptionId), nameof(Status))]
public class CancellationIntent
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SubscriptionId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>too_expensive | passed_exam | not_useful | technical | switching | other.</summary>
    [MaxLength(32)]
    public string Reason { get; set; } = default!;

    [MaxLength(1024)]
    public string? ReasonDetail { get; set; }

    /// <summary>started | offered_coupon | offered_pause | confirmed_cancel | dismissed | retained.</summary>
    [MaxLength(32)]
    public string Status { get; set; } = "started";

    [MaxLength(64)]
    public string? OfferedCouponCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>Admin-editable rule that decides which deflection offer to make for a given reason.</summary>
[Index(nameof(TriggerReason), nameof(IsActive))]
public class DeflectionRule
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(32)]
    public string TriggerReason { get; set; } = default!;

    [MaxLength(64)]
    public string OfferedCouponCode { get; set; } = default!;

    public int MinTenureDays { get; set; }
    public int MaxOffersPerUser { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
