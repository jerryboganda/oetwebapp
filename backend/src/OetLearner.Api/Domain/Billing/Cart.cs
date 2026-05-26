using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain.Billing;

[Index(nameof(UserId))]
[Index(nameof(SessionToken))]
[Index(nameof(Status))]
public class Cart
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string? UserId { get; set; }

    [MaxLength(256)]
    public string? SessionToken { get; set; }

    /// <summary>active | converted | abandoned</summary>
    [MaxLength(32)]
    public string Status { get; set; } = "active";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// When the +24h abandoned-cart recovery email was queued for this cart.
    /// Set by <c>AbandonedCartRecoveryService</c> after a successful Brevo
    /// dispatch so the same cart is never emailed twice. Null when no recovery
    /// email has been sent.
    /// </summary>
    public DateTimeOffset? RecoveryEmailSentAt { get; set; }

    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
    public ICollection<AppliedPromoCode> AppliedPromoCodes { get; set; } = new List<AppliedPromoCode>();
}

[Index(nameof(CartId))]
public class CartItem
{
    [Key]
    public Guid Id { get; set; }

    public Guid CartId { get; set; }
    public Guid BillingProductId { get; set; }
    public Guid BillingPriceId { get; set; }

    public int Quantity { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Cart Cart { get; set; } = default!;
    public BillingProduct BillingProduct { get; set; } = default!;
    public BillingPrice BillingPrice { get; set; } = default!;
}

[Index(nameof(CartId))]
public class AppliedPromoCode
{
    [Key]
    public Guid Id { get; set; }

    public Guid CartId { get; set; }

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    public decimal? DiscountAmount { get; set; }
    public decimal? DiscountPercent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Cart Cart { get; set; } = default!;
}
