using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class LearnerSkillProfile
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(32)]
    public string CriterionCode { get; set; } = default!;

    public double CurrentRating { get; set; } = 1500;     // Elo-like rating
    public int ConfidenceLevel { get; set; }               // 0-100, increases with more data
    public int EvidenceCount { get; set; }
    public string RecentScoresJson { get; set; } = "[]";   // Last 20 scores for trend
    public DateTimeOffset LastUpdatedAt { get; set; }
}
