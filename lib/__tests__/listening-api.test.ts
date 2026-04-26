import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

const { mockEnsureFreshToken, mockFetchWithTimeout } = vi.hoisted(() => ({
  mockEnsureFreshToken: vi.fn(),
  mockFetchWithTimeout: vi.fn(),
}));

vi.mock('../auth-client', () => ({
  ensureFreshAccessToken: mockEnsureFreshToken,
}));

vi.mock('../env', () => ({
  env: { apiBaseUrl: 'https://api.test' },
}));

vi.mock('../network/fetch-with-timeout', () => ({
  fetchWithTimeout: mockFetchWithTimeout,
}));

import {
  getListeningHome,
  getListeningSession,
  startListeningAttempt,
  saveListeningAnswer,
  heartbeatListeningAttempt,
  submitListeningAttempt,
  getListeningResult,
  getListeningDrill,
} from '../listening-api';

function jsonResponse(body: unknown, init?: ResponseInit): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'content-type': 'application/json' },
    ...init,
  });
}

describe('listening-api', () => {
  beforeEach(() => {
    mockEnsureFreshToken.mockReset();
    mockFetchWithTimeout.mockReset();
    mockEnsureFreshToken.mockResolvedValue('TKN');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // ── URL composition ────────────────────────────────────────────────────

  it('prepends the API base URL for relative paths', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ intro: '' }));
    await getListeningHome();
    expect(mockFetchWithTimeout.mock.calls[0]![0]).toBe('https://api.test/v1/listening/home');
  });

  // ── auth header ────────────────────────────────────────────────────────

  it('includes Authorization when a token is available', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ intro: '' }));
    await getListeningHome();
    const init = mockFetchWithTimeout.mock.calls[0]![1] as RequestInit;
    const headers = new Headers(init.headers);
    expect(headers.get('Authorization')).toBe('Bearer TKN');
    expect(headers.get('Accept')).toBe('application/json');
  });

  it('omits Authorization when no token is available', async () => {
    mockEnsureFreshToken.mockResolvedValue(null);
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ intro: '' }));
    await getListeningHome();
    const init = mockFetchWithTimeout.mock.calls[0]![1] as RequestInit;
    const headers = new Headers(init.headers);
    expect(headers.get('Authorization')).toBeNull();
  });

  it('adds Content-Type for string bodies and not for missing bodies', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ ok: true }));
    await startListeningAttempt('paper-1', 'practice');

    const startInit = mockFetchWithTimeout.mock.calls[0]![1] as RequestInit;
    const startHeaders = new Headers(startInit.headers);
    expect(startHeaders.get('Content-Type')).toBe('application/json');

    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ ok: true }));
    await getListeningHome();
    const getInit = mockFetchWithTimeout.mock.calls[1]![1] as RequestInit;
    const getHeaders = new Headers(getInit.headers);
    expect(getHeaders.get('Content-Type')).toBeNull();
  });

  // ── querystring composition ───────────────────────────────────────────

  it('builds session URL without querystring when no options', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({}));
    await getListeningSession('paper-1');
    expect(mockFetchWithTimeout.mock.calls[0]![0]).toBe(
      'https://api.test/v1/listening-papers/papers/paper-1/session',
    );
  });

  it('serialises mode + attemptId in the session querystring', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({}));
    await getListeningSession('paper-1', { mode: 'exam', attemptId: 'a-1' });
    const url = String(mockFetchWithTimeout.mock.calls[0]![0]);
    expect(url).toContain('mode=exam');
    expect(url).toContain('attemptId=a-1');
  });

  it('omits attemptId when null/undefined', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({}));
    await getListeningSession('paper-1', { mode: 'practice', attemptId: null });
    const url = String(mockFetchWithTimeout.mock.calls[0]![0]);
    expect(url).toContain('mode=practice');
    expect(url).not.toContain('attemptId');
  });

  it('URL-encodes the paper id and attempt id', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({}));
    await getListeningSession('weird id/with?chars');
    expect(mockFetchWithTimeout.mock.calls[0]![0]).toBe(
      'https://api.test/v1/listening-papers/papers/weird%20id%2Fwith%3Fchars/session',
    );
  });

  // ── verb / payload ────────────────────────────────────────────────────

  it('PUTs answer payload as JSON', async () => {
    mockFetchWithTimeout.mockResolvedValue(new Response(null, { status: 204 }));
    await saveListeningAnswer('a-1', 'q-1', 'B');
    const init = mockFetchWithTimeout.mock.calls[0]![1] as RequestInit;
    expect(init.method).toBe('PUT');
    expect(JSON.parse(String(init.body))).toEqual({ userAnswer: 'B' });
  });

  it('PATCHes heartbeat payload with default deviceType', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ attemptId: 'a-1', elapsedSeconds: 30, lastClientSyncAt: 't' }));
    await heartbeatListeningAttempt('a-1', 30);
    const init = mockFetchWithTimeout.mock.calls[0]![1] as RequestInit;
    expect(init.method).toBe('PATCH');
    expect(JSON.parse(String(init.body))).toEqual({ elapsedSeconds: 30, deviceType: 'web' });
  });

  it('passes a custom deviceType through', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ attemptId: 'a-1', elapsedSeconds: 15, lastClientSyncAt: 't' }));
    await heartbeatListeningAttempt('a-1', 15, 'ios');
    const init = mockFetchWithTimeout.mock.calls[0]![1] as RequestInit;
    expect(JSON.parse(String(init.body))).toEqual({ elapsedSeconds: 15, deviceType: 'ios' });
  });

  it('POSTs to submit attempt with no body', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ attemptId: 'a-1' }));
    await submitListeningAttempt('a-1');
    const init = mockFetchWithTimeout.mock.calls[0]![1] as RequestInit;
    expect(init.method).toBe('POST');
    expect(init.body).toBeUndefined();
  });

  it('GETs review at the canonical endpoint', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ attemptId: 'a-1' }));
    await getListeningResult('a-1');
    expect(mockFetchWithTimeout.mock.calls[0]![0]).toBe(
      'https://api.test/v1/listening-papers/attempts/a-1/review',
    );
  });

  it('builds drill URL with optional paperId / attemptId', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({}));
    await getListeningDrill('d-1', { paperId: 'p-1', attemptId: 'a-1' });
    const url = String(mockFetchWithTimeout.mock.calls[0]![0]);
    expect(url).toContain('/v1/listening-papers/drills/d-1');
    expect(url).toContain('paperId=p-1');
    expect(url).toContain('attemptId=a-1');
  });

  // ── 204 handling ──────────────────────────────────────────────────────

  it('returns undefined for 204 No Content responses', async () => {
    mockFetchWithTimeout.mockResolvedValue(new Response(null, { status: 204 }));
    const result = await saveListeningAnswer('a-1', 'q-1', 'B');
    expect(result).toBeUndefined();
  });

  // ── error paths ───────────────────────────────────────────────────────

  it('throws an HttpError with status + parsed JSON detail on non-OK responses', async () => {
    mockFetchWithTimeout.mockResolvedValue(
      new Response(JSON.stringify({ message: 'Bad attempt id', detail: 'x' }), {
        status: 422,
        headers: { 'content-type': 'application/json' },
      }),
    );

    try {
      await getListeningResult('a-1');
      throw new Error('should have thrown');
    } catch (err) {
      const e = err as Error & { status?: number; detail?: unknown };
      expect(e.message).toBe('Bad attempt id');
      expect(e.status).toBe(422);
      expect(e.detail).toEqual({ message: 'Bad attempt id', detail: 'x' });
    }
  });

  it('falls back to "HTTP <status>" when error body is not JSON', async () => {
    mockFetchWithTimeout.mockResolvedValue(
      new Response('<html>nope</html>', {
        status: 503,
        headers: { 'content-type': 'text/html' },
      }),
    );

    try {
      await getListeningHome();
      throw new Error('should have thrown');
    } catch (err) {
      const e = err as Error & { status?: number; detail?: unknown };
      expect(e.message).toBe('HTTP 503');
      expect(e.status).toBe(503);
      expect(e.detail).toBeNull();
    }
  });
});
