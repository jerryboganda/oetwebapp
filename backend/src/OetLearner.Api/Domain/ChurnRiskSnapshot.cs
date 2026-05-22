using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Per-user churn-risk snapshot computed by the heuristic-AI ChurnPredictionService.
/// One row per user per day; older snapshots retained for trend analysis.
/// </summary>
[Index(nameof(UserId), nameof(SnapshotDate))]
[Index(nameof(SnapshotDate), nameof(RiskBand))]
[Index(nameof(RiskBand), nameof(RiskScore))]
public class ChurnRiskSnapshot
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public DateOnly SnapshotDate { get; set; }

    /// <summary>0.0 (no risk) → 1.0 (certain churn).</summary>
    public decimal RiskScore { get; set; }

    /// <summary>low | medium | high.</summary>
    [MaxLength(8)]
    public string RiskBand { get; set; } = "low";

    /// <summary>JSON object: factor → contribution to score.</summary>
    [MaxLength(2048)]
    public string FactorsJson { get; set; } = "{}";

    /// <summary>Recommended retention action (e.g. send_winback_coupon, schedule_check_in).</summary>
    [MaxLength(64)]
    public string? RecommendedAction { get; set; }

    /// <summary>Has the recommended action been dispatched? Set by the retention worker.</summary>
    public bool ActionDispatched { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}

/// <summary>EMA-based per-user usage forecast for the next N-day window.</summary>
[Index(nameof(UserId), nameof(SnapshotDate))]
[Index(nameof(FeatureCode), nameof(SnapshotDate))]
public class UsageForecastSnapshot
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public DateOnly SnapshotDate { get; set; }

    /// <summary>Feature this forecast covers, or "*" for all-features aggregate.</summary>
    [MaxLength(64)]
    public string FeatureCode { get; set; } = "*";

    /// <summary>Window length in days the forecast projects.</summary>
    public int WindowDays { get; set; } = 30;

    /// <summary>Predicted call count for the window.</summary>
    public int ForecastCalls { get; set; }

    /// <summary>Predicted credit consumption for the window.</summary>
    public int ForecastCredits { get; set; }

    /// <summary>Predicted cost in USD for the window (sum of CostEstimateUsd).</summary>
    public decimal ForecastCostUsd { get; set; }

    /// <summary>Rolling 30-day average daily calls used as input.</summary>
    public decimal Ema30DailyCalls { get; set; }

    /// <summary>JSON: {feature: forecast} when FeatureCode == "*".</summary>
    [MaxLength(4096)]
    public string? PerFeatureJson { get; set; }

    /// <summary>Suggested top-up amount (credits) if predicted run-out is within window.</summary>
    public int SuggestedTopUpCredits { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
