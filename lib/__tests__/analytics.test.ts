import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

const authClientMock = vi.hoisted(() => ({
  ensureFreshAccessToken: vi.fn(),
}));

vi.mock('@/lib/auth-client', () => authClientMock);

function requestUrl(call: unknown[]): string {
  return String(call[0]);
}

function requestBody(call: unknown[]): Record<string, unknown> {
  return JSON.parse(String((call[1] as RequestInit).body));
}

function noContent() {
  return new Response(null, { status: 204 });
}

describe('analytics transport', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    vi.clearAllMocks();
    globalThis.fetch = vi.fn();
    authClientMock.ensureFreshAccessToken.mockResolvedValue('access-token-123');
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.resetModules();
  });

  it('buffers events until the browser transport is initialized and then sends them', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValue(noContent());

    const { analytics, initializeAnalyticsTransport } = await import('../analytics');

    analytics.track('task_started', { taskId: 'wt-001', subtest: 'writing' });
    expect(globalThis.fetch).not.toHaveBeenCalled();

    initializeAnalyticsTransport();
    // Events are collected for a few seconds so they can share a request.
    expect(globalThis.fetch).not.toHaveBeenCalled();
    await analytics.flush();

    expect(globalThis.fetch).toHaveBeenCalledTimes(1);
    const call = vi.mocked(globalThis.fetch).mock.calls[0];
    // A lone event still uses the single-event route.
    expect(requestUrl(call)).toContain('/v1/analytics/events');
    expect(requestUrl(call)).not.toContain('/batch');
    expect((call[1] as RequestInit).headers).toMatchObject({
      Authorization: 'Bearer access-token-123',
      'Content-Type': 'application/json',
    });

    const body = requestBody(call);
    expect(body.eventName).toBe('task_started');
    expect(body.properties).toMatchObject({ taskId: 'wt-001', subtest: 'writing' });
  });

  it('sends events tracked together as one batched request, in order', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValue(noContent());
    const { analytics, initializeAnalyticsTransport } = await import('../analytics');
    initializeAnalyticsTransport();

    analytics.track('page_viewed', { path: '/a' });
    analytics.track('task_started', { taskId: 'wt-001' });
    analytics.track('task_submitted', { taskId: 'wt-001' });
    await analytics.flush();

    expect(globalThis.fetch).toHaveBeenCalledTimes(1);
    const call = vi.mocked(globalThis.fetch).mock.calls[0];
    expect(requestUrl(call)).toContain('/v1/analytics/events/batch');
    const events = requestBody(call).events as Array<{ eventName: string }>;
    expect(events.map((event) => event.eventName)).toEqual(['page_viewed', 'task_started', 'task_submitted']);
  });

  it('sends a full batch without waiting for the window', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValue(noContent());
    const { analytics, initializeAnalyticsTransport } = await import('../analytics');
    initializeAnalyticsTransport();

    for (let index = 0; index < 20; index += 1) {
      analytics.track('page_viewed', { index });
    }

    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledTimes(1));
    expect(requestUrl(vi.mocked(globalThis.fetch).mock.calls[0])).toContain('/batch');
    expect((requestBody(vi.mocked(globalThis.fetch).mock.calls[0]).events as unknown[]).length).toBe(20);
  });

  it('falls back to single events when the API has no batch route, and stops trying it', async () => {
    vi.mocked(globalThis.fetch).mockImplementation(async (input) => (
      String(input).includes('/batch') ? new Response(null, { status: 404 }) : noContent()
    ));
    const { analytics, initializeAnalyticsTransport } = await import('../analytics');
    initializeAnalyticsTransport();

    analytics.track('page_viewed', { path: '/a' });
    analytics.track('task_started', { taskId: 'wt-001' });
    await analytics.flush();

    const urls = vi.mocked(globalThis.fetch).mock.calls.map(requestUrl);
    expect(urls).toHaveLength(3); // the rejected batch, then each event on its own
    expect(urls[0]).toContain('/batch');
    expect(urls[1]).not.toContain('/batch');
    expect(urls[2]).not.toContain('/batch');

    analytics.track('page_viewed', { path: '/b' });
    analytics.track('page_viewed', { path: '/c' });
    await analytics.flush();

    const laterUrls = vi.mocked(globalThis.fetch).mock.calls.slice(3).map(requestUrl);
    expect(laterUrls).toHaveLength(2);
    expect(laterUrls.every((url) => !url.includes('/batch'))).toBe(true);
  });

  it('splits a batch that would be larger than the API accepts', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValue(noContent());
    const { analytics, initializeAnalyticsTransport } = await import('../analytics');
    initializeAnalyticsTransport();

    for (let index = 0; index < 4; index += 1) {
      analytics.track('page_viewed', { notes: 'x'.repeat(20_000), index });
    }
    await analytics.flush();

    const calls = vi.mocked(globalThis.fetch).mock.calls;
    expect(calls).toHaveLength(2);
    expect(calls.every((call) => requestUrl(call).includes('/batch'))).toBe(true);
    const indexes = calls.flatMap((call) => (requestBody(call).events as Array<{ properties: { index: number } }>).map((event) => event.properties.index));
    expect(indexes).toEqual([0, 1, 2, 3]);
  });

  it('re-reads credentials and retries once when a request is rejected as unauthorized', async () => {
    vi.mocked(globalThis.fetch)
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(noContent());
    const { analytics, initializeAnalyticsTransport } = await import('../analytics');
    initializeAnalyticsTransport();

    analytics.track('task_started', { taskId: 'wt-001' });
    await analytics.flush();

    expect(globalThis.fetch).toHaveBeenCalledTimes(2);
    expect(authClientMock.ensureFreshAccessToken).toHaveBeenCalledTimes(2);
  });

  it('sends nothing while there is no access token', async () => {
    authClientMock.ensureFreshAccessToken.mockResolvedValue(null);
    const { analytics, initializeAnalyticsTransport } = await import('../analytics');
    initializeAnalyticsTransport();

    analytics.track('page_viewed', { path: '/sign-in' });
    await analytics.flush();

    expect(globalThis.fetch).not.toHaveBeenCalled();
  });

  it('drops a batch the server rejects instead of retrying it', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValue(new Response(null, { status: 500 }));
    const { analytics, initializeAnalyticsTransport } = await import('../analytics');
    initializeAnalyticsTransport();

    analytics.track('page_viewed', { path: '/a' });
    analytics.track('page_viewed', { path: '/b' });
    await analytics.flush();

    expect(globalThis.fetch).toHaveBeenCalledTimes(1);
  });

  it('sends what is queued with keepalive when the page is hidden', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValue(noContent());
    const { analytics, initializeAnalyticsTransport } = await import('../analytics');
    initializeAnalyticsTransport();

    analytics.track('page_viewed', { path: '/a' });
    analytics.track('page_viewed', { path: '/b' });
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'hidden' });
    try {
      document.dispatchEvent(new Event('visibilitychange'));
      await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledTimes(1));
    } finally {
      Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' });
    }

    expect((vi.mocked(globalThis.fetch).mock.calls[0][1] as RequestInit).keepalive).toBe(true);
  });

  it('flushAnalytics returns once everything is sent, without waiting out its bound', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValue(noContent());
    const { analytics, initializeAnalyticsTransport, flushAnalytics } = await import('../analytics');
    initializeAnalyticsTransport();

    analytics.track('page_viewed', { path: '/a' });
    await flushAnalytics(60_000);

    expect(globalThis.fetch).toHaveBeenCalledTimes(1);
  });

  it('flushAnalytics gives up waiting after its bound when the send hangs', async () => {
    vi.mocked(globalThis.fetch).mockImplementation(() => new Promise<Response>(() => undefined));
    const { analytics, initializeAnalyticsTransport, flushAnalytics } = await import('../analytics');
    vi.useFakeTimers();
    try {
      initializeAnalyticsTransport();
      analytics.track('page_viewed', { path: '/a' });

      let returned = false;
      const waiting = flushAnalytics(1500).then(() => {
        returned = true;
      });

      await vi.advanceTimersByTimeAsync(1499);
      expect(returned).toBe(false);

      await vi.advanceTimersByTimeAsync(1);
      await waiting;
      expect(returned).toBe(true);
    } finally {
      vi.useRealTimers();
    }
  });
});
