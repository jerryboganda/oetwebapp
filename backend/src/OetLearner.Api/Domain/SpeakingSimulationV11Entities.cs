using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public enum SpeakingSimulationV11ReleaseStatus
{
    Draft = 0,
    Approved = 1,
    Retired = 2,
}

public enum SpeakingSimulationV11ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}

public enum SpeakingSimulationV11AssessmentStatus
{
    Pending = 0,
    Complete = 1,
    TechnicalReview = 2,
    Invalid = 3,
}

public enum SpeakingSimulationV11AudioQualityStatus
{
    Pending = 0,
    Passed = 1,
    NeedsReview = 2,
    Failed = 3,
}

public class SpeakingSimulationV11SpecRelease
{
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SpecVersion { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ReleaseVersion { get; set; } = string.Empty;

    public SpeakingSimulationV11ReleaseStatus Status { get; set; } = SpeakingSimulationV11ReleaseStatus.Draft;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SpeakingSimulationV11RubricRelease
{
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string RubricVersion { get; set; } = string.Empty;

    [MaxLength(64)]
    public string CalibrationVersion { get; set; } = string.Empty;

    public SpeakingSimulationV11ReleaseStatus Status { get; set; } = SpeakingSimulationV11ReleaseStatus.Draft;
    public string CriteriaJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SpeakingSimulationV11OwnerApproval
{
    [MaxLength(128)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ApprovalKey { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ScopeKey { get; set; } = "global";

    [MaxLength(64)]
    public string? SpecVersion { get; set; }

    [MaxLength(64)]
    public string? RubricVersion { get; set; }

    public SpeakingSimulationV11ApprovalStatus Status { get; set; } = SpeakingSimulationV11ApprovalStatus.Pending;
    public decimal? NumericValue { get; set; }
    public string? EvidenceJson { get; set; }

    [MaxLength(64)]
    public string? ApprovedByUserId { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SpeakingSimulationV11Assessment
{
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string? ExamSessionId { get; set; }

    [MaxLength(64)]
    public string? SpeakingSessionId { get; set; }

    [MaxLength(64)]
    public string? RolePlayCardId { get; set; }

    [MaxLength(32)]
    public string ProfessionId { get; set; } = "medicine";

    [MaxLength(64)]
    public string SpecVersion { get; set; } = string.Empty;

    [MaxLength(64)]
    public string RubricVersion { get; set; } = string.Empty;

    [MaxLength(64)]
    public string CalibrationVersion { get; set; } = string.Empty;

    public SpeakingSimulationV11AssessmentStatus Status { get; set; } = SpeakingSimulationV11AssessmentStatus.Pending;
    public SpeakingSimulationV11AudioQualityStatus AudioQualityStatus { get; set; } = SpeakingSimulationV11AudioQualityStatus.Pending;
    public decimal? ConfidenceScore { get; set; }

    [MaxLength(32)]
    public string? ConfidenceLabel { get; set; }

    [MaxLength(64)]
    public string? ConfidenceRange { get; set; }

    public string GraphDisclaimer { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<SpeakingSimulationV11PersonaSnapshot> PersonaSnapshots { get; set; } = new List<SpeakingSimulationV11PersonaSnapshot>();
    public ICollection<SpeakingSimulationV11Evidence> Evidence { get; set; } = new List<SpeakingSimulationV11Evidence>();
    public ICollection<SpeakingSimulationV11CriterionScore> CriterionScores { get; set; } = new List<SpeakingSimulationV11CriterionScore>();
    public ICollection<SpeakingSimulationV11TurnMetric> TurnMetrics { get; set; } = new List<SpeakingSimulationV11TurnMetric>();
}

public class SpeakingSimulationV11PersonaSnapshot
{
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AssessmentId { get; set; } = default!;

    [MaxLength(64)]
    public string? RolePlayCardId { get; set; }

    [MaxLength(32)]
    public string PersonaRole { get; set; } = string.Empty;

    [MaxLength(200)]
    public string ScenarioTitle { get; set; } = string.Empty;

    [MaxLength(160)]
    public string Setting { get; set; } = string.Empty;

    [MaxLength(256)]
    public string CandidateRole { get; set; } = string.Empty;

    [MaxLength(256)]
    public string InterlocutorRole { get; set; } = string.Empty;

    [MaxLength(256)]
    public string PatientEmotion { get; set; } = string.Empty;

    [MaxLength(256)]
    public string CommunicationGoal { get; set; } = string.Empty;

    [MaxLength(256)]
    public string ClinicalTopic { get; set; } = string.Empty;

    public string PersonaJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SpeakingSimulationV11Evidence
{
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AssessmentId { get; set; } = default!;

    [MaxLength(32)]
    public string PrimaryCriterionCode { get; set; } = string.Empty;

    [MaxLength(32)]
    public string CriterionCode { get; set; } = string.Empty;

    [MaxLength(32)]
    public string EvidenceType { get; set; } = string.Empty;

    public int? TurnNumber { get; set; }

    [MaxLength(256)]
    public string? SourceReference { get; set; }

    public string QuoteText { get; set; } = string.Empty;
    public int? StartMs { get; set; }
    public int? EndMs { get; set; }
    [MaxLength(32)]
    public string EvidenceStatus { get; set; } = "supported";

    public string? FindingText { get; set; }
    public string? ActionSuggestion { get; set; }

    [MaxLength(32)]
    public string? ConfidenceLabel { get; set; }
    public decimal? ConfidenceScore { get; set; }
    public bool IsPrimary { get; set; } = true;

    [MaxLength(64)]
    public string? SourceTranscriptId { get; set; }
    [MaxLength(64)]
    public string? SourceRecordingId { get; set; }
    [MaxLength(64)]
    public string? CardVersion { get; set; }

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SpeakingSimulationV11CriterionScore
{
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AssessmentId { get; set; } = default!;

    [MaxLength(32)]
    public string CriterionCode { get; set; } = string.Empty;

    public int Weight { get; set; }
    public decimal RawScore { get; set; }
    public decimal WeightedScore { get; set; }

    [MaxLength(32)]
    public string? ScoreBand { get; set; }

    public string? Rationale { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SpeakingSimulationV11TurnMetric
{
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AssessmentId { get; set; } = default!;

    public int TurnNumber { get; set; }

    [MaxLength(128)]
    public string ModelName { get; set; } = string.Empty;

    public int PromptLatencyMs { get; set; }
    public int CompletionLatencyMs { get; set; }
    public int TotalLatencyMs { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}
