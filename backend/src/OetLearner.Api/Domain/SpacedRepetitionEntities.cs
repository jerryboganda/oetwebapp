using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class ReviewItem
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SourceType { get; set; } = default!;    // "evaluation_issue", "vocabulary", "grammar_error", "pronunciation"

    [MaxLength(64)]
    public string? SourceId { get; set; }                  // Reference to originating entity

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(32)]
    public string? CriterionCode { get; set; }

    public string QuestionJson { get; set; } = "{}";       // What to show (the mistake or concept)
    public string AnswerJson { get; set; } = "{}";         // Correct version + explanation

    public double EaseFactor { get; set; } = 2.5;          // SM-2 ease factor
    public int IntervalDays { get; set; } = 1;             // Current interval
    public int ReviewCount { get; set; }
    public int ConsecutiveCorrect { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTimeOffset? LastReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";         // "active", "mastered", "suspended"

    /// <summary>Recalls v1: learner-applied star flag. Mirrors LearnerVocabulary.Starred.</summary>
    public bool Starred { get; set; }

    /// <summary>Optional star reason: spelling | pronunciation | meaning | hearing | confused.</summary>
    [MaxLength(16)]
    public string? StarReason { get; set; }
}
