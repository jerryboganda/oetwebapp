import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const mockFetchWithTimeout = vi.hoisted(() => vi.fn());

vi.mock('@/lib/auth-client', () => ({
  ensureFreshAccessToken: vi.fn(async () => null),
}));

vi.mock('@/lib/env', () => ({
  env: { apiBaseUrl: '' },
}));

vi.mock('@/lib/network/fetch-with-timeout', () => ({
  fetchWithTimeout: mockFetchWithTimeout,
}));

import { apiRequest, MAX_RETRY_AFTER_MS, parseRetryAfter, retryDelayWithJitter } from '../client';

function failure(status: number, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify({ code: 'upstream_error', message: 'fail', retryable: true }), {
    status,
    headers: { 'content-type': 'application/json', ...headers },
  });
}

function success() {
  return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
}

describe('parseRetryAfter', () => {
  it('reads delta-seconds as milliseconds', () => {
    expect(parseRetryAfter('2')).toBe(2000);
    expect(parseRetryAfter(' 0 ')).toBe(0);
  });

  it('reads an HTTP date relative to now, never negative', () => {
    const now = Date.parse('Wed, 21 Oct 2026 07:28:00 GMT');
    expect(parseRetryAfter('Wed, 21 Oct 2026 07:28:05 GMT', now)).toBe(5000);
    expect(parseRetryAfter('Wed, 21 Oct 2026 07:27:00 GMT', now)).toBe(0);
  });

  it('is null when absent or unreadable', () => {
    expect(parseRetryAfter(null)).toBeNull();
    expect(parseRetryAfter(undefined)).toBeNull();
    expect(parseRetryAfter('')).toBeNull();
    expect(parseRetryAfter('soon')).toBeNull();
  });
});

describe('retryDelayWithJitter', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('spreads a schedule slot over 50-100% of it, so the worst case is unchanged', () => {
    const random = vi.spyOn(Math, 'random');
    random.mockReturnValue(0);
    expect(retryDelayWithJitter(1000)).toBe(500);
    random.mockReturnValue(0.5);
    expect(retryDelayWithJitter(1000)).toBe(750);
    random.mockReturnValue(0.999999);
    expect(retryDelayWithJitter(3000)).toBeLessThanOrEqual(3000);
    expect(retryDelayWithJitter(3000)).toBeGreaterThanOrEqual(2999);
  });
});

describe('apiRequest retry policy', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    mockFetchWithTimeout.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('waits out a short Retry-After instead of retrying earlier', async () => {
    vi.spyOn(Math, 'random').mockReturnValue(0); // jittered slot would be 500 ms
    mockFetchWithTimeout
      .mockResolvedValueOnce(failure(503, { 'retry-after': '2' }))
      .mockResolvedValueOnce(success());

    const result = apiRequest('/v1/things');

    await vi.advanceTimersByTimeAsync(1999);
    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    await expect(result).resolves.toEqual({ ok: true });
    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(2);
  });

  it('fails now when Retry-After is longer than it is willing to wait', async () => {
    mockFetchWithTimeout.mockResolvedValue(failure(503, { 'retry-after': String(MAX_RETRY_AFTER_MS / 1000 + 1) }));

    await expect(apiRequest('/v1/things')).rejects.toMatchObject({ status: 503 });

    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(1);
  });

  it.each(['POST', 'PUT', 'PATCH', 'DELETE'])('does not retry a 429 on a %s', async (method) => {
    mockFetchWithTimeout.mockResolvedValue(failure(429));

    await expect(apiRequest('/v1/things', { method, body: '{}' })).rejects.toMatchObject({ status: 429 });

    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(1);
  });

  it('still retries a 429 on a read, with jittered backoff', async () => {
    vi.spyOn(Math, 'random').mockReturnValue(0);
    mockFetchWithTimeout
      .mockResolvedValueOnce(failure(429))
      .mockResolvedValueOnce(success());

    const result = apiRequest('/v1/things');

    await vi.advanceTimersByTimeAsync(499);
    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    await expect(result).resolves.toEqual({ ok: true });
    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(2);
  });

  it('keeps retrying a 5xx on a write exactly as before (only 429 is excluded)', async () => {
    vi.spyOn(Math, 'random').mockReturnValue(1);
    mockFetchWithTimeout
      .mockResolvedValueOnce(failure(503))
      .mockResolvedValueOnce(success());

    const result = apiRequest('/v1/things', { method: 'POST', body: '{}' });

    await vi.advanceTimersByTimeAsync(1000);
    await expect(result).resolves.toEqual({ ok: true });
    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(2);
  });

  it('backs network errors off with jitter and still gives up after the same number of attempts', async () => {
    vi.spyOn(Math, 'random').mockReturnValue(0); // 500 ms then 1500 ms
    mockFetchWithTimeout.mockRejectedValue(new TypeError('Failed to fetch'));

    const settled = apiRequest('/v1/things').catch((error: unknown) => error);

    await vi.advanceTimersByTimeAsync(499);
    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1499);
    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1);
    expect(await settled).toMatchObject({ code: 'network_error' });
    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(3);
  });

  it('never retries when the caller disabled retries (maxRetries: 0)', async () => {
    mockFetchWithTimeout.mockResolvedValue(failure(503));

    await expect(apiRequest('/v1/zip', { method: 'POST', body: '{}' }, { maxRetries: 0 }))
      .rejects.toMatchObject({ status: 503 });

    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(1);
  });
});
