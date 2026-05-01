using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>Content revision snapshot for version history.</summary>
public class ContentRevision
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ContentItemId { get; set; } = default!;

    public int RevisionNumber { get; set; }

    [MaxLength(32)]
    public string State { get; set; } = "saved";

    [MaxLength(512)]
    public string? ChangeNote { get; set; }

    /// <summary>Full JSON snapshot of the content item at this revision.</summary>
    public string SnapshotJson { get; set; } = "{}";

    [MaxLength(128)]
    public string CreatedBy { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Versioned AI evaluation configuration.</summary>
public class AIConfigVersion
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Model { get; set; } = default!;

    [MaxLength(64)]
    public string Provider { get; set; } = default!;

    [MaxLength(64)]
    public string TaskType { get; set; } = default!;

    public AIConfigStatus Status { get; set; } = AIConfigStatus.Testing;

    public double Accuracy { get; set; }

    public double ConfidenceThreshold { get; set; }

    [MaxLength(512)]
    public string? RoutingRule { get; set; }

    [MaxLength(128)]
    public string? ExperimentFlag { get; set; }

    [MaxLength(128)]
    public string PromptLabel { get; set; } = default!;

    [MaxLength(512)]
    public string? ChangeNote { get; set; }

    [MaxLength(128)]
    public string CreatedBy { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Feature flag for rollout and kill-switch management.</summary>
public class FeatureFlag
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(128)]
    public string Key { get; set; } = default!;

    public FeatureFlagType FlagType { get; set; } = FeatureFlagType.Release;

    public bool Enabled { get; set; }

    public int RolloutPercentage { get; set; }

    [MaxLength(512)]
    public string? Description { get; set; }

    [MaxLength(128)]
    public string? Owner { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Audit trail for all admin-initiated changes.</summary>
public class AuditEvent
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    public DateTimeOffset OccurredAt { get; set; }

    [MaxLength(64)]
    public string ActorId { get; set; } = default!;

    [MaxLength(64)]
    public string? ActorAuthAccountId { get; set; }

    [MaxLength(128)]
    public string ActorName { get; set; } = default!;

    [MaxLength(128)]
    public string Action { get; set; } = default!;

    [MaxLength(64)]
    public string ResourceType { get; set; } = default!;

    [MaxLength(64)]
    public string? ResourceId { get; set; }

    [MaxLength(1024)]
    public string? Details { get; set; }

    public ApplicationUserAccount? ActorAuthAccount { get; set; }
}

/// <summary>Content publish request requiring multi-stage approval before going live.</summary>
public class ContentPublishRequest
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ContentItemId { get; set; } = default!;

    [MaxLength(64)]
    public string RequestedBy { get; set; } = default!;

    [MaxLength(128)]
    public string RequestedByName { get; set; } = default!;

    [MaxLength(64)]
    public string? ReviewedBy { get; set; }

    [MaxLength(128)]
    public string? ReviewedByName { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "pending";
    // pending | editor_review | publisher_approval | approved | rejected

    /// <summary>Current approval stage: editor_review | publisher_approval</summary>
    [MaxLength(32)]
    public string Stage { get; set; } = "editor_review";

    [MaxLength(512)]
    public string? RequestNote { get; set; }

    [MaxLength(512)]
    public string? ReviewNote { get; set; }

    // ── Editor Review Fields ──
    [MaxLength(64)]
    public string? EditorReviewedBy { get; set; }

    [MaxLength(128)]
    public string? EditorReviewedByName { get; set; }

    public DateTimeOffset? EditorReviewedAt { get; set; }

    [MaxLength(512)]
    public string? EditorNotes { get; set; }

    // ── Publisher Approval Fields ──
    [MaxLength(64)]
    public string? PublisherApprovedBy { get; set; }

    [MaxLength(128)]
    public string? PublisherApprovedByName { get; set; }

    public DateTimeOffset? PublisherApprovedAt { get; set; }

    [MaxLength(512)]
    public string? PublisherNotes { get; set; }

    // ── Rejection Fields ──
    [MaxLength(64)]
    public string? RejectedBy { get; set; }

    [MaxLength(128)]
    public string? RejectedByName { get; set; }

    public DateTimeOffset? RejectedAt { get; set; }

    [MaxLength(512)]
    public string? RejectionReason { get; set; }

    /// <summary>Which stage the rejection occurred at: editor_review | publisher_approval</summary>
    [MaxLength(32)]
    public string? RejectionStage { get; set; }

    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

/// <summary>Expert review escalation when AI and human scores significantly diverge.</summary>
public class ReviewEscalation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewRequestId { get; set; } = default!;

    [MaxLength(64)]
    public string OriginalReviewerId { get; set; } = default!;

    [MaxLength(64)]
    public string? SecondReviewerId { get; set; }

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(64)]
    public string TriggerCriterion { get; set; } = default!;

    public int AiScore { get; set; }

    public int HumanScore { get; set; }

    public int Divergence { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "pending";
    // pending | assigned | resolved

    [MaxLength(512)]
    public string? ResolutionNote { get; set; }

    public int? FinalScore { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>Learner-initiated escalation/dispute of a review decision.</summary>
public class LearnerEscalation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string SubmissionId { get; set; } = default!;

    [MaxLength(128)]
    public string Reason { get; set; } = default!;

    [MaxLength(2000)]
    public string Details { get; set; } = default!;

    [MaxLength(32)]
    public string Status { get; set; } = "Pending";
    // Pending | InReview | Resolved | Rejected

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Subscription plan definition for billing administration.</summary>
public class BillingPlan
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(1024)]
    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "USD";

    [MaxLength(16)]
    public string Interval { get; set; } = "month";

    public int DurationMonths { get; set; } = 1;

    public bool IsVisible { get; set; } = true;

    public bool IsRenewable { get; set; } = true;

    public int TrialDays { get; set; }

    public int DisplayOrder { get; set; }

    public int IncludedCredits { get; set; }

    /// <summary>
    /// Mocks V2 Wave 7 — Diagnostic-mock entitlement. Configurable per plan.
    /// Values: <c>unlimited</c>, <c>one_per_lifetime</c>, <c>one_per_renewal_period</c>,
    /// <c>paid_per_use</c>, <c>disabled</c>.
    /// </summary>
    [MaxLength(32)]
    public string DiagnosticMockEntitlement { get; set; } = "one_per_lifetime";

    [MaxLength(2048)]
    public string IncludedSubtestsJson { get; set; } = "[]";

    [MaxLength(2048)]
    public string EntitlementsJson { get; set; } = "{}";

    [MaxLength(64)]
    public string? ActiveVersionId { get; set; }

    [MaxLength(64)]
    public string? LatestVersionId { get; set; }

    public int ActiveSubscribers { get; set; }

    public BillingPlanStatus Status { get; set; } = BillingPlanStatus.Active;

    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Immutable billing plan catalog snapshot.</summary>
public class BillingPlanVersion
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string PlanId { get; set; } = default!;

    public int VersionNumber { get; set; }

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(1024)]
    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "USD";

    [MaxLength(16)]
    public string Interval { get; set; } = "month";

    public int DurationMonths { get; set; } = 1;

    public bool IsVisible { get; set; } = true;

    public bool IsRenewable { get; set; } = true;

    public int TrialDays { get; set; }

    public int DisplayOrder { get; set; }

    public int IncludedCredits { get; set; }

    [MaxLength(2048)]
    public string IncludedSubtestsJson { get; set; } = "[]";

    [MaxLength(2048)]
    public string EntitlementsJson { get; set; } = "{}";

    public BillingPlanStatus Status { get; set; } = BillingPlanStatus.Active;

    public DateTimeOffset? ArchivedAt { get; set; }

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }

    [MaxLength(128)]
    public string? CreatedByAdminName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Admin-initiated AI content generation job.</summary>
public class ContentGenerationJob
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string RequestedBy { get; set; } = default!;    // Admin user ID

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(64)]
    public string? TaskTypeId { get; set; }

    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    [MaxLength(16)]
    public string Difficulty { get; set; } = "medium";

    public int RequestedCount { get; set; } = 1;
    public int GeneratedCount { get; set; }

    public string PromptConfigJson { get; set; } = "{}";   // Custom generation parameters
    public string GeneratedContentIdsJson { get; set; } = "[]";

    [MaxLength(32)]
    public string State { get; set; } = "pending";         // "pending", "generating", "completed", "failed"

    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
