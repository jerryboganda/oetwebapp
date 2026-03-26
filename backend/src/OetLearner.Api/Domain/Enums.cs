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
    Published,
    Archived
}

public enum SubscriptionStatus
{
    Trial,
    Active,
    PastDue,
    Cancelled
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
    WritingEvaluation,
    SpeakingTranscription,
    SpeakingEvaluation,
    StudyPlanRegeneration,
    MockReportGeneration,
    ReviewCompletion
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
    Active,
    Legacy
}
