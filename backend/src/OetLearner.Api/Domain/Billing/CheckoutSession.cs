using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain.Billing;

[Index(nameof(IdempotencyKey), IsUnique = true)]
[Index(nameof(StripeSessionId))]
[Index(nameof(GatewayOrderId))]
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

    /// <summary>
    /// Hosted provider URL returned with <see cref="StripeSessionId"/>.
    /// Persisted so idempotent replays do not need a provider round-trip.
    /// Null only for non-hosted gateways and legacy Stripe rows awaiting backfill.
    /// </summary>
    [MaxLength(2048)]
    public string? HostedCheckoutUrl { get; set; }

    /// <summary>Which gateway owns this session: "stripe" (hosted redirect) or "paypal"
    /// (in-page embedded capture). Defaults to stripe for back-compat with existing rows.</summary>
    [MaxLength(16)]
    public string Gateway { get; set; } = "stripe";

    /// <summary>For embedded gateways (PayPal), the gateway order id the browser SDK
    /// approves and the capture/webhook map back to this cart session. Null for Stripe.</summary>
    [MaxLength(256)]
    public string? GatewayOrderId { get; set; }

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
