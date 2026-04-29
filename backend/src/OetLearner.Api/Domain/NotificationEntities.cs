using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

public enum NotificationEventKey
{
    LearnerEvaluationCompleted,
    LearnerEvaluationFailed,
    LearnerMockReportReady,
    LearnerReviewRequested,
    LearnerReviewCompleted,
    LearnerReviewReworkRequested,
    LearnerStudyPlanRegenerated,
    LearnerStudyPlanDueReminder,
    LearnerReadinessUpdated,
    LearnerInvoiceFailed,
    LearnerSubscriptionChanged,
    LearnerAccountStatusChanged,
    LearnerFreezeRequested,
    LearnerFreezeApproved,
    LearnerFreezeRejected,
    LearnerFreezeStarted,
    LearnerFreezeEnded,
    ExpertReviewAssigned,
    ExpertReviewClaimed,
    ExpertReviewReleased,
    ExpertReviewReassigned,
    ExpertReviewOverdue,
    ExpertReviewReworkRequested,
    ExpertCalibrationAvailable,
    ExpertCalibrationResult,
    ExpertScheduleChanged,
    AdminReviewOpsAction,
    AdminUserLifecycleAction,
    AdminBillingFailureAlert,
    AdminFeatureFlagChanged,
    AdminAiConfigChanged,
    AdminStuckJobAlert,
    AdminNotificationDeliveryFailureAlert,
    AdminFreezePolicyChanged,
    AdminFreezeLifecycleAction,

    // Private Speaking Session notifications
    LearnerPrivateSpeakingBooked,
    LearnerPrivateSpeakingReminder,
    LearnerPrivateSpeakingCancelled,
    ExpertPrivateSpeakingAssigned,
    ExpertPrivateSpeakingReminder,
    ExpertPrivateSpeakingCancelled,
    AdminPrivateSpeakingBooked,

    LearnerAccountCreated,
    LearnerEmailVerificationRequested,
    LearnerPasswordResetRequested,
    LearnerClassBookingConfirmed,
    LearnerMockSubmitted,
    LearnerInvoiceGenerated,
    LearnerDailyStudyReminder,
    LearnerMissedLesson,
    LearnerWeakSkillReminder,
    LearnerHomeworkDue,
    LearnerLessonUnlocked,
    LearnerStreakReminder,
    LearnerMockScheduled,
    LearnerMockReminder24h,
    LearnerMockReminder2h,
    LearnerMockReminder30m,
    LearnerMockTechnicalCheck,
    LearnerMockStarted,
    LearnerMockIncomplete,
    LearnerMockAutoScoreReady,
    LearnerMockTeacherFeedbackReady,
    LearnerMockRetakeRecommended,
    LearnerSpeakingBooked,
    LearnerSpeakingReminder24h,
    LearnerSpeakingReminder30m,
    LearnerSpeakingTutorJoined,
    LearnerSpeakingStudentLate,
    LearnerSpeakingRecordingReady,
    LearnerSpeakingFeedbackReady,
    LearnerSpeakingRescheduleNeeded,
    ExpertSpeakingSessionAssigned,
    ExpertSpeakingStudentNoShow,
    ExpertSpeakingFeedbackOverdue,
    AdminSpeakingNoShowAlert,
    AdminSpeakingOverdueFeedbackAlert,
    LearnerWritingAssigned,
    LearnerWritingDeadlineReminder,
    LearnerWritingSubmitted,
    LearnerWritingTeacherAssigned,
    LearnerWritingFeedbackReady,
    LearnerWritingRewriteRequested,
    LearnerWritingRewriteOverdue,
    ExpertWritingAssigned,
    ExpertWritingFeedbackOverdue,
    AdminWritingOverdueEscalation,
    LearnerTrialStarted,
    LearnerTrialEnding,
    LearnerTrialExpired,
    LearnerPaymentSucceeded,
    LearnerPaymentFailed,
    LearnerRenewalComing,
    LearnerSubscriptionCancelled,
    LearnerCouponApplied,
    LearnerCreditsLow,
    LearnerCreditsExpiring,
    LearnerRefundProcessed,
    LearnerTrialConversionNudge,
    LearnerAbandonedCheckout,
    LearnerInactiveNudge,
    LearnerExamUrgencyReminder,
    LearnerWinBack,
    LearnerCourseLaunch,
    AdminSuspiciousActivityAlert,
    AdminProviderOutageAlert,
    AdminOverdueFeedbackSpikeAlert,
    AdminFailedPaymentSpikeAlert,
    AdminCouponAbuseAlert,
    AdminContentFlaggedAlert
}

public enum NotificationChannel
{
    InApp,
    Email,
    Push,
    Sms,
    WhatsApp
}

public enum NotificationSeverity
{
    Info,
    Success,
    Warning,
    Critical
}

public enum NotificationEmailMode
{
    Off,
    Immediate,
    DailyDigest
}

public enum NotificationDeliveryStatus
{
    Pending = 0,
    Sent = 1,
    Suppressed = 2,
    Failed = 3,
    Expired = 4,
    Created = 5,
    Queued = 6,
    Delivered = 7,
    Opened = 8,
    Clicked = 9,
    Bounced = 10,
    Unsubscribed = 11
}

[Index(nameof(DedupeKey), IsUnique = true)]
[Index(nameof(RecipientAuthAccountId), nameof(CreatedAt))]
[Index(nameof(State), nameof(CreatedAt))]
public class NotificationEvent
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string RecipientAuthAccountId { get; set; } = default!;

    [MaxLength(32)]
    public string RecipientRole { get; set; } = default!;

    [MaxLength(128)]
    public string EventKey { get; set; } = default!;

    [MaxLength(64)]
    public string Category { get; set; } = default!;

    [MaxLength(256)]
    public string Title { get; set; } = default!;

    [MaxLength(4096)]
    public string Body { get; set; } = default!;

    [MaxLength(512)]
    public string? ActionUrl { get; set; }

    public NotificationSeverity Severity { get; set; }
    public AsyncState State { get; set; } = AsyncState.Queued;

    [MaxLength(128)]
    public string EntityType { get; set; } = default!;

    [MaxLength(128)]
    public string EntityId { get; set; } = default!;

    [MaxLength(128)]
    public string VersionOrDateBucket { get; set; } = default!;

    [MaxLength(256)]
    public string DedupeKey { get; set; } = default!;

    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int FanoutAttempts { get; set; }
}

[Index(nameof(AuthAccountId), nameof(IsRead), nameof(CreatedAt))]
[Index(nameof(NotificationEventId), IsUnique = true)]
public class NotificationInboxItem
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string NotificationEventId { get; set; } = default!;

    [MaxLength(64)]
    public string AuthAccountId { get; set; } = default!;

    [MaxLength(128)]
    public string EventKey { get; set; } = default!;

    [MaxLength(64)]
    public string Category { get; set; } = default!;

    [MaxLength(256)]
    public string Title { get; set; } = default!;

    [MaxLength(4096)]
    public string Body { get; set; } = default!;

    [MaxLength(512)]
    public string? ActionUrl { get; set; }

    public NotificationSeverity Severity { get; set; }
    public bool IsRead { get; set; }
    public string ChannelsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

[Index(nameof(AuthAccountId), IsUnique = true)]
public class NotificationPreference
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string AuthAccountId { get; set; } = default!;

    [MaxLength(64)]
    public string Timezone { get; set; } = "UTC";

    public bool GlobalInAppEnabled { get; set; } = true;
    public bool GlobalEmailEnabled { get; set; } = true;
    public bool GlobalPushEnabled { get; set; } = true;
    public bool QuietHoursEnabled { get; set; }
    public int? QuietHoursStartMinutes { get; set; }
    public int? QuietHoursEndMinutes { get; set; }
    public string EventOverridesJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[Index(nameof(AudienceRole), nameof(EventKey), IsUnique = true)]
public class NotificationPolicyOverride
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(32)]
    public string AudienceRole { get; set; } = default!;

    [MaxLength(128)]
    public string EventKey { get; set; } = default!;

    public bool? InAppEnabled { get; set; }
    public bool? EmailEnabled { get; set; }
    public bool? PushEnabled { get; set; }
    public NotificationEmailMode? EmailMode { get; set; }
    public int? MaxDeliveriesPerHour { get; set; }
    public int? MaxDeliveriesPerDay { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }

    [MaxLength(128)]
    public string? UpdatedByAdminName { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

[Index(nameof(NotificationEventId), nameof(Channel), nameof(AttemptedAt))]
[Index(nameof(AuthAccountId), nameof(Channel), nameof(Status), nameof(AttemptedAt))]
[Index(nameof(Status), nameof(AttemptedAt))]
public class NotificationDeliveryAttempt
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string NotificationEventId { get; set; } = default!;

    [MaxLength(64)]
    public string AuthAccountId { get; set; } = default!;

    public NotificationChannel Channel { get; set; }
    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;

    [MaxLength(64)]
    public string? SubscriptionId { get; set; }

    [MaxLength(64)]
    public string? Provider { get; set; }

    [MaxLength(256)]
    public string? MessageId { get; set; }

    [MaxLength(128)]
    public string? ErrorCode { get; set; }

    [MaxLength(1024)]
    public string? ErrorMessage { get; set; }

    public string ResponsePayloadJson { get; set; } = "{}";
    public DateTimeOffset AttemptedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

[Index(nameof(AuthAccountId), nameof(IsActive))]
[Index(nameof(Endpoint), IsUnique = true)]
public class PushSubscription
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string AuthAccountId { get; set; } = default!;

    [MaxLength(2048)]
    public string Endpoint { get; set; } = default!;

    [MaxLength(1024)]
    public string P256dh { get; set; } = default!;

    [MaxLength(1024)]
    public string Auth { get; set; } = default!;

    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    [MaxLength(256)]
    public string? UserAgent { get; set; }

    [MaxLength(128)]
    public string? FailureReasonCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastSuccessfulAt { get; set; }
    public DateTimeOffset? LastFailureAt { get; set; }
}

[Index(nameof(AuthAccountId), nameof(Platform))]
[Index(nameof(Token), IsUnique = true)]
public class MobilePushToken
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string AuthAccountId { get; set; } = default!;

    [MaxLength(512)]
    public string Token { get; set; } = default!;

    [MaxLength(16)]
    public string Platform { get; set; } = default!;

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[Index(nameof(AuthAccountId), nameof(Channel), nameof(Category), IsUnique = true)]
[Index(nameof(Channel), nameof(IsGranted), nameof(UpdatedAt))]
public class NotificationConsent
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string AuthAccountId { get; set; } = default!;

    public NotificationChannel Channel { get; set; }

    [MaxLength(64)]
    public string Category { get; set; } = "global";

    public bool IsGranted { get; set; }

    [MaxLength(64)]
    public string Source { get; set; } = "user";

    [MaxLength(512)]
    public string? Reason { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }

    [MaxLength(128)]
    public string? UpdatedByAdminName { get; set; }

    public DateTimeOffset? GrantedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[Index(nameof(AuthAccountId), nameof(Channel), nameof(EventKey), nameof(IsActive))]
[Index(nameof(Channel), nameof(IsActive), nameof(ExpiresAt))]
public class NotificationSuppression
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string AuthAccountId { get; set; } = default!;

    public NotificationChannel Channel { get; set; }

    [MaxLength(128)]
    public string? EventKey { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(128)]
    public string ReasonCode { get; set; } = "manual_suppression";

    [MaxLength(1024)]
    public string? Reason { get; set; }

    [MaxLength(64)]
    public string CreatedByAdminId { get; set; } = default!;

    [MaxLength(128)]
    public string CreatedByAdminName { get; set; } = default!;

    [MaxLength(64)]
    public string? ReleasedByAdminId { get; set; }

    [MaxLength(128)]
    public string? ReleasedByAdminName { get; set; }

    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
}

public class NotificationTemplate
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string EventKey { get; set; } = default!;

    [MaxLength(32)]
    public string Channel { get; set; } = default!;

    [MaxLength(64)]
    public string? Category { get; set; }

    [MaxLength(16)]
    public string Locale { get; set; } = "en";

    public int Version { get; set; } = 1;

    [MaxLength(512)]
    public string? Description { get; set; }

    [MaxLength(256)]
    public string SubjectTemplate { get; set; } = default!;

    public string BodyTemplate { get; set; } = default!;

    public string? TextTemplate { get; set; }
    public string? HtmlTemplate { get; set; }
    public string MetadataJson { get; set; } = "{}";

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
