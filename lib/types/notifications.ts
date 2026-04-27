export type NotificationAudienceRole = 'learner' | 'expert' | 'admin';
export type NotificationChannel = 'in_app' | 'email' | 'push';
export type NotificationSeverity = 'info' | 'success' | 'warning' | 'critical';
export type NotificationEmailMode = 'off' | 'immediate' | 'daily_digest';
export type NotificationDeliveryStatus = 'pending' | 'sent' | 'suppressed' | 'failed' | 'expired';

export interface NotificationFeedItem {
  id: string;
  eventKey: string;
  category: string;
  title: string;
  body: string;
  actionUrl: string | null;
  severity: NotificationSeverity;
  isRead: boolean;
  channels: NotificationChannel[];
  createdAt: string;
  readAt: string | null;
}

export interface NotificationEventPreferencePayload {
  inAppEnabled: boolean | null;
  emailEnabled: boolean | null;
  pushEnabled: boolean | null;
  emailMode: NotificationEmailMode | null;
}

export interface NotificationPreferencePayload {
  timezone: string;
  globalInAppEnabled: boolean;
  globalEmailEnabled: boolean;
  globalPushEnabled: boolean;
  quietHoursEnabled: boolean;
  quietHoursStartLocalTime: string | null;
  quietHoursEndLocalTime: string | null;
  eventPreferences: Record<string, NotificationEventPreferencePayload>;
  legacyLearnerSettings: Record<string, unknown>;
}

export interface NotificationPreferencePatchRequest {
  timezone?: string;
  globalInAppEnabled?: boolean;
  globalEmailEnabled?: boolean;
  globalPushEnabled?: boolean;
  quietHoursEnabled?: boolean;
  quietHoursStartLocalTime?: string | null;
  quietHoursEndLocalTime?: string | null;
  eventPreferences?: Record<string, NotificationEventPreferencePayload>;
}

export interface PushSubscriptionPayload {
  endpoint: string;
  p256dh: string;
  auth: string;
  expiresAt: string | null;
  userAgent: string | null;
}

export interface PushSubscriptionRegistrationResponse {
  subscriptionId: string;
}

export interface NotificationFeedResponse {
  items: NotificationFeedItem[];
  totalCount: number;
  unreadCount: number;
  page: number;
  pageSize: number;
}

export interface AdminNotificationCatalogEntry {
  eventKey: string;
  audienceRole: NotificationAudienceRole;
  category: string;
  label: string;
  description: string;
  defaultSeverity: NotificationSeverity;
  defaultInAppEnabled: boolean;
  defaultEmailEnabled: boolean;
  defaultPushEnabled: boolean;
  defaultEmailMode: NotificationEmailMode;
}

export interface AdminNotificationPolicyRow {
  audienceRole: NotificationAudienceRole;
  eventKey: string;
  category: string;
  label: string;
  inAppEnabled: boolean;
  emailEnabled: boolean;
  pushEnabled: boolean;
  emailMode: NotificationEmailMode;
  isOverride: boolean;
  updatedAt: string | null;
  updatedByAdminId: string | null;
  updatedByAdminName: string | null;
}

export interface AdminNotificationPoliciesResponse {
  globalEmailEnabledByAudience: Record<NotificationAudienceRole, boolean>;
  globalChannelEnabledByAudience?: Record<NotificationAudienceRole, AdminNotificationAudienceChannelPolicy>;
  rows: AdminNotificationPolicyRow[];
}

export interface AdminNotificationAudienceChannelPolicy {
  inAppEnabled: boolean;
  emailEnabled: boolean;
  pushEnabled: boolean;
}

export interface AdminNotificationHealthChannelSnapshot {
  channel: NotificationChannel;
  sentLast24Hours: number;
  failedLast24Hours: number;
  suppressedLast24Hours: number;
}

export interface AdminNotificationFailureQueueItem {
  eventId: string;
  eventKey: string;
  audienceRole: NotificationAudienceRole;
  channel: NotificationChannel;
  status: NotificationDeliveryStatus;
  errorCode: string | null;
  errorMessage: string | null;
  attemptedAt: string;
}

export interface AdminNotificationHealthSnapshot {
  generatedAt: string;
  queuedEvents: number;
  failedEvents: number;
  unreadInboxItems: number;
  failedDeliveriesLast24Hours: number;
  pendingDigestJobs: number;
  activePushSubscriptions: number;
  expiredPushSubscriptions: number;
  channels: AdminNotificationHealthChannelSnapshot[];
  failureQueue: AdminNotificationFailureQueueItem[];
}

export interface NotificationDeliveryAttemptItem {
  id: string;
  eventId: string;
  eventKey: string;
  audienceRole: NotificationAudienceRole;
  channel: NotificationChannel;
  status: NotificationDeliveryStatus;
  provider: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  attemptedAt: string;
  completedAt: string | null;
}

export interface NotificationDeliveryAttemptResponse {
  items: NotificationDeliveryAttemptItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface NotificationRealtimeEnvelope {
  type: string;
  notification: NotificationFeedItem;
  unreadCount: number;
}

export interface AdminNotificationTestEmailRequest {
  recipientEmail: string;
  eventKey: string;
  audienceRole: NotificationAudienceRole;
}
