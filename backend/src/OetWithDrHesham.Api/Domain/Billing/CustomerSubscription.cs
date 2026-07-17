using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain.Billing;

[Index(nameof(StripeSubscriptionId), IsUnique = true)]
[Index(nameof(UserId))]
public class CustomerSubscription
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(256)]
    public string StripeSubscriptionId { get; set; } = default!;

    [MaxLength(256)]
    public string StripePriceId { get; set; } = default!;

    public Guid? BillingProductId { get; set; }

    /// <summary>active | past_due | canceled | paused | trialing</summary>
    [MaxLength(32)]
    public string Status { get; set; } = "active";

    public DateTimeOffset CurrentPeriodStart { get; set; }
    public DateTimeOffset CurrentPeriodEnd { get; set; }

    public bool CancelAtPeriodEnd { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
