using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class WritingTutorReview
{
    public Guid Id { get; set; }

    public Guid SubmissionId { get; set; }

    [MaxLength(64)]
    public string TutorId { get; set; } = default!;

    [MaxLength(16)]
    public string Status { get; set; } = "claimed";

    public string FreeTextFeedback { get; set; } = string.Empty;

    public string PerCriterionCommentsJson { get; set; } = "{}";

    public string? ScoreOverrideJson { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // ── Double-marking + content-checklist marking (spec §12/§14.4) ────────────
    // Added by AddWritingExamModuleClosure.

    /// <summary>first | second | senior — ordinal of this marker in a double-marking flow.</summary>
    [MaxLength(16)]
    public string MarkerSequence { get; set; } = "first";

    /// <summary>True once the tutor has worked through the task content checklist.</summary>
    public bool IsContentChecklistMarked { get; set; }

    /// <summary>
    /// Per-checklist-item verdict JSON keyed by checklist item id:
    /// { "&lt;id&gt;": "included" | "missing" | "inaccurate" | "irrelevant" } (spec §14.2).
    /// </summary>
    public string ContentChecklistVerdictJson { get; set; } = "{}";

    /// <summary>AI pre-assessment the tutor accepted/edited, for audit (spec §13.2).</summary>
    public string? AcceptedAiPreAssessmentJson { get; set; }
}

public class WritingTutorReviewAssignment
{
    public Guid Id { get; set; }

    public Guid SubmissionId { get; set; }

    [MaxLength(64)]
    public string TutorId { get; set; } = default!;

    public DateTimeOffset ClaimedAt { get; set; }

    public DateTimeOffset DueAt { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "claimed";

    public DateTimeOffset? ReleasedAt { get; set; }
}

public class WritingTutorCalibration
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string TutorId { get; set; } = default!;

    public decimal AgreementCoefficient { get; set; }

    public int SamplesReviewed { get; set; }

    public DateTimeOffset LastCalibratedAt { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";
}
