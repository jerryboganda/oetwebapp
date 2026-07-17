using System.ComponentModel.DataAnnotations;

namespace OetWithDrHesham.Api.Domain;

/// <summary>
/// Canonical <see cref="WritingSubmission.Status"/> values. The column is
/// <c>[MaxLength(16)]</c>, so every value here MUST be ≤ 16 chars.
/// </summary>
public static class WritingSubmissionStatuses
{
    public const string Queued = "queued";
    public const string Preflight = "preflight";
    public const string Grading = "grading";
    public const string Graded = "graded";
    public const string Failed = "failed";

    /// <summary>
    /// A MOCK submission that is awaiting human examiner marking. Mock Speaking
    /// &amp; Writing are never AI-graded — this status is set instead of running
    /// the AI rubric. 15 chars (fits the [MaxLength(16)] column).
    /// </summary>
    public const string AwaitingReview = "awaiting_review";
}

public class WritingSubmission
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public Guid ScenarioId { get; set; }

    [MaxLength(16)]
    public string Mode { get; set; } = "practice";

    public string LetterContent { get; set; } = default!;

    [MaxLength(64)]
    public string LetterContentHash { get; set; } = default!;

    public int WordCount { get; set; }

    public int TimeSpentSeconds { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }

    public bool IsRevision { get; set; }

    public Guid? OriginalSubmissionId { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "queued";

    [MaxLength(16)]
    public string GradingTier { get; set; } = "batched";

    [MaxLength(16)]
    public string InputSource { get; set; } = "typed";

    /// <summary>
    /// JSON snapshot of the learner's Case Notes PDF highlights at submit time
    /// (<c>Record&lt;pageNumber, Highlight[]&gt;</c>). Rendered read-only on the
    /// results page and in the tutor marking surface. Defaults to an empty map.
    /// </summary>
    public string CaseNoteHighlightsJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
}

public class WritingGrade
{
    public Guid Id { get; set; }

    public Guid SubmissionId { get; set; }

    public short C1Purpose { get; set; }

    public short C2Content { get; set; }

    public short C3Conciseness { get; set; }

    public short C4Genre { get; set; }

    public short C5Organisation { get; set; }

    public short C6Language { get; set; }

    public short RawTotal { get; set; }

    public int EstimatedBand { get; set; }

    [MaxLength(8)]
    public string BandLabel { get; set; } = default!;

    public string PerCriterionFeedbackJson { get; set; } = "{}";

    public string TopThreePrioritiesJson { get; set; } = "[]";

    [MaxLength(16)]
    public string? ConfidenceFlag { get; set; }

    [MaxLength(64)]
    public string ModelUsed { get; set; } = default!;

    [MaxLength(32)]
    public string CanonVersion { get; set; } = default!;

    public Guid? AppealedByGradeId { get; set; }

    public Guid? TutorReviewId { get; set; }

    public DateTimeOffset GradedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class WritingScoreAppeal
{
    public Guid Id { get; set; }

    public Guid SubmissionId { get; set; }

    public Guid OriginalGradeId { get; set; }

    public Guid? NewGradeId { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(500)]
    public string Reason { get; set; } = default!;

    [MaxLength(16)]
    public string Status { get; set; } = "pending";

    [MaxLength(16)]
    public string? Resolution { get; set; }

    [MaxLength(500)]
    public string? ResolutionNote { get; set; }

    public int? DeltaRawPoints { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
}
