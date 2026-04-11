namespace OetLearner.Api.Domain;

public enum AttemptState
{
    NotStarted,
    InProgress,
    Paused,
    Submitted,
    Evaluating,
    Completed,
    Failed,
    Abandoned
}

public enum AsyncState
{
    Idle,
    Queued,
    Processing,
    Completed,
    Failed
}

public enum ReviewRequestState
{
    Draft,
    Submitted,
    AwaitingPayment,
    Queued,
    InReview,
    Completed,
    Failed,
    Cancelled
}

public enum StudyPlanItemStatus
{
    NotStarted,
    InProgress,
    Completed,
    Skipped,
    Rescheduled
}

public enum ContentStatus
{
    Draft,
    InReview,
    Published,
    Archived
}

public enum SubscriptionStatus
{
    Trial,
    Pending,
    Active,
    PastDue,
    Suspended,
    Cancelled,
    Expired
}

public enum FreezeApprovalMode
{
    AutoApprove,
    AdminApprovalRequired
}

public enum FreezeAccessMode
{
    ReadOnly
}

public enum FreezeEntitlementPauseMode
{
    InternalClock,
    None
}

public enum FreezeStatus
{
    PendingApproval,
    Scheduled,
    Active,
    Completed,
    Rejected,
    Cancelled,
    ForceEnded
}

public enum UploadState
{
    Pending,
    InProgress,
    Uploaded,
    Failed
}

public enum JobType
{
    // ── Existing ──
    WritingEvaluation,
    SpeakingTranscription,
    SpeakingEvaluation,
    StudyPlanRegeneration,
    MockReportGeneration,
    ReviewCompletion,
    NotificationFanout,
    NotificationDigestDispatch,
    FreezeStart,
    FreezeEnd,

    // ── Phase 1 ──
    ContentGeneration,         // AI content generation for admin
    SkillProfileUpdate,        // Update learner skill profile after evaluation
    AchievementCheck,          // Check and award achievements after XP-granting events

    // ── Phase 2 ──
    ConversationEvaluation,    // Evaluate completed AI conversation
    PronunciationAnalysis,     // Analyze pronunciation from speaking audio
    PredictionComputation,     // Compute score predictions after evaluation

    // ── Phase 3 ──
    CertificateGeneration,     // Generate PDF certificates
    ReferralConversion,        // Process referral credit awards
    CohortProgressReport,      // Generate cohort progress reports
    VocabularyDailySet,        // Prepare daily vocabulary set per learner

    // ── Phase 4 ──
    LeaderboardComputation,    // Weekly/monthly leaderboard recalculation

    // ── Private Speaking Sessions ──
    PrivateSpeakingZoomCreate,           // Create Zoom meeting after payment confirmation
    PrivateSpeakingBookingConfirmation,  // Send booking confirmation notifications
    PrivateSpeakingReminder,             // Session reminder notifications
    PrivateSpeakingReservationExpiry     // Expire unpaid reservations
}

public enum ConfidenceBand
{
    Low,
    Medium,
    High
}

public enum RiskLevel
{
    Low,
    Moderate,
    High
}

public enum ExpertReviewStatus
{
    Queued,
    Assigned,
    Claimed,
    InReview,
    DraftSaved,
    Submitted,
    ReworkRequested,
    SecondReviewRequired,
    Completed,
    Cancelled
}

public enum ExpertAssignmentState
{
    Unassigned,
    Assigned,
    Claimed,
    Released,
    Reassigned
}

public enum SlaState
{
    OnTrack,
    AtRisk,
    Overdue,
    CompletedOnTime,
    CompletedLate
}

public enum CalibrationCaseStatus
{
    Pending,
    Completed
}

public enum CalibrationNoteType
{
    Completed,
    Comment,
    System
}

// ── Admin / CMS enums ──

public enum FeatureFlagType
{
    Release,
    Experiment,
    Operational
}

public enum AIConfigStatus
{
    Active,
    Testing,
    Deprecated
}

public enum BillingPlanStatus
{
    Draft,
    Active,
    Inactive,
    Archived,
    Legacy
}

public enum BillingAddOnStatus
{
    Draft,
    Active,
    Inactive,
    Archived
}

public enum BillingCouponStatus
{
    Draft,
    Active,
    Inactive,
    Expired,
    Exhausted,
    Archived
}

public enum BillingDiscountType
{
    Percentage,
    FixedAmount
}

public enum BillingQuoteStatus
{
    Created,
    Applied,
    Completed,
    Expired,
    Cancelled
}

public enum BillingRedemptionStatus
{
    Reserved,
    Applied,
    Voided
}

public enum SubscriptionItemStatus
{
    Active,
    Cancelled,
    Expired
}

public enum MediaAssetStatus
{
    Processing,
    Ready,
    Failed
}
