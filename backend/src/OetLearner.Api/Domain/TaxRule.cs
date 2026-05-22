using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Effective-dated tax rate applied to checkout. One row per country / tax-type combination.
/// Multiple rows for the same country with non-overlapping date ranges support rate changes.
/// </summary>
[Index(nameof(Country), nameof(EffectiveFrom))]
[Index(nameof(Region), nameof(IsActive))]
public class TaxRule
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>ISO 3166-1 alpha-2 country code (e.g. GB, AE, SA, EG).</summary>
    [MaxLength(2)]
    public string Country { get; set; } = default!;

    /// <summary>Billing region (UK/GULF/EGYPT/ROW). Pre-computed for fast lookup.</summary>
    [MaxLength(16)]
    public string Region { get; set; } = default!;

    /// <summary>Tax kind: vat, gst, sales_tax, withholding.</summary>
    [MaxLength(32)]
    public string TaxType { get; set; } = default!;

    /// <summary>Display name (e.g. "UK VAT", "UAE VAT", "Egypt VAT").</summary>
    [MaxLength(64)]
    public string DisplayName { get; set; } = default!;

    /// <summary>Rate in percent (e.g. 20.0 for UK VAT).</summary>
    public decimal RatePercent { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }

    /// <summary>If true, B2B buyers with a valid foreign VAT-ID are zero-rated (reverse-charge).</summary>
    public bool ZeroRateForB2BReverseCharge { get; set; } = true;

    /// <summary>If true, prices in this region are quoted tax-inclusive (UK convention).</summary>
    public bool IsTaxInclusiveDisplay { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
