import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

const { mockEnsureFreshToken } = vi.hoisted(() => ({
  mockEnsureFreshToken: vi.fn(),
}));

vi.mock('../auth-client', () => ({
  ensureFreshAccessToken: mockEnsureFreshToken,
}));

vi.mock('../env', () => ({
  env: { apiBaseUrl: 'https://api.test' },
}));

import {
  fetchNotifications,
  markNotificationRead,
  markAllNotificationsRead,
  fetchNotificationPreferences,
  updateNotificationPreferences,
  createPushSubscription,
  deletePushSubscription,
  fetchAdminNotificationCatalog,
  updateAdminNotificationPolicy,
  fetchAdminNotificationDeliveries,
  sendAdminNotificationTestEmail,
} from '../notifications-api';

describe('notifications-api', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    mockEnsureFreshToken.mockReset();
    mockEnsureFreshToken.mockResolvedValue('TOKEN-XYZ');
    fetchSpy = vi.spyOn(globalThis, 'fetch');
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  function jsonResponse(body: unknown, init?: ResponseInit): Response {
    return new Response(JSON.stringify(body), {
      status: 200,
      headers: { 'content-type': 'application/json' },
      ...init,
    });
  }

  // ── auth guard ────────────────────────────────────────────────────────

  it('throws NotificationApiError when no access token is available', async () => {
    mockEnsureFreshToken.mockResolvedValue(null);
    await expect(fetchNotifications()).rejects.toMatchObject({
      name: 'NotificationApiError',
      status: 401,
      code: 'not_authenticated',
    });
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('attaches the bearer token and JSON content-type for body requests', async () => {
    fetchSpy.mockResolvedValue(jsonResponse({ ok: true }));

    await markNotificationRead('n-1');

    const [url, init] = fetchSpy.mock.calls[0]!;
    expect(url).toBe('https://api.test/v1/notifications/n-1/read');
    expect((init as RequestInit).method).toBe('POST');
    const headers = new Headers((init as RequestInit).headers);
    expect(headers.get('Authorization')).toBe('Bearer TOKEN-XYZ');
    expect(headers.get('Content-Type')).toBe('application/json');
  });

  // ── feed querystring ──────────────────────────────────────────────────

  it('builds the feed URL without a querystring when no params are given', async () => {
    fetchSpy.mockResolvedValue(jsonResponse({ items: [], unreadCount: 0 }));
    await fetchNotifications();
    expect(fetchSpy.mock.calls[0]![0]).toBe('https://api.test/v1/notifications');
  });

  it('serialises feed query params correctly', async () => {
    fetchSpy.mockResolvedValue(jsonResponse({ items: [], unreadCount: 0 }));
    await fetchNotifications({
      page: 2,
      pageSize: 25,
      unreadOnly: true,
      category: 'billing',
      channel: 'in_app',
    });
    const url = String(fetchSpy.mock.calls[0]![0]);
    expect(url).toContain('page=2');
    expect(url).toContain('pageSize=25');
    expect(url).toContain('unreadOnly=true');
    expect(url).toContain('category=billing');
    expect(url).toContain('channel=in_app');
  });

  it('omits unreadOnly when explicitly false', async () => {
    fetchSpy.mockResolvedValue(jsonResponse({ items: [], unreadCount: 0 }));
    await fetchNotifications({ unreadOnly: false });
    expect(String(fetchSpy.mock.calls[0]![0])).not.toContain('unreadOnly');
  });

  // ── id encoding ───────────────────────────────────────────────────────

  it('URL-encodes the notification id when marking as read', async () => {
    fetchSpy.mockResolvedValue(jsonResponse({ ok: true }));
    await markNotificationRead('weird id/with?chars');
    expect(fetchSpy.mock.calls[0]![0]).toBe(
      'https://api.test/v1/notifications/weird%20id%2Fwith%3Fchars/read',
    );
  });

  it('URL-encodes the subscription id for DELETE', async () => {
    fetchSpy.mockResolvedValue(new Response(null, { status: 204 }));
    await deletePushSubscription('sub@1/foo');
    expect(fetchSpy.mock.calls[0]![0]).toBe(
      'https://api.test/v1/notifications/push-subscriptions/sub%401%2Ffoo',
    );
  });

  // ── 204 handling ──────────────────────────────────────────────────────

  it('returns undefined for 204 No Content responses', async () => {
    fetchSpy.mockResolvedValue(new Response(null, { status: 204 }));
    const result = await markAllNotificationsRead();
    expect(result).toBeUndefined();
  });

  // ── error mapping ─────────────────────────────────────────────────────

  it('parses code/message from JSON error bodies', async () => {
    fetchSpy.mockResolvedValue(
      new Response(
        JSON.stringify({ code: 'invalid_payload', message: 'Bad shape' }),
        { status: 422, headers: { 'content-type': 'application/json' } },
      ),
    );

    await expect(updateNotificationPreferences({} as never)).rejects.toMatchObject({
      name: 'NotificationApiError',
      status: 422,
      code: 'invalid_payload',
      message: 'Bad shape',
    });
  });

  it('falls back to a default message when the error body is not JSON', async () => {
    fetchSpy.mockResolvedValue(
      new Response('<html>oops</html>', {
        status: 500,
        headers: { 'content-type': 'text/html' },
      }),
    );

    await expect(fetchNotificationPreferences()).rejects.toMatchObject({
      name: 'NotificationApiError',
      status: 500,
      code: 'notification_request_failed',
      message: expect.stringContaining('500'),
    });
  });

  // ── verb / payload ────────────────────────────────────────────────────

  it('PATCHes preferences with a JSON body', async () => {
    fetchSpy.mockResolvedValue(jsonResponse({ ok: true }));
    await updateNotificationPreferences({ marketingOptIn: true } as never);
    const init = fetchSpy.mock.calls[0]![1] as RequestInit;
    expect(init.method).toBe('PATCH');
    expect(init.body).toBe(JSON.stringify({ marketingOptIn: true }));
  });

  it('POSTs push subscription registration', async () => {
    fetchSpy.mockResolvedValue(jsonResponse({ id: 'sub-1' }));
    await createPushSubscription({ endpoint: 'https://e', keys: {} } as never);
    const init = fetchSpy.mock.calls[0]![1] as RequestInit;
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({
      endpoint: 'https://e',
      keys: {},
    });
  });

  // ── admin endpoints ───────────────────────────────────────────────────

  it('GETs the admin notification catalog', async () => {
    fetchSpy.mockResolvedValue(jsonResponse([{ key: 'welcome' }]));
    const result = await fetchAdminNotificationCatalog();
    expect(fetchSpy.mock.calls[0]![0]).toBe('https://api.test/v1/admin/notifications/catalog');
    expect(result).toEqual([{ key: 'welcome' }]);
  });

  it('PUTs an admin notification policy with encoded path segments', async () => {
    fetchSpy.mockResolvedValue(jsonResponse({ ok: true }));
    await updateAdminNotificationPolicy('learner', 'billing/dunning' as never, {
      inAppEnabled: true,
      emailMode: 'daily_digest',
    });
    expect(fetchSpy.mock.calls[0]![0]).toBe(
      'https://api.test/v1/admin/notifications/policies/learner/billing%2Fdunning',
    );
    const init = fetchSpy.mock.calls[0]![1] as RequestInit;
    expect(init.method).toBe('PUT');
  });

  it('serialises admin delivery filter params', async () => {
    fetchSpy.mockResolvedValue(jsonResponse({ items: [] }));
    await fetchAdminNotificationDeliveries({
      page: 1,
      pageSize: 10,
      status: 'failed',
      channel: 'email',
      audienceRole: 'learner',
      eventKey: 'welcome',
    });
    const url = String(fetchSpy.mock.calls[0]![0]);
    expect(url).toContain('status=failed');
    expect(url).toContain('channel=email');
    expect(url).toContain('audienceRole=learner');
    expect(url).toContain('eventKey=welcome');
  });

  it('does not throw when admin test email returns 204', async () => {
    fetchSpy.mockResolvedValue(new Response(null, { status: 204 }));
    await expect(
      sendAdminNotificationTestEmail({ recipient: 'x@y' } as never),
    ).resolves.toBeUndefined();
  });
});
