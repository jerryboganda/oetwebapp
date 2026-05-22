using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Maps (region, currency, product type) → payment gateway with a priority order.
/// IGatewayRegistry consults this table at checkout to pick the best available
/// gateway; the registry skips entries whose gateway has missing credentials.
/// </summary>
[Index(nameof(Region), nameof(Currency), nameof(ProductType), nameof(Priority))]
[Index(nameof(Region), nameof(Currency), nameof(ProductType), nameof(GatewayName), IsUnique = true)]
public class GatewayRoutingConfig
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Region code (UK/GULF/EGYPT/PK/ROW) or a specific ISO country override.</summary>
    [MaxLength(16)]
    public string Region { get; set; } = default!;

    /// <summary>ISO 4217 currency. Use "*" to match any currency in the region.</summary>
    [MaxLength(8)]
    public string Currency { get; set; } = default!;

    /// <summary>One of: subscription, addon, wallet_topup, manual. Use "*" for any.</summary>
    [MaxLength(32)]
    public string ProductType { get; set; } = default!;

    /// <summary>Gateway identifier — must match an IPaymentGateway.GatewayName.</summary>
    [MaxLength(32)]
    public string GatewayName { get; set; } = default!;

    /// <summary>Lower number = higher priority. Entries with equal priority are non-deterministic.</summary>
    public int Priority { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}

public static class GatewayProductTypes
{
    public const string Subscription = "subscription";
    public const string AddOn = "addon";
    public const string WalletTopUp = "wallet_topup";
    public const string Manual = "manual";
    public const string Any = "*";
}
