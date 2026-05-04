using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// DB-backed wallet top-up tier configuration. When at least one row is
/// active, <see cref="Services.WalletService.GetConfiguredTopUpTiers"/>
/// uses these rows in place of the appsettings fallback
/// (<see cref="Configuration.WalletBillingOptions.TopUpTiers"/>).
/// </summary>
public class WalletTopUpTierConfig
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Stable, immutable kebab-case identifier for this tier. Used to refer
    /// to a tier across catalog versions and audit trails. Once set on a row
    /// the slug must never change (admin updates rejected by
    /// <see cref="Services.AdminWalletTierService"/>).
    /// </summary>
    [MaxLength(64)]
    public string? Slug { get; set; }

    /// <summary>Top-up amount in whole units of <see cref="Currency"/> (matches WalletService dollar units).</summary>
    public int Amount { get; set; }

    /// <summary>Base credits granted by this tier.</summary>
    public int Credits { get; set; }

    /// <summary>Bonus credits granted on top of <see cref="Credits"/>. Must be &gt;= 0.</summary>
    public int Bonus { get; set; }

    [MaxLength(80)]
    public string? Label { get; set; }

    public bool IsPopular { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(3)]
    public string Currency { get; set; } = "AUD";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? CreatedBy { get; set; }

    [MaxLength(64)]
    public string? UpdatedBy { get; set; }
}
