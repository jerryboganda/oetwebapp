namespace OetLearner.Api.Contracts;

public sealed record NotificationFeedItem(
    string Id,
    string EventKey,
    string Category,
    string Title,
    string Body,
    string? ActionUrl,
    string Severity,
    bool IsRead,
    IReadOnlyList<string> Channels,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record NotificationPreferencePayload(
    string Timezone,
    bool GlobalInAppEnabled,
    bool GlobalEmailEnabled,
    bool GlobalPushEnabled,
    bool QuietHoursEnabled,
    string? QuietHoursStartLocalTime,
    string? QuietHoursEndLocalTime,
    IReadOnlyDictionary<string, NotificationEventPreferencePayload> EventPreferences,
    IReadOnlyDictionary<string, object?> LegacyLearnerSettings);

public sealed record NotificationEventPreferencePayload(
    bool? InAppEnabled,
    bool? EmailEnabled,
    bool? PushEnabled,
    string? EmailMode);

public sealed record PushSubscriptionPayload(
    string Endpoint,
    string P256dh,
    string Auth,
    DateTimeOffset? ExpiresAt,
    string? UserAgent);

public sealed record NotificationPreferencePatchRequest(
    string? Timezone,
    bool? GlobalInAppEnabled,
    bool? GlobalEmailEnabled,
    bool? GlobalPushEnabled,
    bool? QuietHoursEnabled,
    string? QuietHoursStartLocalTime,
    string? QuietHoursEndLocalTime,
    Dictionary<string, NotificationEventPreferencePayload>? EventPreferences);

public sealed record NotificationFeedResponse(
    IReadOnlyList<NotificationFeedItem> Items,
    int TotalCount,
    int UnreadCount,
    int Page,
    int PageSize);

public sealed record AdminNotificationCatalogEntry(
    string EventKey,
    string AudienceRole,
    string Category,
    string Label,
    string Description,
    string DefaultSeverity,
    bool DefaultInAppEnabled,
    bool DefaultEmailEnabled,
    bool DefaultPushEnabled,
    string DefaultEmailMode);

public sealed record AdminNotificationPolicyRow(
    string AudienceRole,
    string EventKey,
    string Category,
    string Label,
    bool InAppEnabled,
    bool EmailEnabled,
    bool PushEnabled,
    string EmailMode,
    bool IsOverride,
    DateTimeOffset? UpdatedAt,
    string? UpdatedByAdminId,
    string? UpdatedByAdminName);

public sealed record AdminNotificationPoliciesResponse(
    IReadOnlyDictionary<string, bool> GlobalEmailEnabledByAudience,
    IReadOnlyList<AdminNotificationPolicyRow> Rows);

public sealed record AdminNotificationPolicyUpdateRequest(
    bool? InAppEnabled,
    bool? EmailEnabled,
    bool? PushEnabled,
    string? EmailMode);

public sealed record AdminNotificationHealthSnapshot(
    DateTimeOffset GeneratedAt,
    int QueuedEvents,
    int FailedEvents,
    int UnreadInboxItems,
    int FailedDeliveriesLast24Hours,
    int PendingDigestJobs,
    int ActivePushSubscriptions,
    int ExpiredPushSubscriptions,
    IReadOnlyList<AdminNotificationHealthChannelSnapshot> Channels,
    IReadOnlyList<AdminNotificationFailureQueueItem> FailureQueue);

public sealed record AdminNotificationHealthChannelSnapshot(
    string Channel,
    int SentLast24Hours,
    int FailedLast24Hours,
    int SuppressedLast24Hours);

public sealed record AdminNotificationFailureQueueItem(
    string EventId,
    string EventKey,
    string AudienceRole,
    string Channel,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset AttemptedAt);

public sealed record NotificationDeliveryAttemptItem(
    string Id,
    string EventId,
    string EventKey,
    string AudienceRole,
    string Channel,
    string Status,
    string? Provider,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset AttemptedAt,
    DateTimeOffset? CompletedAt);

public sealed record NotificationDeliveryAttemptResponse(
    IReadOnlyList<NotificationDeliveryAttemptItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record NotificationRealtimeEnvelope(
    string Type,
    NotificationFeedItem Notification,
    int UnreadCount);

public sealed record AdminNotificationTestEmailRequest(
    string RecipientEmail,
    string EventKey,
    string AudienceRole);

public sealed record AdminNotificationProofTriggerRequest(
    string EventKey,
    string RecipientEmail,
    Dictionary<string, string?>? Tokens = null,
    string? EntityType = null,
    string? EntityId = null,
    string? VersionOrDateBucket = null,
    bool ProcessImmediately = true,
    bool DispatchDigestImmediately = false);

public sealed record AdminNotificationProofTriggerResponse(
    string NotificationEventId,
    string EventKey,
    string AudienceRole,
    string RecipientEmail,
    string RecipientAuthAccountId,
    string Title,
    string Body,
    string? ActionUrl,
    string Severity,
    string? InboxItemId,
    bool ProcessedImmediately,
    bool DigestDispatchedImmediately);
