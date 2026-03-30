import { expect, type APIRequestContext } from '@playwright/test';
import { authHeadersForRole, createDisposableWritingReviewRequest } from './api-auth';
import { seededAccounts, type SeededRole } from './auth';

const apiBaseURL = (process.env.NEXT_PUBLIC_API_BASE_URL ?? 'http://localhost:5198').replace(/\/$/, '');

type NotificationProofResponse = {
  notificationEventId: string;
  eventKey: string;
  audienceRole: string;
  recipientEmail: string;
  recipientAuthAccountId: string;
  title: string;
  body: string;
  actionUrl: string | null;
  severity: string;
  inboxItemId: string | null;
  processedImmediately: boolean;
  digestDispatchedImmediately: boolean;
};

type NotificationFeedResponse = {
  items: Array<{
    id: string;
    eventKey: string;
    title: string;
    body: string;
    actionUrl: string | null;
    isRead: boolean;
  }>;
  totalCount: number;
  unreadCount: number;
  page: number;
  pageSize: number;
};

type NotificationProofRequest = {
  eventKey: string;
  recipientEmail: string;
  tokens?: Record<string, string | null>;
  entityType?: string;
  entityId?: string;
  versionOrDateBucket?: string;
  processImmediately?: boolean;
  dispatchDigestImmediately?: boolean;
};

async function expectOkResponse(
  response: {
    ok(): boolean;
    status(): number;
    text(): Promise<string>;
  },
  message: string,
) {
  if (response.ok()) {
    return;
  }

  const body = await response.text().catch(() => '<no response body>');
  expect(response.ok(), `${message}\nStatus: ${response.status()}\nBody: ${body}`).toBeTruthy();
}

export async function ensureNotificationEventEnabled(
  request: APIRequestContext,
  role: SeededRole,
  eventKey: string,
) {
  const headers = await authHeadersForRole(request, role);
  const response = await request.patch(`${apiBaseURL}/v1/notifications/preferences`, {
    headers,
    data: {
      globalInAppEnabled: true,
      globalEmailEnabled: true,
      eventPreferences: {
        [eventKey]: {
          inAppEnabled: true,
          emailEnabled: true,
          pushEnabled: false,
          emailMode: 'immediate',
        },
      },
    },
  });

  await expectOkResponse(response, `Expected notification preferences patch to succeed for ${role}/${eventKey}`);
}

export async function fetchNotificationFeedForRole(
  request: APIRequestContext,
  role: SeededRole,
  pageSize = 50,
) {
  const headers = await authHeadersForRole(request, role);
  const response = await request.get(`${apiBaseURL}/v1/notifications?page=1&pageSize=${pageSize}`, {
    headers,
  });

  await expectOkResponse(response, `Expected notification feed fetch to succeed for ${role}`);
  return response.json() as Promise<NotificationFeedResponse>;
}

export async function triggerNotificationProof(
  request: APIRequestContext,
  payload: NotificationProofRequest,
) {
  const headers = await authHeadersForRole(request, 'admin');
  const response = await request.post(`${apiBaseURL}/v1/admin/notifications/proof/trigger`, {
    headers,
    data: {
      processImmediately: true,
      dispatchDigestImmediately: false,
      ...payload,
    },
  });

  await expectOkResponse(response, `Expected notification proof trigger for ${payload.eventKey} to succeed`);
  return response.json() as Promise<NotificationProofResponse>;
}

export async function triggerLearnerNotificationProof(
  request: APIRequestContext,
  eventKey: string,
  tokens?: Record<string, string | null>,
) {
  await ensureNotificationEventEnabled(request, 'learner', eventKey);
  return triggerNotificationProof(request, {
    eventKey,
    recipientEmail: seededAccounts.learner.email,
    tokens,
  });
}

export async function triggerExpertAssignedNotificationProof(request: APIRequestContext) {
  const review = await createDisposableWritingReviewRequest(request);
  const message = `QA expert notification proof ${Date.now()}`;
  await ensureNotificationEventEnabled(request, 'expert', 'ExpertReviewAssigned');
  const proof = await triggerNotificationProof(request, {
    eventKey: 'ExpertReviewAssigned',
    recipientEmail: seededAccounts.expert.email,
    tokens: {
      reviewRequestId: review.reviewRequestId,
      message,
    },
  });

  return {
    proof,
    reviewRequestId: review.reviewRequestId,
    message,
  };
}

export async function triggerAdminNotificationProof(request: APIRequestContext) {
  const message = `QA admin notification proof ${Date.now()}`;
  await ensureNotificationEventEnabled(request, 'admin', 'AdminNotificationDeliveryFailureAlert');
  const proof = await triggerNotificationProof(request, {
    eventKey: 'AdminNotificationDeliveryFailureAlert',
    recipientEmail: seededAccounts.admin.email,
    tokens: {
      message,
    },
  });

  return { proof, message };
}
