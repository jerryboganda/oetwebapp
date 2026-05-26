using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Outcome of a single smart-retry attempt executed by the
/// <c>DunningCampaignService</c> against a Stripe invoice that failed
/// during a recurring renewal. Three attempts are scheduled per failed
/// invoice (T+24h, T+72h, T+168h) and tracked in this table so a Stripe
/// webhook retry storm cannot duplicate charges.
/// </summary>
[Index(nameof(SubscriptionId), nameof(InvoiceId))]
[Index(nameof(InvoiceId), nameof(AttemptNumber), IsUnique = true)]
[Index(nameof(Outcome), nameof(ScheduledAt))]
public class DunningAttempt
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SubscriptionId { get; set; } = default!;

    /// <summary>Stripe invoice id that failed and is being retried.</summary>
    [MaxLength(128)]
    public string InvoiceId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>1, 2, or 3.</summary>
    public int AttemptNumber { get; set; }

    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset? ExecutedAt { get; set; }

    /// <summary>pending | succeeded | failed.</summary>
    public DunningAttemptOutcome Outcome { get; set; } = DunningAttemptOutcome.Pending;

    [MaxLength(64)]
    public string? StripeFailureCode { get; set; }

    [MaxLength(512)]
    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public enum DunningAttemptOutcome
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}
