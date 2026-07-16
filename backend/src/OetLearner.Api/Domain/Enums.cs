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
    /// <summary>Candidate has requested a subscription freeze; access remains active until admin approval.</summary>
    FreezeRequested,
    /// <summary>Course access is frozen and blocked while remaining days are preserved.</summary>
    Frozen,
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
    BillingRenewalReminder,              // 3-day "heads up renewal" email triggered by Stripe invoice.upcoming webhook

    // NOTE: keep this value LAST. JobType is persisted as an int ordinal, so any new value
    // MUST be appended at the very end to avoid renumbering existing BackgroundJobs rows.
    PrivateSpeakingNoShowSweep           // Detect/mark no-shows from Zoom attendance data (logically Private Speaking)
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

// ── Access & payment string taxonomies ──
// Stored as plain lower-case strings (not int-backed enums) so the columns stay
// readable in SQL and additive values never renumber existing rows.

/// <summary>
/// How a purchased <see cref="BillingPlan"/> reaches the buyer. Stored in
/// <see cref="BillingPlan.DeliveryMethod"/> / <see cref="BillingPlanVersion.DeliveryMethod"/>.
/// Anything other than <see cref="AutomaticWeb"/> parks the Subscription at
/// <c>Pending</c> + <see cref="FulfilmentStatuses.PendingManual"/> until an admin
/// marks it fulfilled.
/// </summary>
public static class DeliveryMethods
{
    /// <summary>Default — access unlocks in the web app the moment payment completes.</summary>
    public const string AutomaticWeb = "automatic_web";

    /// <summary>Web access, but an admin must release it by hand.</summary>
    public const string ManualWeb = "manual_web";

    /// <summary>Delivered as a Telegram channel invite (<see cref="BillingPlan.TelegramInviteUrl"/>).</summary>
    public const string Telegram = "telegram";

    /// <summary>Physical/off-platform material handed over outside the app.</summary>
    public const string ManualMaterial = "manual_material";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        AutomaticWeb, ManualWeb, Telegram, ManualMaterial,
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    /// <summary>True when the plan needs an admin "Mark Fulfilled" action before access is released.</summary>
    public static bool RequiresManualFulfilment(string? value)
        => IsValid(value) && !string.Equals(value, AutomaticWeb, StringComparison.OrdinalIgnoreCase);

    public static string Label(string value) => value switch
    {
        AutomaticWeb => "Automatic (web app)",
        ManualWeb => "Manual (web app)",
        Telegram => "Telegram channel",
        ManualMaterial => "Manual material",
        _ => value,
    };
}

/// <summary>
/// Manual-fulfilment state of a <see cref="Subscription"/>, orthogonal to
/// <see cref="SubscriptionStatus"/>. Only <see cref="Fulfilled"/> flips a
/// manual-delivery subscription from Pending to Active.
/// </summary>
public static class FulfilmentStatuses
{
    /// <summary>Default — automatic delivery, nothing for an admin to do.</summary>
    public const string Auto = "auto";

    /// <summary>Paid, awaiting the admin hand-over step.</summary>
    public const string PendingManual = "pending_manual";

    /// <summary>Admin has handed the package over; access released.</summary>
    public const string Fulfilled = "fulfilled";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Auto, PendingManual, Fulfilled,
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

/// <summary>
/// Discriminator on <see cref="ManualPaymentRequest"/> — every order carries exactly
/// one proof row, either a file the learner uploaded or a receipt the system minted
/// from a completed card-gateway transaction.
/// </summary>
public static class PaymentProofKinds
{
    /// <summary>Learner-uploaded proof file (bank transfer / Vodafone Cash / InstaPay / …).</summary>
    public const string LearnerUpload = "learner_upload";

    /// <summary>System-generated receipt for a card gateway; has no <c>ProofUrl</c>.</summary>
    public const string GatewayReceipt = "gateway_receipt";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        LearnerUpload, GatewayReceipt,
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
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
