using System.ComponentModel.DataAnnotations;

namespace OetWithDrHesham.Api.Domain;

public class WritingReadinessScore
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public DateOnly Date { get; set; }

    public int Score { get; set; }

    public decimal? MockAverageBand { get; set; }

    public decimal? TrajectorySlope { get; set; }

    public decimal? CanonCleanRate { get; set; }

    public int? TimeMgmtScore { get; set; }

    public int? TypeConsistency { get; set; }

    [MaxLength(8)]
    public string? PredictedBandLabel { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
