using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

/// <summary>
/// Stored FX rate. One row per (from, to, effective_from). The FxRateService
/// refreshes daily from the configured provider; historic rows are retained
/// so any invoice / refund can recompute against the exact rate at the time.
/// </summary>
[Index(nameof(FromCurrency), nameof(ToCurrency), nameof(EffectiveFrom))]
[Index(nameof(EffectiveFrom))]
public class ExchangeRate
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(3)]
    public string FromCurrency { get; set; } = default!;

    [MaxLength(3)]
    public string ToCurrency { get; set; } = default!;

    /// <summary>1 FromCurrency = Rate ToCurrency.</summary>
    public decimal Rate { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }

    /// <summary>Provider that supplied the rate (open_exchange_rates, ecb, fixer, manual).</summary>
    [MaxLength(32)]
    public string Source { get; set; } = "manual";

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A/B pricing experiment. Variants assigned per-user via deterministic hash;
/// the resolved variant feeds into the region-pricing resolver so checkout sees
/// a different PriceAmount per user without writing per-user rows.
/// </summary>
[Index(nameof(Status))]
[Index(nameof(TargetType), nameof(TargetId))]
public class PricingExperiment
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    /// <summary>plan | addon | wallet_topup_tier.</summary>
    [MaxLength(32)]
    public string TargetType { get; set; } = default!;

    [MaxLength(64)]
    public string TargetId { get; set; } = default!;

    /// <summary>Restrict to a billing region, or "*" for global.</summary>
    [MaxLength(16)]
    public string Region { get; set; } = "*";

    /// <summary>draft | running | paused | completed.</summary>
    [MaxLength(16)]
    public string Status { get; set; } = "draft";

    /// <summary>0-100 — share of eligible users included; rest see control.</summary>
    public int RolloutPercent { get; set; }

    /// <summary>JSON array of variants: [{ code, weight, priceMultiplier, currency? }].</summary>
    [MaxLength(2048)]
    public string VariantsJson { get; set; } = "[]";

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }
}

/// <summary>Per-user variant assignment for a pricing experiment.</summary>
[Index(nameof(ExperimentId), nameof(UserId), IsUnique = true)]
[Index(nameof(ExperimentId), nameof(VariantCode))]
public class PricingExperimentAssignment
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ExperimentId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(32)]
    public string VariantCode { get; set; } = default!;

    /// <summary>Was a conversion (paid checkout) attributed to this variant?</summary>
    public bool Converted { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }
    public decimal? ConvertedAmount { get; set; }

    public DateTimeOffset AssignedAt { get; set; }
}
