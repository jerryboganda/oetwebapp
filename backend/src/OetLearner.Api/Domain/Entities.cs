using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class LearnerUser
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(32)]
    public string Role { get; set; } = "learner";

    [MaxLength(128)]
    public string DisplayName { get; set; } = default!;

    [MaxLength(256)]
    public string Email { get; set; } = default!;

    [MaxLength(64)]
    public string Timezone { get; set; } = "UTC";

    [MaxLength(16)]
    public string Locale { get; set; } = "en-AU";

    [MaxLength(64)]
    public string? CurrentPlanId { get; set; }

    [MaxLength(32)]
    public string? ActiveProfessionId { get; set; }

    public int OnboardingCurrentStep { get; set; } = 1;
    public int OnboardingStepCount { get; set; } = 4;
    public bool OnboardingCompleted { get; set; }
    public DateTimeOffset? OnboardingStartedAt { get; set; }
    public DateTimeOffset? OnboardingCompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActiveAt { get; set; }
}

public class LearnerGoal
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(32)]
    public string ProfessionId { get; set; } = default!;

    public DateOnly? TargetExamDate { get; set; }
    public string? OverallGoal { get; set; }
    public int? TargetWritingScore { get; set; }
    public int? TargetSpeakingScore { get; set; }
    public int? TargetReadingScore { get; set; }
    public int? TargetListeningScore { get; set; }
    public int PreviousAttempts { get; set; }
    public string WeakSubtestsJson { get; set; } = "[]";
    public int StudyHoursPerWeek { get; set; }
    public string? TargetCountry { get; set; }
    public string? TargetOrganization { get; set; }
    public string DraftStateJson { get; set; } = "{}";
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class LearnerSettings
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public string ProfileJson { get; set; } = "{}";
    public string NotificationsJson { get; set; } = "{}";
    public string PrivacyJson { get; set; } = "{}";
    public string AccessibilityJson { get; set; } = "{}";
    public string AudioJson { get; set; } = "{}";
    public string StudyJson { get; set; } = "{}";
}

public class ProfessionReference
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = default!;

    [MaxLength(32)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Label { get; set; } = default!;

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public int SortOrder { get; set; }
}

public class SubtestReference
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = default!;

    [MaxLength(32)]
    public string Code { get; set; } = default!;

    [MaxLength(64)]
    public string Label { get; set; } = default!;

    public bool SupportsProfessionSpecificContent { get; set; }
}

public class CriterionReference
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(32)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Label { get; set; } = default!;

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    public int SortOrder { get; set; }
}

public class ContentItem
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(32)]
    public string ContentType { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(32)]
    public string Difficulty { get; set; } = default!;

    public int EstimatedDurationMinutes { get; set; }
    public string CriteriaFocusJson { get; set; } = "[]";

    [MaxLength(64)]
    public string? ScenarioType { get; set; }

    public string ModeSupportJson { get; set; } = "[]";

    [MaxLength(64)]
    public string PublishedRevisionId { get; set; } = default!;

    public ContentStatus Status { get; set; }
    public string? CaseNotes { get; set; }
    public string DetailJson { get; set; } = "{}";
    public string ModelAnswerJson { get; set; } = "{}";

    [MaxLength(100)]
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

public class Attempt
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string ContentId { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(32)]
    public string Context { get; set; } = default!;

    [MaxLength(32)]
    public string Mode { get; set; } = default!;

    public AttemptState State { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int ElapsedSeconds { get; set; }
    public int DraftVersion { get; set; } = 1;

    [MaxLength(64)]
    public string? ParentAttemptId { get; set; }

    [MaxLength(64)]
    public string? ComparisonGroupId { get; set; }

    [MaxLength(32)]
    public string DeviceType { get; set; } = "web";

    public DateTimeOffset? LastClientSyncAt { get; set; }
    public string DraftContent { get; set; } = string.Empty;
    public string Scratchpad { get; set; } = string.Empty;
    public string ChecklistJson { get; set; } = "[]";
    public string AnswersJson { get; set; } = "{}";
    public UploadState AudioUploadState { get; set; }
    public string? AudioObjectKey { get; set; }
    public string AudioMetadataJson { get; set; } = "{}";
    public string TranscriptJson { get; set; } = "[]";
    public string AnalysisJson { get; set; } = "{}";
}

public class Evaluation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    public AsyncState State { get; set; }
    public string ScoreRange { get; set; } = default!;
    public string? GradeRange { get; set; }
    public ConfidenceBand ConfidenceBand { get; set; }
    public string StrengthsJson { get; set; } = "[]";
    public string IssuesJson { get; set; } = "[]";
    public string CriterionScoresJson { get; set; } = "[]";
    public string FeedbackItemsJson { get; set; } = "[]";
    public DateTimeOffset? GeneratedAt { get; set; }
    public string ModelExplanationSafe { get; set; } = default!;
    public string LearnerDisclaimer { get; set; } = default!;
    public string StatusReasonCode { get; set; } = "none";
    public string StatusMessage { get; set; } = "Ready";
    public bool Retryable { get; set; }
    public int? RetryAfterMs { get; set; }
    public DateTimeOffset LastTransitionAt { get; set; }
}

public class ReadinessSnapshot
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public DateTimeOffset ComputedAt { get; set; }
    public string PayloadJson { get; set; } = default!;
    public int Version { get; set; } = 1;
}

public class StudyPlan
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int Version { get; set; } = 1;
    public DateTimeOffset GeneratedAt { get; set; }
    public AsyncState State { get; set; } = AsyncState.Completed;
    public string Checkpoint { get; set; } = default!;
    public string WeakSkillFocus { get; set; } = default!;
    public string? RetakeRescueMode { get; set; }
}

public class StudyPlanItem
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string StudyPlanId { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    public int DurationMinutes { get; set; }

    [MaxLength(1024)]
    public string Rationale { get; set; } = default!;

    public DateOnly DueDate { get; set; }
    public StudyPlanItemStatus Status { get; set; }

    [MaxLength(32)]
    public string Section { get; set; } = default!;

    [MaxLength(64)]
    public string? ContentId { get; set; }

    [MaxLength(32)]
    public string ItemType { get; set; } = default!;
}

public class ReviewRequest
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    public ReviewRequestState State { get; set; }

    [MaxLength(32)]
    public string TurnaroundOption { get; set; } = default!;

    public string FocusAreasJson { get; set; } = "[]";
    public string LearnerNotes { get; set; } = string.Empty;

    [MaxLength(32)]
    public string PaymentSource { get; set; } = default!;

    public decimal PriceSnapshot { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string EligibilitySnapshotJson { get; set; } = "{}";
}

public class Subscription
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string PlanId { get; set; } = default!;

    public SubscriptionStatus Status { get; set; }
    public DateTimeOffset NextRenewalAt { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public decimal PriceAmount { get; set; }
    public string Currency { get; set; } = "AUD";
    public string Interval { get; set; } = "monthly";
}

public class Wallet
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int CreditBalance { get; set; }
    public string LedgerSummaryJson { get; set; } = "[]";
    public DateTimeOffset LastUpdatedAt { get; set; }
}

public class Invoice
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public DateTimeOffset IssuedAt { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "AUD";
    public string Status { get; set; } = "Paid";
    public string Description { get; set; } = default!;
}

public class UploadSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(256)]
    public string UploadUrl { get; set; } = default!;

    [MaxLength(256)]
    public string StorageKey { get; set; } = default!;

    public DateTimeOffset ExpiresAt { get; set; }
    public UploadState State { get; set; }
}

public class BackgroundJobItem
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    public JobType Type { get; set; }
    public AsyncState State { get; set; }
    public string? AttemptId { get; set; }
    public string? ResourceId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset LastTransitionAt { get; set; }
    public string StatusReasonCode { get; set; } = "pending";
    public string StatusMessage { get; set; } = "Queued";
    public bool Retryable { get; set; } = true;
    public int RetryCount { get; set; }
    public int? RetryAfterMs { get; set; }
}

public class DiagnosticSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public AttemptState State { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class DiagnosticSubtestStatus
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string DiagnosticSessionId { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    public AttemptState State { get; set; }
    public int EstimatedDurationMinutes { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? AttemptId { get; set; }
}

public class MockAttempt
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public string ConfigJson { get; set; } = "{}";
    public AttemptState State { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ReportId { get; set; }
}

public class MockReport
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string MockAttemptId { get; set; } = default!;

    public AsyncState State { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset? GeneratedAt { get; set; }
}

public class AnalyticsEventRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string EventName { get; set; } = default!;

    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
}

public class IdempotencyRecord
{
    [Key]
    [MaxLength(128)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Scope { get; set; } = default!;

    [MaxLength(128)]
    public string Key { get; set; } = default!;

    public string ResponseJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}
