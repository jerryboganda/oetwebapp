using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Per-region price override for a billing target (plan, add-on, wallet top-up tier).
/// When present, supersedes the target's default currency/price for checkout in that
/// region. Missing rows fall back to the target's own Currency/Price columns.
/// </summary>
[Index(nameof(TargetType), nameof(TargetId), nameof(Region), IsUnique = true)]
[Index(nameof(Region), nameof(IsActive))]
public class RegionPricing
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>One of: plan, addon, wallet_topup_tier.</summary>
    [MaxLength(32)]
    public string TargetType { get; set; } = default!;

    /// <summary>Identifier of the target row (BillingPlan.Id, BillingAddOn.Id, etc).</summary>
    [MaxLength(64)]
    public string TargetId { get; set; } = default!;

    /// <summary>Region code: UK, GULF, EGYPT, PK, ROW, or a more specific country override.</summary>
    [MaxLength(16)]
    public string Region { get; set; } = default!;

    /// <summary>ISO 4217 currency code for this region's price.</summary>
    [MaxLength(8)]
    public string Currency { get; set; } = default!;

    /// <summary>Price in major units (matches existing BillingPlan.Price convention).</summary>
    public decimal PriceAmount { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}

public static class RegionPricingTargetTypes
{
    public const string Plan = "plan";
    public const string AddOn = "addon";
    public const string WalletTopUpTier = "wallet_topup_tier";
}

public static class BillingRegions
{
    public const string UnitedKingdom = "UK";
    public const string Gulf = "GULF";
    public const string Egypt = "EGYPT";
    public const string Pakistan = "PK";
    public const string RestOfWorld = "ROW";

    public static IReadOnlyList<string> All { get; } = new[] { UnitedKingdom, Gulf, Egypt, Pakistan, RestOfWorld };

    /// <summary>
    /// Maps ISO 3166-1 alpha-2 country codes to their billing region. Unknown codes
    /// fall back to ROW so the gateway registry can still resolve a route.
    /// </summary>
    public static string FromCountry(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return RestOfWorld;
        }

        return countryCode.ToUpperInvariant() switch
        {
            "GB" or "UK" => UnitedKingdom,
            "AE" or "SA" or "OM" or "QA" or "KW" or "BH" => Gulf,
            "EG" => Egypt,
            "PK" => Pakistan,
            _ => RestOfWorld,
        };
    }
}
