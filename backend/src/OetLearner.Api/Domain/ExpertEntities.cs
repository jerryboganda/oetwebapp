using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

[Index(nameof(AuthAccountId), IsUnique = true)]
public class ExpertUser
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string? AuthAccountId { get; set; }

    [MaxLength(32)]
    public string Role { get; set; } = ApplicationUserRoles.Expert;

    [MaxLength(128)]
    public string DisplayName { get; set; } = default!;

    [MaxLength(256)]
    public string Email { get; set; } = default!;

    public string SpecialtiesJson { get; set; } = "[]";

    [MaxLength(64)]
    public string Timezone { get; set; } = "UTC";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public ApplicationUserAccount? AuthAccount { get; set; }
}

public class ExpertReviewAssignment
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewRequestId { get; set; } = default!;

    [MaxLength(64)]
    public string? AssignedReviewerId { get; set; }

    [MaxLength(64)]
    public string? AssignedBy { get; set; }

    public DateTimeOffset? AssignedAt { get; set; }

    public ExpertAssignmentState ClaimState { get; set; } = ExpertAssignmentState.Unassigned;

    public DateTimeOffset? ReleasedAt { get; set; }

    [MaxLength(64)]
    public string? ReassignedFrom { get; set; }

    [MaxLength(128)]
    public string? ReasonCode { get; set; }
}

public class ExpertReviewDraft
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewRequestId { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewerId { get; set; } = default!;

    public int Version { get; set; } = 1;

    [MaxLength(32)]
    public string State { get; set; } = "editing";

    public string RubricEntriesJson { get; set; } = "{}";

    public string CriterionCommentsJson { get; set; } = "{}";

    public string AnchoredCommentsJson { get; set; } = "[]";

    public string TimestampCommentsJson { get; set; } = "[]";

    public string FinalCommentDraft { get; set; } = string.Empty;

    public string ScratchpadJson { get; set; } = "\"\"";

    public string ChecklistItemsJson { get; set; } = "[]";

    public DateTimeOffset DraftSavedAt { get; set; }

    [MaxLength(64)]
    public string? AutosaveErrorState { get; set; }
}

public class ExpertCalibrationCase
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(32)]
    public string ProfessionId { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(128)]
    public string BenchmarkLabel { get; set; } = default!;

    public string CaseArtifactsJson { get; set; } = "{}";

    public string ReferenceRubricJson { get; set; } = "{}";

    public string ReferenceNotesJson { get; set; } = "{}";

    [MaxLength(32)]
    public string Difficulty { get; set; } = "medium";

    public CalibrationCaseStatus Status { get; set; } = CalibrationCaseStatus.Pending;

    public int BenchmarkScore { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class ExpertCalibrationResult
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string CalibrationCaseId { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewerId { get; set; } = default!;

    public string SubmittedRubricJson { get; set; } = "{}";

    public int ReviewerScore { get; set; }

    public double AlignmentScore { get; set; }

    public string DisagreementSummary { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public DateTimeOffset SubmittedAt { get; set; }

    /// <summary>
    /// True when the reviewer saved a draft and has not yet submitted.
    /// Drafts never contribute to alignment/history metrics and can be overwritten or discarded.
    /// </summary>
    public bool IsDraft { get; set; }

    /// <summary>
    /// Last mutation timestamp; distinct from <see cref="SubmittedAt"/> (the moment of final submit).
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class ExpertCalibrationNote
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    public CalibrationNoteType Type { get; set; }

    public string Message { get; set; } = default!;

    [MaxLength(64)]
    public string? CaseId { get; set; }

    [MaxLength(64)]
    public string? ReviewerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class ExpertAvailability
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewerId { get; set; } = default!;

    [MaxLength(64)]
    public string Timezone { get; set; } = "UTC";

    public string DaysJson { get; set; } = "{}";

    public DateTimeOffset EffectiveFrom { get; set; }

    public DateTimeOffset? EffectiveTo { get; set; }
}

public class ExpertMetricSnapshot
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewerId { get; set; } = default!;

    public DateTimeOffset WindowStart { get; set; }

    public DateTimeOffset WindowEnd { get; set; }

    public int CompletedReviews { get; set; }

    public int DraftReviews { get; set; }

    public double AvgTurnaroundHours { get; set; }

    public double SlaHitRate { get; set; }

    public double CalibrationScore { get; set; }

    public double ReworkRate { get; set; }

    public string CompletionDataJson { get; set; } = "[]";
}

/// <summary>Reusable comment template for expert review annotations.</summary>
[Index(nameof(CreatedByExpertId))]
public class ExpertAnnotationTemplate
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string CreatedByExpertId { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(64)]
    public string CriterionCode { get; set; } = default!;

    [MaxLength(128)]
    public string Label { get; set; } = default!;

    [MaxLength(1500)]
    public string TemplateText { get; set; } = default!;

    public int UsageCount { get; set; }

    public bool IsShared { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Override or block a specific date from the expert's weekly recurring schedule.</summary>
[Index(nameof(ReviewerId))]
public class ScheduleException
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewerId { get; set; } = default!;

    public DateOnly Date { get; set; }

    /// <summary>True = day off (blocked), False = custom hours for that date.</summary>
    public bool IsBlocked { get; set; }

    [MaxLength(5)]
    public string? StartTime { get; set; }

    [MaxLength(5)]
    public string? EndTime { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Records an amendment to a submitted expert review.</summary>
[Index(nameof(ReviewRequestId))]
public class ExpertReviewAmend
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewRequestId { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewerId { get; set; } = default!;

    public string BeforeSnapshotJson { get; set; } = "{}";

    public string AfterSnapshotJson { get; set; } = "{}";

    public int AmendNumber { get; set; } = 1;

    public DateTimeOffset AmendedAt { get; set; }
}

/// <summary>Compensation rate configured per expert per subtest.</summary>
[Index(nameof(ExpertId))]
public class ExpertCompensationRate
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ExpertId { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    public long RateMinorUnits { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "GBP";

    public DateTimeOffset EffectiveFrom { get; set; }

    public DateTimeOffset? EffectiveTo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Earnings accrued per completed review.</summary>
[Index(nameof(ExpertId))]
[Index(nameof(ReviewRequestId))]
public class ExpertEarning
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ExpertId { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewRequestId { get; set; } = default!;

    public long AmountMinorUnits { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "GBP";

    [MaxLength(32)]
    public string Status { get; set; } = "pending";

    public DateTimeOffset EarnedAt { get; set; }

    public DateTimeOffset? PaidOutAt { get; set; }

    [MaxLength(64)]
    public string? PayoutId { get; set; }
}

/// <summary>Batch payout to an expert.</summary>
[Index(nameof(ExpertId))]
public class ExpertPayout
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ExpertId { get; set; } = default!;

    public long TotalAmountMinorUnits { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "GBP";

    [MaxLength(32)]
    public string Status { get; set; } = "pending";

    [MaxLength(64)]
    public string? ApprovedByAdminId { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Thread-based messaging between expert and admin team.</summary>
[Index(nameof(ExpertId))]
public class ExpertMessageThread
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ExpertId { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(32)]
    public string Status { get; set; } = "open";

    [MaxLength(64)]
    public string? LinkedReviewRequestId { get; set; }

    [MaxLength(64)]
    public string? LinkedCalibrationCaseId { get; set; }

    [MaxLength(64)]
    public string? LinkedLearnerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Individual reply within an expert-admin message thread.</summary>
[Index(nameof(ThreadId))]
public class ExpertMessageReply
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ThreadId { get; set; } = default!;

    [MaxLength(64)]
    public string AuthorId { get; set; } = default!;

    [MaxLength(32)]
    public string AuthorRole { get; set; } = default!;

    [MaxLength(128)]
    public string AuthorName { get; set; } = default!;

    [MaxLength(4000)]
    public string Body { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Per-review SLA compliance snapshot for operational visibility.</summary>
[Index(nameof(ReviewRequestId))]
public class ExpertSlaSnapshot
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewRequestId { get; set; } = default!;

    [MaxLength(64)]
    public string ExpertId { get; set; } = default!;

    public DateTimeOffset SlaDueAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public bool WasMet { get; set; }

    public double? TurnaroundHours { get; set; }

    [MaxLength(32)]
    public string SlaState { get; set; } = "on_track";

    public DateTimeOffset CreatedAt { get; set; }
}
