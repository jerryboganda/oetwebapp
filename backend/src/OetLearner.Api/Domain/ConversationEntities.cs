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
    public string? ContentId { get; set; }

    [MaxLength(64)]
    public string? TemplateId { get; set; }

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = "speaking";

    [MaxLength(64)]
    public string TaskTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string Profession { get; set; } = "medicine";

    public string ScenarioJson { get; set; } = "{}";

    [MaxLength(32)]
    public string State { get; set; } = "preparing";

    public int TurnCount { get; set; }
    public int DurationSeconds { get; set; }
    public string TranscriptJson { get; set; } = "[]";

    [MaxLength(64)]
    public string? EvaluationId { get; set; }

    [MaxLength(256)]
    public string? LastErrorCode { get; set; }

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
    public string Role { get; set; } = default!;

    public string Content { get; set; } = default!;

    [MaxLength(512)]
    public string? AudioUrl { get; set; }

    public int DurationMs { get; set; }
    public int TimestampMs { get; set; }
    public double? ConfidenceScore { get; set; }

    public string AnalysisJson { get; set; } = "{}";

    [MaxLength(64)]
    public string? AiFeatureCode { get; set; }

    [MaxLength(64)]
    public string? AiUsageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
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

    [MaxLength(64)]
    public string TaskTypeCode { get; set; } = "oet-roleplay";

    public string Scenario { get; set; } = default!;

    [MaxLength(512)]
    public string? RoleDescription { get; set; }

    public string? PatientContext { get; set; }

    public string? ExpectedOutcomes { get; set; }

    public string ObjectivesJson { get; set; } = "[]";
    public string ExpectedRedFlagsJson { get; set; } = "[]";
    public string KeyVocabularyJson { get; set; } = "[]";
    public string PatientVoiceJson { get; set; } = "{}";

    [MaxLength(16)]
    public string Difficulty { get; set; } = "medium";

    public int EstimatedDurationSeconds { get; set; } = 300;

    [MaxLength(16)]
    public string Status { get; set; } = "draft";

    public DateTimeOffset? PublishedAtUtc { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? CreatedByUserId { get; set; }

    [MaxLength(64)]
    public string? UpdatedByUserId { get; set; }
}

public class ConversationEvaluation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SessionId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int OverallScaled { get; set; }

    [MaxLength(4)]
    public string OverallGrade { get; set; } = "E";

    public bool Passed { get; set; }

    [MaxLength(4)]
    public string? CountryVariant { get; set; }

    public string CriteriaJson { get; set; } = "[]";
    public string StrengthsJson { get; set; } = "[]";
    public string ImprovementsJson { get; set; } = "[]";
    public string SuggestedPracticeJson { get; set; } = "[]";
    public string AppliedRuleIdsJson { get; set; } = "[]";

    [MaxLength(32)]
    public string RulebookVersion { get; set; } = "";

    [MaxLength(512)]
    public string? Advisory { get; set; }

    [MaxLength(64)]
    public string? AiUsageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ConversationTurnAnnotation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SessionId { get; set; } = default!;

    [MaxLength(64)]
    public string EvaluationId { get; set; } = default!;

    public int TurnNumber { get; set; }

    [MaxLength(16)]
    public string Type { get; set; } = "improvement";

    [MaxLength(64)]
    public string? Category { get; set; }

    [MaxLength(32)]
    public string? RuleId { get; set; }

    public string Evidence { get; set; } = "";
    public string? Suggestion { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
