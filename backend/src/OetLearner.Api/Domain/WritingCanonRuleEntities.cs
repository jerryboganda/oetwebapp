using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class WritingCanonRule
{
    [MaxLength(16)]
    public string Id { get; set; } = default!;

    [MaxLength(32)]
    public string Category { get; set; } = default!;

    public string AppliesToLetterTypesJson { get; set; } = "[]";

    public string AppliesToProfessionsJson { get; set; } = "[]";

    [MaxLength(8)]
    public string Severity { get; set; } = "medium";

    [MaxLength(1000)]
    public string RuleText { get; set; } = default!;

    public string CorrectExamplesJson { get; set; } = "[]";

    public string IncorrectExamplesJson { get; set; } = "[]";

    [MaxLength(16)]
    public string DetectionType { get; set; } = "regex";

    public string DetectionConfigJson { get; set; } = "{}";

    public Guid? LessonId { get; set; }

    public int Version { get; set; } = 1;

    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public class WritingCanonViolation
{
    public Guid Id { get; set; }

    public Guid SubmissionId { get; set; }

    [MaxLength(16)]
    public string RuleId { get; set; } = default!;

    [MaxLength(8)]
    public string Severity { get; set; } = "medium";

    [MaxLength(500)]
    public string? Snippet { get; set; }

    public int? LineNumber { get; set; }

    public int? CharStart { get; set; }

    public int? CharEnd { get; set; }

    [MaxLength(500)]
    public string? SuggestedFix { get; set; }

    public bool Disputed { get; set; }

    [MaxLength(500)]
    public string? DisputeResolution { get; set; }

    public DateTimeOffset DetectedAt { get; set; }
}
