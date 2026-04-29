import { ApiError, apiClient } from './api';
import type {
  AdminNotificationCatalogEntry,
  AdminNotificationConsentResponse,
  AdminNotificationHealthSnapshot,
  AdminNotificationPoliciesResponse,
  AdminNotificationPolicyRow,
  AdminNotificationSuppressionCreateRequest,
  AdminNotificationSuppressionResponse,
  AdminNotificationTestEmailRequest,
  NotificationAudienceRole,
  NotificationChannel,
  NotificationConsentChannel,
  NotificationConsentItem,
  NotificationConsentUpdateRequest,
  NotificationDeliveryAttemptResponse,
  NotificationFeedResponse,
  NotificationPreferencePatchRequest,
  NotificationPreferencePayload,
  NotificationSuppressibleChannel,
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

export async function fetchNotificationConsents(): Promise<NotificationConsentItem[]> {
  return requestJson<NotificationConsentItem[]>('/v1/notifications/consents', {
    method: 'GET',
  });
}

export async function updateNotificationConsent(
  channel: NotificationConsentChannel,
  payload: NotificationConsentUpdateRequest,
): Promise<NotificationConsentItem> {
  return requestJson<NotificationConsentItem>(`/v1/notifications/consents/${encodeURIComponent(channel)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
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
    maxDeliveriesPerHour?: number | null;
    maxDeliveriesPerDay?: number | null;
    clearMaxDeliveriesPerHour?: boolean;
    clearMaxDeliveriesPerDay?: boolean;
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

export async function resetAdminNotificationPolicyOverride(
  audienceRole: NotificationAudienceRole,
  eventKey: string,
): Promise<AdminNotificationPolicyRow> {
  return requestJson<AdminNotificationPolicyRow>(
    `/v1/admin/notifications/policies/${encodeURIComponent(audienceRole)}/${encodeURIComponent(eventKey)}`,
    { method: 'DELETE' },
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

export async function fetchAdminNotificationConsents(params?: {
  page?: number;
  pageSize?: number;
  authAccountId?: string;
  channel?: NotificationConsentChannel;
}): Promise<AdminNotificationConsentResponse> {
  const searchParams = new URLSearchParams();
  if (params?.page) searchParams.set('page', String(params.page));
  if (params?.pageSize) searchParams.set('pageSize', String(params.pageSize));
  if (params?.authAccountId) searchParams.set('authAccountId', params.authAccountId);
  if (params?.channel) searchParams.set('channel', params.channel);

  const query = searchParams.toString();
  return requestJson<AdminNotificationConsentResponse>(
    `/v1/admin/notifications/consents${query ? `?${query}` : ''}`,
    { method: 'GET' },
  );
}

export async function setAdminNotificationConsent(
  authAccountId: string,
  channel: NotificationConsentChannel,
  payload: NotificationConsentUpdateRequest,
): Promise<NotificationConsentItem> {
  return requestJson<NotificationConsentItem>(
    `/v1/admin/notifications/consents/${encodeURIComponent(authAccountId)}/${encodeURIComponent(channel)}`,
    {
      method: 'PUT',
      body: JSON.stringify(payload),
    },
  );
}

export async function fetchAdminNotificationSuppressions(params?: {
  page?: number;
  pageSize?: number;
  authAccountId?: string;
  channel?: NotificationSuppressibleChannel;
  activeOnly?: boolean;
}): Promise<AdminNotificationSuppressionResponse> {
  const searchParams = new URLSearchParams();
  if (params?.page) searchParams.set('page', String(params.page));
  if (params?.pageSize) searchParams.set('pageSize', String(params.pageSize));
  if (params?.authAccountId) searchParams.set('authAccountId', params.authAccountId);
  if (params?.channel) searchParams.set('channel', params.channel);
  if (typeof params?.activeOnly === 'boolean') searchParams.set('activeOnly', String(params.activeOnly));

  const query = searchParams.toString();
  return requestJson<AdminNotificationSuppressionResponse>(
    `/v1/admin/notifications/suppressions${query ? `?${query}` : ''}`,
    { method: 'GET' },
  );
}

export async function createAdminNotificationSuppression(
  payload: AdminNotificationSuppressionCreateRequest,
): Promise<AdminNotificationSuppressionResponse['items'][number]> {
  return requestJson<AdminNotificationSuppressionResponse['items'][number]>(
    '/v1/admin/notifications/suppressions',
    {
      method: 'POST',
      body: JSON.stringify(payload),
    },
  );
}

export async function releaseAdminNotificationSuppression(
  suppressionId: string,
): Promise<AdminNotificationSuppressionResponse['items'][number]> {
  return requestJson<AdminNotificationSuppressionResponse['items'][number]>(
    `/v1/admin/notifications/suppressions/${encodeURIComponent(suppressionId)}`,
    { method: 'DELETE' },
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
