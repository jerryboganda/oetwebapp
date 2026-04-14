using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class ConversationSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string? ContentId { get; set; }                 // Related speaking task

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = "speaking";

    [MaxLength(64)]
    public string TaskTypeCode { get; set; } = default!;   // "oet-roleplay", "ielts-part1", etc.

    public string ScenarioJson { get; set; } = "{}";       // Scenario card / interview topic

    [MaxLength(32)]
    public string State { get; set; } = "preparing";       // "preparing", "active", "completed", "abandoned", "evaluating", "evaluated"

    public int TurnCount { get; set; }
    public int DurationSeconds { get; set; }
    public string TranscriptJson { get; set; } = "[]";     // Full conversation transcript

    [MaxLength(64)]
    public string? EvaluationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class ConversationTurn
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string SessionId { get; set; } = default!;

    public int TurnNumber { get; set; }

    [MaxLength(16)]
    public string Role { get; set; } = default!;           // "learner", "ai", "system"

    public string Content { get; set; } = default!;        // Transcript text

    [MaxLength(256)]
    public string? AudioUrl { get; set; }

    public int DurationMs { get; set; }
    public int TimestampMs { get; set; }                   // Offset from session start
    public double? ConfidenceScore { get; set; }           // STT confidence

    public string AnalysisJson { get; set; } = "{}";       // Per-turn analysis
}

public class ConversationTemplate
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    public string Scenario { get; set; } = default!;

    [MaxLength(512)]
    public string? RoleDescription { get; set; }

    public string? PatientContext { get; set; }

    public string? ExpectedOutcomes { get; set; }

    [MaxLength(16)]
    public string Difficulty { get; set; } = "medium";

    public int EstimatedDurationMinutes { get; set; } = 5;

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
