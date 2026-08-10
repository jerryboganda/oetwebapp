using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public enum WritingAssessmentV11Status
{
    AwaitingPreflight,
    BlockedMissingInput,
    RequiresReview,
    BlockedReleaseGate,
    AwaitingReprocess,
    RestrictedCalibration,
    CandidateReady,
    HeldForReview,
}

public enum WritingAssessmentReleaseStatus
{
    Blocked,
    Draft,
    Approved,
    Retired,
}

public enum WritingAssessmentModelAnswerStatus
{
    HeldForReview,
    Ready,
    Rejected,
}

public static class WritingAssessmentV11Invariants
{
    private static readonly HashSet<string> PrimaryCriteria = new(StringComparer.Ordinal)
    {
        "purpose",
        "content",
        "conciseness_clarity",
        "genre_style",
        "organisation_layout",
        "language",
    };

    public static bool IsPrimaryCriterion(string? criterionCode)
        => !string.IsNullOrWhiteSpace(criterionCode)
            && PrimaryCriteria.Contains(criterionCode.Trim().ToLowerInvariant());

    public static IReadOnlySet<string> SupportedPrimaryCriteria => PrimaryCriteria;
}

/// <summary>
/// Append-only v1.1 report snapshot linked to one immutable Writing submission.
/// Candidate output is enabled only after the governed release gate allows it.
/// </summary>
public class WritingAssessmentReportV11
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }

    [MaxLength(32)]
    public WritingAssessmentV11Status Status { get; set; } = WritingAssessmentV11Status.AwaitingPreflight;

    [MaxLength(64)]
    public string Profession { get; set; } = string.Empty;

    [MaxLength(64)]
    public string LetterType { get; set; } = string.Empty;

    [MaxLength(64)]
    public string RulePackVersion { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ModelVersion { get; set; } = string.Empty;

    [MaxLength(64)]
    public string CalibrationSetVersion { get; set; } = string.Empty;

    [MaxLength(64)]
    public string OriginalLetterHash { get; set; } = string.Empty;

    public string OriginalLetterSnapshot { get; set; } = string.Empty;
    public string TaskSnapshot { get; set; } = string.Empty;
    public string CaseNotesSnapshot { get; set; } = string.Empty;
    public string? ClassificationJson { get; set; }
    public string? FeatureRecordJson { get; set; }
    public string TopPrioritiesJson { get; set; } = "[]";
    public string StrengthsJson { get; set; } = "[]";
    public string StudyPlanJson { get; set; } = "[]";

    public short? PurposeScore { get; set; }
    public short? ContentScore { get; set; }
    public short? ConcisenessClarityScore { get; set; }
    public short? GenreStyleScore { get; set; }
    public short? OrganisationLayoutScore { get; set; }
    public short? LanguageScore { get; set; }
    public int? EstimatedPracticeScore { get; set; }

    [MaxLength(32)]
    public string? ScoreRange { get; set; }

    [MaxLength(32)]
    public string? ConfidenceLabel { get; set; }

    [MaxLength(64)]
    public string? ConfidenceRange { get; set; }

    public bool CandidateNumericScoreEnabled { get; set; }
    public bool CandidateReportVisible { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<WritingAssessmentFactEvidence> Facts { get; set; } = new List<WritingAssessmentFactEvidence>();
    public ICollection<WritingAssessmentError> Errors { get; set; } = new List<WritingAssessmentError>();
    public ICollection<WritingAssessmentCriterionEvidence> Criteria { get; set; } = new List<WritingAssessmentCriterionEvidence>();
}

public class WritingAssessmentFactEvidence
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }

    [MaxLength(32)]
    public string Classification { get; set; } = string.Empty;

    public string FactText { get; set; } = string.Empty;

    [MaxLength(128)]
    public string SourceReference { get; set; } = string.Empty;

    [MaxLength(32)]
    public string CandidateStatus { get; set; } = string.Empty;

    public string? CandidateExcerpt { get; set; }
    public string? Explanation { get; set; }
}

public class WritingAssessmentError
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }

    [MaxLength(64)]
    public string Category { get; set; } = string.Empty;

    public string? Location { get; set; }
    public string? CandidateWording { get; set; }
    public string? Correction { get; set; }

    [MaxLength(128)]
    public string? RuleSource { get; set; }

    public string? WhyItMatters { get; set; }

    [MaxLength(16)]
    public string Severity { get; set; } = "minor";

    [MaxLength(16)]
    public string Confidence { get; set; } = "medium";

    [MaxLength(32)]
    public string PrimaryCriterionCode { get; set; } = string.Empty;

    public string SecondaryCriterionCodesJson { get; set; } = "[]";
    public int? StartOffset { get; set; }
    public int? EndOffset { get; set; }
    public bool IsGroupedDuplicate { get; set; }
}

public class WritingAssessmentCriterionEvidence
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }

    [MaxLength(32)]
    public string CriterionCode { get; set; } = string.Empty;

    public short Score { get; set; }
    public short MaximumScore { get; set; }
    public string StrengthObservation { get; set; } = string.Empty;
    public string LimitationObservation { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "[]";
    public string ImprovementAction { get; set; } = string.Empty;
}

public class WritingAssessmentReleaseGate
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string ModelVersion { get; set; } = string.Empty;

    [MaxLength(64)]
    public string CalibrationSetVersion { get; set; } = string.Empty;

    public WritingAssessmentReleaseStatus Status { get; set; } = WritingAssessmentReleaseStatus.Blocked;
    public bool CandidateNumericScoreEnabled { get; set; }
    public decimal? MeanAbsoluteError { get; set; }
    public decimal? ContentConcisenessCorrelation { get; set; }
    public decimal? LanguageCorrelation { get; set; }
    public decimal? InventedClaimRate { get; set; }
    public decimal? OwnerApprovedTolerance { get; set; }
    public int QualifiedReviewerCount { get; set; }
    public int HumanRatingsPerBenchmark { get; set; }
    public string? ApprovalEvidenceJson { get; set; }

    [MaxLength(64)]
    public string? ApprovedByUserId { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class WritingAssessmentPackVersion
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string Profession { get; set; } = string.Empty;

    [MaxLength(64)]
    public string LetterType { get; set; } = string.Empty;

    [MaxLength(64)]
    public string VersionKey { get; set; } = string.Empty;

    public WritingAssessmentReleaseStatus Status { get; set; } = WritingAssessmentReleaseStatus.Blocked;
    public bool CandidateFacing { get; set; }
    public string RulesJson { get; set; } = "{}";
    public string? ApprovalEvidenceJson { get; set; }

    [MaxLength(64)]
    public string? ApprovedByUserId { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class WritingAssessmentModelAnswer
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public WritingAssessmentModelAnswerStatus Status { get; set; } = WritingAssessmentModelAnswerStatus.HeldForReview;
    public bool IsCandidateVisible { get; set; }
    public string? ModelAnswerText { get; set; }
    public string? CorrectedCandidateLetter { get; set; }
    public string? WhyThisWorksJson { get; set; }
    public string GroundedFactReferencesJson { get; set; } = "[]";
    public string? HoldReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
