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

    /// <summary>FK to the CMS-authored <see cref="ConversationTemplate"/> this session was built from.</summary>
    [MaxLength(64)]
    public string? TemplateId { get; set; }

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = "speaking";

    /// <summary>Canonical task type: <c>oet-roleplay</c> | <c>oet-handover</c>.</summary>
    [MaxLength(64)]
    public string TaskTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string Profession { get; set; } = "medicine";

    public string ScenarioJson { get; set; } = "{}";       // Scenario card / interview topic

    [MaxLength(32)]
    public string State { get; set; } = "preparing";       // "preparing", "active", "completed", "abandoned", "evaluating", "evaluated", "failed"

    public int TurnCount { get; set; }
    public int DurationSeconds { get; set; }
    public string TranscriptJson { get; set; } = "[]";     // Full conversation transcript

    [MaxLength(64)]
    public string? EvaluationId { get; set; }

    /// <summary>Last non-fatal error emitted to the client, if any. For telemetry.</summary>
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
    public string Role { get; set; } = default!;           // "learner", "ai", "system"

    public string Content { get; set; } = default!;        // Transcript text

    /// <summary>URL (via IFileStorage) to the turn's audio (learner mic capture OR AI TTS).
    /// Retention: swept by <c>ConversationAudioRetentionWorker</c>.</summary>
    [MaxLength(512)]
    public string? AudioUrl { get; set; }

    public int DurationMs { get; set; }
    public int TimestampMs { get; set; }                   // Offset from session start
    public double? ConfidenceScore { get; set; }           // STT confidence

    public string AnalysisJson { get; set; } = "{}";       // Per-turn analysis

    /// <summary>AI feature code this turn's AI call was billed under, if role='ai'.</summary>
    [MaxLength(64)]
    public string? AiFeatureCode { get; set; }

    /// <summary>Usage id correlating to <see cref="AiUsageRecord"/>.</summary>
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

    /// <summary>Canonical task type: <c>oet-roleplay</c> | <c>oet-handover</c>.</summary>
    [MaxLength(64)]
    public string TaskTypeCode { get; set; } = "oet-roleplay";

    public string Scenario { get; set; } = default!;

    [MaxLength(512)]
    public string? RoleDescription { get; set; }

    public string? PatientContext { get; set; }

    public string? ExpectedOutcomes { get; set; }

    /// <summary>JSON array of objectives the learner must cover.</summary>
    public string ObjectivesJson { get; set; } = "[]";

    /// <summary>JSON array of red-flag cues the learner should recognise.</summary>
    public string ExpectedRedFlagsJson { get; set; } = "[]";

    /// <summary>JSON array of key clinical vocabulary for the scenario.</summary>
    public string KeyVocabularyJson { get; set; } = "[]";

    /// <summary>JSON describing the patient/colleague voice (gender, age, accent, tone, provider voice id).</summary>
    public string PatientVoiceJson { get; set; } = "{}";

    [MaxLength(16)]
    public string Difficulty { get; set; } = "medium";

    public int EstimatedDurationSeconds { get; set; } = 300;

    /// <summary>CMS lifecycle: <c>draft|published|archived</c>.</summary>
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

/// <summary>
/// Per-session AI-graded evaluation. Replaces the hardcoded literal that
/// <see cref="Services.ConversationService"/> used to return. Written by the
/// background evaluator after a session is completed.
/// </summary>
public class ConversationEvaluation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SessionId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>Overall OET Speaking scaled score (0–500). Projected from the 4-criterion
    /// mean on 0–6 via <see cref="Services.OetScoring.ConversationProjectedScaled"/>.</summary>
    public int OverallScaled { get; set; }

    /// <summary>Grade letter (A / B / C+ / C / D / E) from <see cref="Services.OetScoring.OetGradeLetterFromScaled"/>.</summary>
    [MaxLength(4)]
    public string OverallGrade { get; set; } = "E";

    public bool Passed { get; set; }

    /// <summary>ISO alpha-2 country code at time of grading. Speaking is country-independent
    /// (always 350), but we persist it for audit.</summary>
    [MaxLength(4)]
    public string? CountryVariant { get; set; }

    /// <summary>JSON array of criterion rows: [{id, score06, evidence, quotes[]}].</summary>
    public string CriteriaJson { get; set; } = "[]";

    /// <summary>JSON array of learner-facing strengths.</summary>
    public string StrengthsJson { get; set; } = "[]";

    /// <summary>JSON array of learner-facing improvements.</summary>
    public string ImprovementsJson { get; set; } = "[]";

    /// <summary>JSON array of suggested practice items (drills, topics).</summary>
    public string SuggestedPracticeJson { get; set; } = "[]";

    /// <summary>JSON array of rule IDs applied during evaluation.</summary>
    public string AppliedRuleIdsJson { get; set; } = "[]";

    /// <summary>Rulebook version used at evaluation time.</summary>
    [MaxLength(32)]
    public string RulebookVersion { get; set; } = "";

    /// <summary>Human-readable advisory banner: "AI-generated — advisory only".</summary>
    [MaxLength(512)]
    public string? Advisory { get; set; }

    /// <summary>AI usage record id correlating to <see cref="AiUsageRecord"/>.</summary>
    [MaxLength(64)]
    public string? AiUsageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Per-turn annotation emitted by evaluation (e.g. "turn 3 — good open question / cite C04.1").
/// Indexed (SessionId, TurnNumber). Consumed by the results UI and by
/// <c>ReviewItemSeeder.SeedConversationIssueAsync</c>.
/// </summary>
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

    /// <summary>strength | error | improvement</summary>
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
