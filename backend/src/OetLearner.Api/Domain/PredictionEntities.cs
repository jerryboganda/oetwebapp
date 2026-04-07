using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class PredictionSnapshot
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    public int PredictedScoreLow { get; set; }
    public int PredictedScoreHigh { get; set; }
    public int PredictedScoreMid { get; set; }

    [MaxLength(32)]
    public string ConfidenceLevel { get; set; } = default!; // "insufficient", "low", "moderate", "good"

    public string FactorsJson { get; set; } = "{}";         // Contributing factors breakdown
    public string TrendJson { get; set; } = "{}";           // Trend direction

    public int EvaluationCount { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}
