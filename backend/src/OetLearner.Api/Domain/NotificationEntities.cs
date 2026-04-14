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
    AdminPrivateSpeakingBooked
}

public enum NotificationChannel
{
    InApp,
    Email,
    Push
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
    Pending,
    Sent,
    Suppressed,
    Failed,
    Expired
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

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }

    [MaxLength(128)]
    public string? UpdatedByAdminName { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

[Index(nameof(NotificationEventId), nameof(Channel), nameof(AttemptedAt))]
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
    public DateTimeOffset UpdatedAt { get; set; }IND