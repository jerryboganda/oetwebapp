using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

[Index(nameof(AuthAccountId), IsUnique = true)]
public class LearnerUser
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string? AuthAccountId { get; set; }

    [MaxLength(32)]
    public string Role { get; set; } = ApplicationUserRoles.Learner;

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

    [MaxLength(32)]
    public string AccountStatus { get; set; } = "active";

    // ── Admin-set master access expiry ──
    // When set and elapsed, login (and token refresh) is refused with
    // ApiException.Forbidden("subscription_expired", …) — see
    // AuthService.EnsureAccountCanAuthenticateAsync. Null == never expires.
    // Written by the admin allocation flow (UserAccessAllocationService),
    // defaulting to the latest allocated package expiry but admin-overridable.
    public DateTimeOffset? AccessExpiresAt { get; set; }

    // ── Engagement tracking ──
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateTimeOffset? LastPracticeDate { get; set; }
    public int TotalPracticeMinutes { get; set; }
    public int TotalPracticeSessions { get; set; }
    public string? WeeklyActivityJson { get; set; }

    // ── Multi-exam ──
    // NOTE: canonical exam-type code is "OET" (upper). All read paths normalize via
    // ExamCodes.Normalize / NormalizeOrNull. Keep default uppercase to match canonical.
    [MaxLength(16)]
    public string ActiveExamTypeCode { get; set; } = "OET";

    public ApplicationUserAccount? AuthAccount { get; set; }
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

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = "oet";

    /// <summary>Target OET delivery mode: paper | computer | home | unsure. Null until the learner sets it.</summary>
    [MaxLength(16)]
    public string? TargetExamMode { get; set; }

    /// <summary>Self-reported confidence: beginner | intermediate | advanced | unsure. Null until set.</summary>
    [MaxLength(16)]
    public string? ConfidenceLevel { get; set; }
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

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public int SortOrder { get; set; }

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = "oet";
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

    // ── Multi-exam discriminator ──
    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = "oet";

    public int DifficultyRating { get; set; } = 1500;    // Elo-style difficulty for adaptive engine

    // ── Content source and QA ──
    [MaxLength(32)]
    public string SourceType { get; set; } = "manual";

    [MaxLength(32)]
    public string QaStatus { get; set; } = "approved";

    [MaxLength(64)]
    public string? QaReviewedBy { get; set; }

    public DateTimeOffset? QaReviewedAt { get; set; }

    public string? PerformanceMetricsJson { get; set; }

    // ── Content migration / hierarchy fields ──

    [MaxLength(8)]
    public string InstructionLanguage { get; set; } = "en";   // "en", "ar", "ar+en"

    [MaxLength(8)]
    public string ContentLanguage { get; set; } = "en";       // target language of the content

    /// <summary>JSON array of profession codes for multi-profession content.</summary>
    public string ProfessionIdsJson { get; set; } = "[]";

    /// <summary>JSON array of package codes this item belongs to.</summary>
    public string PackageEligibilityJson { get; set; } = "[]";

    [MaxLength(64)]
    public string? CohortRelevance { get; set; }   // batch/cohort code if cohort-specific

    [MaxLength(32)]
    public string SourceProvenance { get; set; } = "original";  // original, official_sample, recall, benchmark, contributed

    [MaxLength(32)]
    public string RightsStatus { get; set; } = "owned";  // owned, licensed, recall_unverified, official_attribution_required

    [MaxLength(32)]
    public string FreshnessConfidence { get; set; } = "current";  // current, likely_current, aging, superseded, archived

    [MaxLength(64)]
    public string? SupersededById { get; set; }

    [MaxLength(64)]
    public string? DuplicateGroupId { get; set; }

    /// <summary>JSON array of {assetId, type, url, format, size, duration, thumbnailUrl}.</summary>
    public string MediaManifestJson { get; set; } = "[]";

    [MaxLength(512)]
    public string? CanonicalSourcePath { get; set; }   // original Drive path for traceability

    [MaxLength(64)]
    public string? ImportBatchId { get; set; }

    public bool IsPreviewEligible { get; set; }

    public bool IsDiagnosticEligible { get; set; }

    public bool IsMockEligible { get; set; }

    public int QualityScore { get; set; }   // 0 = unrated, 1-5 admin-assigned quality
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

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = "oet";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(64)]
    public string? ModelVersionId { get; set; }
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

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = "oet";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(64)]
    public string? ModelVersionId { get; set; }
}

public class ReadinessSnapshot
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public DateTimeOffset ComputedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string PayloadJson { get; set; } = default!;
    public int Version { get; set; } = 1;

    public decimal OverallReadiness { get; set; }
    public decimal WritingReadiness { get; set; }
    public decimal SpeakingReadiness { get; set; }
    public decimal ReadingReadiness { get; set; }
    public decimal ListeningReadiness { get; set; }
    public decimal VocabularyReadiness { get; set; }

    [MaxLength(16)]
    public string OverallRisk { get; set; } = "Unknown";

    public decimal? TargetDateProbability { get; set; }

    [MaxLength(32)]
    public string? WeakestSubtest { get; set; }

    public int RecommendedStudyHoursPerWeek { get; set; }

    [MaxLength(16)]
    public string ConfidenceLevel { get; set; } = "Low";

    public int DataPointCount { get; set; }
}

public class ReadinessHistory
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public DateOnly WeekStartDate { get; set; }
    public DateTimeOffset RecordedAt { get; set; }

    public decimal Overall { get; set; }
    public decimal Writing { get; set; }
    public decimal Speaking { get; set; }
    public decimal Reading { get; set; }
    public decimal Listening { get; set; }
    public decimal Vocabulary { get; set; }

    [MaxLength(16)]
    public string Risk { get; set; } = "Unknown";

    public decimal? TargetDateProbability { get; set; }
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

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = "oet";

    // ── Planner engine fields (additive May 2026) ──
    [MaxLength(64)]
    public string? DiagnosticAttemptId { get; set; }

    public int WeekNumber { get; set; } = 1;
    public int TotalWeeks { get; set; } = 1;
    public DateOnly? PlanWindowStart { get; set; }
    public DateOnly? PlanWindowEnd { get; set; }

    [MaxLength(64)]
    public string? TemplateId { get; set; }

    public int MinutesPerDayBudget { get; set; }

    [MaxLength(128)]
    public string? GenerationInputsHash { get; set; }

    public string SubtestWeightsJson { get; set; } = "{}";
    public bool IsPremiumPersonalized { get; set; }

    [MaxLength(32)]
    public string EntitlementTierAtGeneration { get; set; } = "free";

    public bool IsActive { get; set; } = true;
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

    // ── Planner engine fields (additive May 2026) ──
    [MaxLength(64)]
    public string? SourceContentId { get; set; }

    [MaxLength(512)]
    public string? ContentRoute { get; set; }

    [MaxLength(64)]
    public string? LinkedReviewItemId { get; set; }

    public int PriorityScore { get; set; }
    public int WeekIndex { get; set; }

    [MaxLength(64)]
    public string? ReplacedById { get; set; }

    public int? FeedbackRating { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? ActualMinutesSpent { get; set; }

    [MaxLength(32)]
    public string? SlotKind { get; set; }

    public string? TagsJson { get; set; }
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

    /// <summary>Compensation owed to the expert reviewer for this request (AUD).</summary>
    public decimal ReviewerCompensation { get; set; }

    /// <summary>Whether the reviewer compensation has been marked as paid.</summary>
    public bool CompensationPaid { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string EligibilitySnapshotJson { get; set; } = "{}";
}

public class AccountFreezePolicy
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = "global";

    public bool IsEnabled { get; set; } = true;
    public bool SelfServiceEnabled { get; set; } = true;
    public FreezeApprovalMode ApprovalMode { get; set; } = FreezeApprovalMode.AutoApprove;
    public int MinDurationDays { get; set; } = 1;
    public int MaxDurationDays { get; set; } = 365;
    public bool AllowScheduling { get; set; } = true;
    public FreezeAccessMode AccessMode { get; set; } = FreezeAccessMode.ReadOnly;
    public FreezeEntitlementPauseMode EntitlementPauseMode { get; set; } = FreezeEntitlementPauseMode.InternalClock;
    public bool RequireReason { get; set; } = true;
    public bool RequireInternalNotes { get; set; }
    public bool AllowActivePaid { get; set; } = true;
    public bool AllowGracePeriod { get; set; } = true;
    public bool AllowTrial { get; set; }
    public bool AllowComplimentary { get; set; }
    public bool AllowCancelled { get; set; }
    public bool AllowExpired { get; set; }
    public bool AllowReviewOnly { get; set; }
    public bool AllowPastDue { get; set; }
    public bool AllowSuspended { get; set; }
    public string PolicyNotes { get; set; } = string.Empty;
    public string EligibilityReasonCodesJson { get; set; } = "[]";
    public string? UpdatedByAdminId { get; set; }
    public string? UpdatedByAdminName { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public class AccountFreezeRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string? RequestedByLearnerId { get; set; }

    [MaxLength(64)]
    public string? RequestedByAdminId { get; set; }

    [MaxLength(128)]
    public string? RequestedByAdminName { get; set; }

    [MaxLength(64)]
    public string? ApprovedByAdminId { get; set; }

    [MaxLength(128)]
    public string? ApprovedByAdminName { get; set; }

    [MaxLength(64)]
    public string? RejectedByAdminId { get; set; }

    [MaxLength(128)]
    public string? RejectedByAdminName { get; set; }

    [MaxLength(64)]
    public string? EndedByAdminId { get; set; }

    [MaxLength(128)]
    public string? EndedByAdminName { get; set; }

    public FreezeStatus Status { get; set; }
    public bool IsCurrent { get; set; } = true;
    public bool IsSelfService { get; set; }
    public bool EntitlementConsumed { get; set; }
    public bool EntitlementReset { get; set; }
    public bool IsOverride { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ScheduledStartAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int DurationDays { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? InternalNotes { get; set; }
    public string PolicySnapshotJson { get; set; } = "{}";
    public int PolicyVersionSnapshot { get; set; }
    public string EligibilitySnapshotJson { get; set; } = "{}";
    public string? RejectionReason { get; set; }
    public string? EndReason { get; set; }
    public string? CancellationReason { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class AccountFreezeEntitlement
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string? FreezeRecordId { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? ResetAt { get; set; }

    [MaxLength(64)]
    public string? ResetByAdminId { get; set; }

    [MaxLength(128)]
    public string? ResetByAdminName { get; set; }

    [MaxLength(256)]
    public string? ResetReason { get; set; }
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

    [MaxLength(64)]
    public string? PlanVersionId { get; set; }

    public SubscriptionStatus Status { get; set; }
    public DateTimeOffset NextRenewalAt { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public decimal PriceAmount { get; set; }
    public string Currency { get; set; } = "AUD";
    public string Interval { get; set; } = "monthly";

    /// <summary>Phase 6: when the voluntary pause ends and renewal resumes. Null when not paused.</summary>
    public DateTimeOffset? PausedUntil { get; set; }

    /// <summary>Phase 5: while in dunning grace, access continues until this timestamp.</summary>
    public DateTimeOffset? GracePeriodUntil { get; set; }

    // ── OET 2026 catalog entitlement counters (Wave 1 — 27-SKU portfolio) ───────────
    public DateTimeOffset? ExpiresAt { get; set; }
    public int WritingAssessmentsRemaining { get; set; }
    public int SpeakingSessionsRemaining { get; set; }
    public int AiCreditsRemaining { get; set; }
    public bool TutorBookUnlocked { get; set; }
    public bool BasicEnglishUnlocked { get; set; }

    // PDF subscription timer/freezing contract. All math is server-side UTC;
    // the frontend only displays these projected values.
    public int AccessDurationDays { get; set; } = 180;
    public int TotalFreezeDaysUsed { get; set; }
    public int MaxFreezeDaysAllowed { get; set; } = 365;
    public int? PreservedRemainingDays { get; set; }
    public DateTimeOffset? PendingFreezeRequestDate { get; set; }
    public DateTimeOffset? FrozenSince { get; set; }
}

public class SubscriptionFreeze
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SubscriptionId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(16)]
    public string RequestedBy { get; set; } = "candidate";

    [MaxLength(16)]
    public string RequestStatus { get; set; } = "pending";

    public DateTimeOffset FreezeRequestDate { get; set; }
    public DateTimeOffset? FreezeStartDate { get; set; }
    public DateTimeOffset? FreezeEndDate { get; set; }
    public int? PreservedRemainingDaysAtFreeze { get; set; }
    public int? FreezeDaysUsed { get; set; }

    [MaxLength(16)]
    public string? FrozenBy { get; set; }

    [MaxLength(512)]
    public string? FreezeReason { get; set; }

    [MaxLength(1024)]
    public string? AdminNotes { get; set; }

    [MaxLength(1024)]
    public string? RejectionReason { get; set; }

    [MaxLength(64)]
    public string? AdminDecisionById { get; set; }

    public DateTimeOffset? AdminDecisionDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
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

    /// <summary>
    /// Cross-DB optimistic-concurrency row version. PostgreSQL maps this to
    /// the <c>xmin</c> system column at runtime via the existing
    /// <c>ConfigureXminToken</c> path; SQLite/in-memory test providers use
    /// EF's shadow-managed <c>byte[]</c> rowversion. Nullable so the
    /// 20260512100000_AddWalletRowVersion migration is safe against existing
    /// rows. Slice A — May 2026 billing hardening.
    /// </summary>
    [ConcurrencyCheck]
    public byte[]? RowVersion { get; set; }
}

public class Invoice
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int? Number { get; set; }

    public DateTimeOffset IssuedAt { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "AUD";
    public string Status { get; set; } = "Paid";
    public string Description { get; set; } = default!;

    [MaxLength(64)]
    public string? PlanVersionId { get; set; }

    [MaxLength(1024)]
    public string AddOnVersionIdsJson { get; set; } = "{}";

    [MaxLength(64)]
    public string? CouponVersionId { get; set; }

    [MaxLength(64)]
    public string? QuoteId { get; set; }

    [MaxLength(256)]
    public string? CheckoutSessionId { get; set; }
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
    public DateTimeOffset? ExpiresAt { get; set; }

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = "oet";
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

    [MaxLength(64)]
    public string? MockBundleId { get; set; }

    [MaxLength(16)]
    public string MockType { get; set; } = "full";

    [MaxLength(32)]
    public string? SubtestCode { get; set; }

    [MaxLength(32)]
    public string Mode { get; set; } = "exam";

    [MaxLength(32)]
    public string Profession { get; set; } = "medicine";

    [MaxLength(32)]
    public string ReviewSelection { get; set; } = "none";

    public bool StrictTimer { get; set; } = true;
    public int ReservedReviewCredits { get; set; }

    /// <summary>
    /// Spec §2 delivery model. Stored lower-case; values from <see cref="MockDeliveryModes"/>.
    /// Defaults to <c>computer</c> for backwards compatibility with rows pre-Wave-1.
    /// </summary>
    [MaxLength(16)]
    public string DeliveryMode { get; set; } = MockDeliveryModes.Computer;

    /// <summary>
    /// Spec §3 strictness preset (learning / exam / final_readiness). Drives grader behaviour
    /// at the sub-test layer (one-play, no hints, timer locking).
    /// </summary>
    [MaxLength(32)]
    public string Strictness { get; set; } = MockStrictness.Exam;

    /// <summary>
    /// Optional 32-bit randomisation seed (Wave 8) used by graders that support shuffling.
    /// Null means no shuffling has been applied (legacy rows + non-randomised types).
    /// </summary>
    public long? RandomisationSeed { get; set; }

    public string ConfigJson { get; set; } = "{}";
    public AttemptState State { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ReportId { get; set; }

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = "oet";

    public MockBundle? MockBundle { get; set; }
    public ICollection<MockSectionAttempt> SectionAttempts { get; set; } = new List<MockSectionAttempt>();
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

    // Schema version of PayloadJson. Empty string indicates pre-V1 reports written before
    // the schema was formalised. The aggregator writes "v1" for all new reports. Consumers
    // should branch on this rather than guessing at the payload shape.
    [MaxLength(16)]
    public string PayloadSchemaVersion { get; set; } = string.Empty;
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

// ── Multi-Exam Reference ──

public class ExamType
{
    [Key]
    [MaxLength(16)]
    public string Code { get; set; } = default!;           // "oet", "ielts", "pte", "cambridge", "toefl"

    [MaxLength(128)]
    public string Label { get; set; } = default!;          // "OET - Occupational English Test"

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    public string SubtestDefinitionsJson { get; set; } = "[]";  // Subtests for this exam
    public string ScoringSystemJson { get; set; } = "{}";       // Scoring ranges, bands, grades
    public string TimingsJson { get; set; } = "{}";             // Per-subtest time limits
    public string ProfessionIdsJson { get; set; } = "[]";       // Applicable professions (empty = all)

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public int SortOrder { get; set; }
}

public class TaskType
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;             // "oet-writing-referral", "ielts-writing-task2"

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Label { get; set; } = default!;          // "Referral Letter", "Task 2 Essay"

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    public string ConfigJson { get; set; } = "{}";         // Word limits, time limits, format rules
    public string CriteriaIdsJson { get; set; } = "[]";    // Evaluation criteria for this task type

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public int SortOrder { get; set; }
}

public class ExamFamily
{
    [Key]
    [MaxLength(16)]
    public string Code { get; set; } = "oet";

    [MaxLength(64)]
    public string Label { get; set; } = "OET";

    [MaxLength(32)]
    public string ScoringModel { get; set; } = "0-500-letter";

    [MaxLength(256)]
    public string? Description { get; set; }

    public string SubtestConfigJson { get; set; } = "[]";
    public string CriteriaConfigJson { get; set; } = "[]";

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
