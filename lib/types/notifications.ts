export type NotificationAudienceRole = 'learner' | 'expert' | 'admin';
export type NotificationChannel = 'in_app' | 'email' | 'push' | 'sms' | 'whatsapp';
export type NotificationConsentChannel = 'sms' | 'whatsapp';
export type NotificationSuppressibleChannel = 'email' | 'push' | 'sms' | 'whatsapp';
export type NotificationSeverity = 'info' | 'success' | 'warning' | 'critical';
export type NotificationEmailMode = 'off' | 'immediate' | 'daily_digest';
export type NotificationDeliveryStatus =
  | 'pending'
  | 'sent'
  | 'suppressed'
  | 'failed'
  | 'expired'
  | 'created'
  | 'queued'
  | 'delivered'
  | 'opened'
  | 'clicked'
  | 'bounced'
  | 'unsubscribed';

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

export interface NotificationConsentItem {
  authAccountId: string;
  channel: NotificationConsentChannel;
  category: string;
  isGranted: boolean;
  requiresExplicitConsent: boolean;
  source: string;
  reason: string | null;
  grantedAt: string | null;
  revokedAt: string | null;
  updatedAt: string;
}

export interface NotificationConsentUpdateRequest {
  isGranted: boolean;
  source?: string | null;
  reason?: string | null;
  category?: string | null;
}

export interface AdminNotificationConsentResponse {
  items: NotificationConsentItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface NotificationSuppressionItem {
  id: string;
  authAccountId: string;
  channel: NotificationSuppressibleChannel;
  eventKey: string | null;
  isActive: boolean;
  reasonCode: string;
  reason: string | null;
  startsAt: string | null;
  expiresAt: string | null;
  createdAt: string;
  updatedAt: string;
  releasedAt: string | null;
  createdByAdminName: string;
  releasedByAdminName: string | null;
}

export interface AdminNotificationSuppressionCreateRequest {
  authAccountId: string;
  channel: NotificationSuppressibleChannel;
  eventKey?: string | null;
  reasonCode: string;
  reason?: string | null;
  startsAt?: string | null;
  expiresAt?: string | null;
}

export interface AdminNotificationSuppressionResponse {
  items: NotificationSuppressionItem[];
  totalCount: number;
  page: number;
  pageSize: number;
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
  defaultSmsEnabled: boolean;
  defaultWhatsAppEnabled: boolean;
  defaultEmailMode: NotificationEmailMode;
  isPolicyProtected: boolean;
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
  maxDeliveriesPerHour: number | null;
  maxDeliveriesPerDay: number | null;
  isPolicyProtected: boolean;
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
