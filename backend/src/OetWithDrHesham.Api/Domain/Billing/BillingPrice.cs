using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain.Billing;

[Index(nameof(StripePriceId))]
[Index(nameof(BillingProductId))]
public class BillingPrice
{
    [Key]
    public Guid Id { get; set; }

    public Guid BillingProductId { get; set; }

    [MaxLength(64)]
    public string? StripePriceId { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "AUD";

    public decimal Amount { get; set; }

    /// <summary>month | year | null for one-time</summary>
    [MaxLength(16)]
    public string? Interval { get; set; }

    public int IntervalCount { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    /// <summary>ISO 3166-1 alpha-2 country code; null = global.</summary>
    [MaxLength(2)]
    public string? Country { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public BillingProduct BillingProduct { get; set; } = default!;
}
