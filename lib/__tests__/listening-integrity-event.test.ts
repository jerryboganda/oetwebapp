/**
 * Listening integrity events are fire-and-forget telemetry (every blur / focus /
 * click / audio tick). They must never be retried by the shared API client: on
 * 30 Sep 2026 a burst of 409 concurrency conflicts was replayed after 1 s and 3 s
 * in synchronised waves and used up every database connection.
 */

import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockEnsureFreshAccessToken, mockFetchWithTimeout } = vi.hoisted(() => ({
  mockEnsureFreshAccessToken: vi.fn(),
  mockFetchWithTimeout: vi.fn(),
}));

vi.mock('../auth-client', () => ({
  ensureFreshAccessToken: mockEnsureFreshAccessToken,
}));

vi.mock('../env', () => ({
  env: { apiBaseUrl: '' },
}));

vi.mock('../network/fetch-with-timeout', () => ({
  fetchWithTimeout: mockFetchWithTimeout,
}));

export {};

const { recordListeningIntegrityEvent } = await import('../listening-api');

function jsonResponse(body: unknown, status: number) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

describe('recordListeningIntegrityEvent', () => {
  beforeEach(() => {
    mockEnsureFreshAccessToken.mockResolvedValue('access-token');
    mockFetchWithTimeout.mockReset();
    if (typeof document !== 'undefined') {
      Object.defineProperty(document, 'cookie', {
        configurable: true,
        get: () => 'oet_csrf=csrf-token',
      });
    }
  });

  it('posts the event once to the attempt route', async () => {
    mockFetchWithTimeout.mockResolvedValue(new Response(null, { status: 204 }));

    await recordListeningIntegrityEvent('lat-1', 'window_blur', '{"a":1}', '2026-09-30T21:32:35.000Z');

    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(1);
    const [url, init] = mockFetchWithTimeout.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('/v1/listening-papers/attempts/lat-1/integrity-events');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({
      eventType: 'window_blur',
      details: '{"a":1}',
      occurredAt: '2026-09-30T21:32:35.000Z',
    });
  });

  it.each([
    ['a retryable 409 concurrency conflict', 409, { code: 'concurrency_conflict', retryable: true }],
    ['a 500 from a database that is out of connections', 500, { code: 'internal_server_error' }],
    ['a 429 rate limit', 429, { code: 'rate_limited' }],
  ])('does not retry %s', async (_label, status, body) => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse(body, status));

    await expect(recordListeningIntegrityEvent('lat-1', 'audio_progress')).rejects.toMatchObject({ status });

    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(1);
  });

  it('does not retry a network failure', async () => {
    mockFetchWithTimeout.mockRejectedValue(new TypeError('Failed to fetch'));

    await expect(recordListeningIntegrityEvent('lat-1', 'page_hidden')).rejects.toMatchObject({
      code: 'network_error',
    });

    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(1);
  });
});
