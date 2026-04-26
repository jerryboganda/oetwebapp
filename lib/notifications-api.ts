import { ApiError, apiClient } from './api';
import type {
  AdminNotificationCatalogEntry,
  AdminNotificationHealthSnapshot,
  AdminNotificationPoliciesResponse,
  AdminNotificationPolicyRow,
  AdminNotificationTestEmailRequest,
  NotificationAudienceRole,
  NotificationDeliveryAttemptResponse,
  NotificationFeedResponse,
  NotificationPreferencePatchRequest,
  NotificationPreferencePayload,
  PushSubscriptionPayload,
  PushSubscriptionRegistrationResponse,
} from './types/notifications';

class NotificationApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = 'NotificationApiError';
  }
}

async function requestJson<T>(path: string, init?: RequestInit): Promise<T> {
  try {
    return await apiClient.request<T>(path, init);
  } catch (error) {
    if (error instanceof ApiError) {
      throw new NotificationApiError(error.status, error.code, error.message);
    }
    throw error;
  }
}

export async function fetchNotifications(params?: {
  page?: number;
  pageSize?: number;
  unreadOnly?: boolean;
  category?: string;
  channel?: string;
}): Promise<NotificationFeedResponse> {
  const searchParams = new URLSearchParams();
  if (params?.page) searchParams.set('page', String(params.page));
  if (params?.pageSize) searchParams.set('pageSize', String(params.pageSize));
  if (params?.unreadOnly) searchParams.set('unreadOnly', 'true');
  if (params?.category) searchParams.set('category', params.category);
  if (params?.channel) searchParams.set('channel', params.channel);

  const query = searchParams.toString();
  return requestJson<NotificationFeedResponse>(`/v1/notifications${query ? `?${query}` : ''}`, {
    method: 'GET',
  });
}

export async function markNotificationRead(notificationId: string): Promise<void> {
  await requestJson(`/v1/notifications/${encodeURIComponent(notificationId)}/read`, {
    method: 'POST',
    body: JSON.stringify({}),
  });
}

export async function markAllNotificationsRead(): Promise<void> {
  await requestJson('/v1/notifications/read-all', {
    method: 'POST',
    body: JSON.stringify({}),
  });
}

export async function fetchNotificationPreferences(): Promise<NotificationPreferencePayload> {
  return requestJson<NotificationPreferencePayload>('/v1/notifications/preferences', {
    method: 'GET',
  });
}

export async function updateNotificationPreferences(
  payload: NotificationPreferencePatchRequest,
): Promise<NotificationPreferencePayload> {
  return requestJson<NotificationPreferencePayload>('/v1/notifications/preferences', {
    method: 'PATCH',
    body: JSON.stringify(payload),
  });
}

export async function createPushSubscription(
  payload: PushSubscriptionPayload,
): Promise<PushSubscriptionRegistrationResponse> {
  return requestJson<PushSubscriptionRegistrationResponse>('/v1/notifications/push-subscriptions', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function deletePushSubscription(subscriptionId: string): Promise<void> {
  await requestJson(`/v1/notifications/push-subscriptions/${encodeURIComponent(subscriptionId)}`, {
    method: 'DELETE',
  });
}

export async function fetchAdminNotificationCatalog(): Promise<AdminNotificationCatalogEntry[]> {
  return requestJson<AdminNotificationCatalogEntry[]>('/v1/admin/notifications/catalog', {
    method: 'GET',
  });
}

export async function fetchAdminNotificationPolicies(): Promise<AdminNotificationPoliciesResponse> {
  return requestJson<AdminNotificationPoliciesResponse>('/v1/admin/notifications/policies', {
    method: 'GET',
  });
}

export async function updateAdminNotificationPolicy(
  audienceRole: NotificationAudienceRole,
  eventKey: string,
  payload: {
    inAppEnabled?: boolean;
    emailEnabled?: boolean;
    pushEnabled?: boolean;
    emailMode?: 'off' | 'immediate' | 'daily_digest';
  },
): Promise<AdminNotificationPolicyRow> {
  return requestJson<AdminNotificationPolicyRow>(
    `/v1/admin/notifications/policies/${encodeURIComponent(audienceRole)}/${encodeURIComponent(eventKey)}`,
    {
      method: 'PUT',
      body: JSON.stringify(payload),
    },
  );
}

export async function fetchAdminNotificationHealth(): Promise<AdminNotificationHealthSnapshot> {
  return requestJson<AdminNotificationHealthSnapshot>('/v1/admin/notifications/health', {
    method: 'GET',
  });
}

export async function fetchAdminNotificationDeliveries(params?: {
  page?: number;
  pageSize?: number;
  status?: string;
  channel?: string;
  audienceRole?: NotificationAudienceRole;
  eventKey?: string;
}): Promise<NotificationDeliveryAttemptResponse> {
  const searchParams = new URLSearchParams();
  if (params?.page) searchParams.set('page', String(params.page));
  if (params?.pageSize) searchParams.set('pageSize', String(params.pageSize));
  if (params?.status) searchParams.set('status', params.status);
  if (params?.channel) searchParams.set('channel', params.channel);
  if (params?.audienceRole) searchParams.set('audienceRole', params.audienceRole);
  if (params?.eventKey) searchParams.set('eventKey', params.eventKey);

  const query = searchParams.toString();
  return requestJson<NotificationDeliveryAttemptResponse>(
    `/v1/admin/notifications/deliveries${query ? `?${query}` : ''}`,
    { method: 'GET' },
  );
}

export async function sendAdminNotificationTestEmail(
  payload: AdminNotificationTestEmailRequest,
): Promise<void> {
  await requestJson('/v1/admin/notifications/test-email', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}
