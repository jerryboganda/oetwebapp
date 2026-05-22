using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Active recovery campaign for a subscription whose renewal payment failed.
/// Drives the Day 0→14 retry / notify / pause schedule.
/// </summary>
[Index(nameof(SubscriptionId), nameof(Status))]
[Index(nameof(Status), nameof(NextAttemptAt))]
public class DunningCampaign
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SubscriptionId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>active | recovered | paused | cancelled.</summary>
    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int AttemptCount { get; set; }

    [MaxLength(64)]
    public string? LastFailureCode { get; set; }

    [MaxLength(512)]
    public string? LastFailureReason { get; set; }

    /// <summary>Comma-separated step codes already executed (day0_email, day3_retry, day5_sms, ...).</summary>
    [MaxLength(512)]
    public string StepsCompletedCsv { get; set; } = string.Empty;

    public DateTimeOffset? RecoveredAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Short-lived signed link emailed to a learner so they can update payment method without logging in.</summary>
[Index(nameof(UserId), nameof(ExpiresAt))]
public class PaymentMethodUpdateLink
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string SubscriptionId { get; set; } = default!;

    [MaxLength(128)]
    public string Token { get; set; } = default!;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
