using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Singleton row (id = <c>global</c>) holding runtime-mutable Writing
/// module configuration: kill switches for the two AI features (grading
/// + coach), free-tier entitlement parameters, and advisory provider
/// preference hints.
///
/// Mirrors the <see cref="AiGlobalPolicy"/> shape: bootstrapped lazily on
/// first read by <c>WritingOptionsProvider</c>; mutated only via the
/// admin endpoint <c>PUT /v1/admin/writing/options</c> which audits.
/// </summary>
public class WritingOptions
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = "global";

    // ── Kill switches ──────────────────────────────────────────────────
    public bool AiGradingEnabled { get; set; } = true;
    public bool AiCoachEnabled { get; set; } = true;

    [MaxLength(256)]
    public string? KillSwitchReason { get; set; }

    // ── Entitlement (free tier) ────────────────────────────────────────
    /// <summary>Completed Writing attempts allowed per rolling window.</summary>
    public int FreeTierLimit { get; set; } = 0;
    /// <summary>Window length in days for the free-tier counter.</summary>
    public int FreeTierWindowDays { get; set; } = 7;
    /// <summary>
    /// Master flag — when false the free tier is disabled entirely
    /// (premium-only). Default false: Writing is premium-only by default.
    /// </summary>
    public bool FreeTierEnabled { get; set; } = false;

    // ── Provider hints (advisory; AiFeatureRoutes still authoritative) ─
    [MaxLength(64)]
    public string? PreferredGradingProvider { get; set; }

    [MaxLength(64)]
    public string? PreferredCoachProvider { get; set; }

    [MaxLength(64)]
    public string? PreferredDraftProvider { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}
