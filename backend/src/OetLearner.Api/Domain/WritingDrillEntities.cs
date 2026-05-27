using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class WritingDrill
{
    public Guid Id { get; set; }

    [MaxLength(32)]
    public string DrillType { get; set; } = default!;

    [MaxLength(4)]
    public string TargetSubSkill { get; set; } = default!;

    [MaxLength(16)]
    public string? TargetCanonRuleId { get; set; }

    public string AppliesToProfessionsJson { get; set; } = "[]";

    public string AppliesToLetterTypesJson { get; set; } = "[]";

    public int Difficulty { get; set; } = 1;

    public string PromptMarkdown { get; set; } = default!;

    public string? ExpectedAnswer { get; set; }

    public string AlternativesJson { get; set; } = "[]";

    [MaxLength(16)]
    public string GradingMethod { get; set; } = "exact";

    public string GradingConfigJson { get; set; } = "{}";

    [MaxLength(16)]
    public string Status { get; set; } = "draft";

    public DateTimeOffset CreatedAt { get; set; }
}

public class WritingDrillAttempt
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public Guid DrillId { get; set; }

    public string? ResponseText { get; set; }

    public bool IsCorrect { get; set; }

    public string? FeedbackText { get; set; }

    public int? TimeSpentSeconds { get; set; }

    public double EaseFactor { get; set; } = 2.5;

    public int IntervalDays { get; set; } = 1;

    public int Repetitions { get; set; }

    public DateTimeOffset? NextDueAt { get; set; }

    public DateTimeOffset AttemptedAt { get; set; }
}
