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

public sealed record RegisterPushTokenRequest(
    string Token,
    string Platform);

public sealed record NotificationConsentItem(
    string AuthAccountId,
    string Channel,
    string Category,
    bool IsGranted,
    bool RequiresExplicitConsent,
    string Source,
    string? Reason,
    DateTimeOffset? GrantedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset UpdatedAt);

public sealed record NotificationConsentUpdateRequest(
    bool IsGranted,
    string? Source,
    string? Reason,
    string? Category = null);

public sealed record AdminNotificationConsentResponse(
    IReadOnlyList<NotificationConsentItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record NotificationSuppressionItem(
    Guid Id,
    string AuthAccountId,
    string Channel,
    string? EventKey,
    bool IsActive,
    string ReasonCode,
    string? Reason,
    DateTimeOffset? StartsAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ReleasedAt,
    string CreatedByAdminName,
    string? ReleasedByAdminName);

public sealed record AdminNotificationSuppressionCreateRequest(
    string AuthAccountId,
    string Channel,
    string? EventKey,
    string ReasonCode,
    string? Reason,
    DateTimeOffset? StartsAt,
    DateTimeOffset? ExpiresAt);

public sealed record AdminNotificationSuppressionResponse(
    IReadOnlyList<NotificationSuppressionItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

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
    bool DefaultSmsEnabled,
    bool DefaultWhatsAppEnabled,
    string DefaultEmailMode,
    bool IsPolicyProtected);

public sealed record AdminNotificationPolicyRow(
    string AudienceRole,
    string EventKey,
    string Category,
    string Label,
    bool InAppEnabled,
    bool EmailEnabled,
    bool PushEnabled,
    string EmailMode,
    int? MaxDeliveriesPerHour,
    int? MaxDeliveriesPerDay,
    bool IsPolicyProtected,
    bool IsOverride,
    DateTimeOffset? UpdatedAt,
    string? UpdatedByAdminId,
    string? UpdatedByAdminName);

public sealed record AdminNotificationPoliciesResponse(
    IReadOnlyDictionary<string, bool> GlobalEmailEnabledByAudience,
    IReadOnlyDictionary<string, AdminNotificationAudienceChannelPolicy> GlobalChannelEnabledByAudience,
    IReadOnlyList<AdminNotificationPolicyRow> Rows);

public sealed record AdminNotificationAudienceChannelPolicy(
    bool InAppEnabled,
    bool EmailEnabled,
    bool PushEnabled);

public sealed record AdminNotificationPolicyUpdateRequest(
    bool? InAppEnabled,
    bool? EmailEnabled,
    bool? PushEnabled,
    string? EmailMode,
    int? MaxDeliveriesPerHour = null,
    int? MaxDeliveriesPerDay = null,
    bool? ClearMaxDeliveriesPerHour = null,
    bool? ClearMaxDeliveriesPerDay = null);

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
