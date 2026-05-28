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

public enum MockReviewReservationState
{
    Reserved,
    PartiallyConsumed,
    Consumed,
    Released,
    Expired
}

public enum StudyPlanItemStatus
{
    NotStarted,
    InProgress,
    Completed,
    Skipped,
    Rescheduled,
    Replaced
}

public enum ContentStatus
{
    Draft,
    InReview,
    EditorReview,
    PublisherApproval,
    Published,
    Rejected,
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
    Expired,
    /// <summary>Phase 6 international expansion: voluntary pause (renewal suspended, access frozen or limited per plan rule).</summary>
    Paused,
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
    PrivateSpeakingCalendarSync,         // Sync connected tutor Google Calendar event
    PrivateSpeakingReminder,             // Session reminder notifications
    PrivateSpeakingReservationExpiry,     // Expire unpaid reservations

    // ── Subscription Lifecycle & Engagement ──
    SubscriptionLifecycleCheck,          // Check upcoming renewals, expiries, past-due
    SlaAlertCheck,                       // Check approaching review deadlines
    DripCampaignDispatch,                // Send drip campaign emails

    // ── Expert review auto-assignment (Phase 4) ──
    ExpertReviewAutoAssign,              // Load-balance writing review requests to experts
    ExpertReviewSlaEscalation,           // Release stale assignments past SLA, re-pool for assigner

    // ── Live Classes ──
    LiveClassRecordingDownload,          // Download Zoom cloud recording after session ends
    LiveClassRecordingTranscribe,        // Transcribe downloaded recording audio
    LiveClassRecordingSummarize,         // Summarize transcript into learner-facing notes
    LiveClassRecordingTranslate,         // Wave A2: translate AI summary EN→AR after summarize stage
    LiveClassRecordingEmbed,             // Wave A2: chunk + embed transcript for "Ask AI about this class" RAG
    LiveClassSessionReminderDispatch,    // Send session reminders to enrolled learners (lead-minutes in payload)
    LiveClassWaitlistPromotion,          // Promote waitlisted learner when a slot opens (fallback; handled inline on cancellation)
    LiveClassNoShowPingDispatch,         // Wave A3: fire after meeting.started — push "starting now" to enrolled learners with no attendance yet

    // ── Wave A5 — Billing background jobs ──
    BillingDunningRetry,                 // Smart-retry a Stripe invoice (Stripe.InvoiceService.PayAsync) on 24h/72h/168h cadence
    BillingAbandonedCartEmail,           // Daily sweep at 03:00 UTC; emails carts idle >24h that have not been recovered yet
    BillingRenewalReminder               // 3-day "heads up renewal" email triggered by Stripe invoice.upcoming webhook
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
    Failed,
    /// <summary>
    /// Content Upload subsystem (Slice 1): the file has been streamed into
    /// staging and is waiting for post-processing (scan, thumbnail, extract).
    /// </summary>
    Staged,
    /// <summary>Scanner / virus check in progress.</summary>
    Scanning,
    /// <summary>Scanner rejected the file.</summary>
    Quarantined,
}
