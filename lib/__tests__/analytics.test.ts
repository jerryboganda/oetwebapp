import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const authClientMock = vi.hoisted(() => ({
  ensureFreshAccessToken: vi.fn(),
}));

vi.mock('../auth-client', () => authClientMock);

describe('analytics transport', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    vi.clearAllMocks();
    globalThis.fetch = vi.fn();
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.resetModules();
  });

  it('buffers events until the browser transport is initialized and then flushes them', async () => {
    authClientMock.ensureFreshAccessToken.mockResolvedValue('access-token-123');
    vi.mocked(globalThis.fetch).mockResolvedValue(new Response(null, { status: 204 }));

    const { analytics, initializeAnalyticsTransport } = await import('../analytics');

    analytics.track('task_started', { taskId: 'wt-001', subtest: 'writing' });
    expect(globalThis.fetch).not.toHaveBeenCalled();

    initializeAnalyticsTransport();

    await vi.waitFor(() => {
      expect(globalThis.fetch).toHaveBeenCalledTimes(1);
    });

    const [requestUrl, requestInit] = vi.mocked(globalThis.fetch).mock.calls[0];
    expect(String(requestUrl)).toContain('/v1/analytics/events');
    expect((requestInit as RequestInit).headers).toMatchObject({
      Authorization: 'Bearer access-token-123',
      'Content-Type': 'application/json',
    });

    const requestBody = JSON.parse(String((requestInit as RequestInit).body));
    expect(requestBody.eventName).toBe('task_started');
    expect(requestBody.properties.taskId).toBe('wt-001');
    expect(requestBody.properties.subtest).toBe('writing');
  });
});