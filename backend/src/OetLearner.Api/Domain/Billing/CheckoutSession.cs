using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain.Billing;

[Index(nameof(IdempotencyKey), IsUnique = true)]
[Index(nameof(StripeSessionId))]
[Index(nameof(UserId))]
public class CheckoutSession
{
    [Key]
    public Guid Id { get; set; }

    public Guid? CartId { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(256)]
    public string? StripeSessionId { get; set; }

    [MaxLength(64)]
    public string IdempotencyKey { get; set; } = default!;

    /// <summary>pending | fulfilled | failed | expired</summary>
    [MaxLength(32)]
    public string Status { get; set; } = "pending";

    public decimal TotalAmount { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "AUD";

    public string? MetadataJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
