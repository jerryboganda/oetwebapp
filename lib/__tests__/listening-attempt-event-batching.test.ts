/**
 * The high-volume Listening attempt events (answer changes, highlights, buffering, reading time)
 * are queued and sent together; everything that changes server-side state is still sent at once.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

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

const SINGLE = '/v1/listening-papers/attempts/lat-1/integrity-events';
const BATCH = `${SINGLE}/batch`;

function noContent() {
  return new Response(null, { status: 204 });
}

function jsonResponse(body: unknown, status: number) {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

function urls(): string[] {
  return mockFetchWithTimeout.mock.calls.map(([url]) => String(url));
}

function bodyOf(call: unknown[]): { events?: Array<{ eventType: string; details?: string; occurredAt: string }> } & Record<string, unknown> {
  return JSON.parse(String((call[1] as RequestInit).body));
}

async function freshModule() {
  vi.resetModules();
  return import('../listening-api');
}

describe('Listening attempt event batching', () => {
  beforeEach(() => {
    mockEnsureFreshAccessToken.mockResolvedValue('access-token');
    mockFetchWithTimeout.mockReset();
    mockFetchWithTimeout.mockImplementation(async () => noContent());
    Object.defineProperty(document, 'cookie', { configurable: true, get: () => 'oet_csrf=csrf-token' });
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('sends the queued attempt events together in one request, in order, each with its own timestamp', async () => {
    const { recordListeningIntegrityEvent, flushListeningAttemptEvents } = await freshModule();

    await recordListeningIntegrityEvent('lat-1', 'answer_changed', '{"questionId":"q1"}', '2026-10-05T10:00:01.000Z');
    await recordListeningIntegrityEvent('lat-1', 'highlight', undefined, '2026-10-05T10:00:02.000Z');
    await recordListeningIntegrityEvent('lat-1', 'answer_changed', '{"questionId":"q2"}', '2026-10-05T10:00:03.000Z');
    expect(mockFetchWithTimeout).not.toHaveBeenCalled();

    await flushListeningAttemptEvents();

    expect(urls()).toEqual([BATCH]);
    const call = mockFetchWithTimeout.mock.calls[0];
    expect((call[1] as RequestInit).method).toBe('POST');
    expect(bodyOf(call).events).toEqual([
      { eventType: 'answer_changed', details: '{"questionId":"q1"}', occurredAt: '2026-10-05T10:00:01.000Z' },
      { eventType: 'highlight', occurredAt: '2026-10-05T10:00:02.000Z' },
      { eventType: 'answer_changed', details: '{"questionId":"q2"}', occurredAt: '2026-10-05T10:00:03.000Z' },
    ]);
  });

  it('uses the single-event route for a lone queued event', async () => {
    const { recordListeningIntegrityEvent, flushListeningAttemptEvents } = await freshModule();

    await recordListeningIntegrityEvent('lat-1', 'strikethrough', undefined, '2026-10-05T10:00:01.000Z');
    await flushListeningAttemptEvents();

    expect(urls()).toEqual([SINGLE]);
    expect(bodyOf(mockFetchWithTimeout.mock.calls[0])).toEqual({ eventType: 'strikethrough', occurredAt: '2026-10-05T10:00:01.000Z' });
  });

  it('keeps each attempt in its own request', async () => {
    const { recordListeningIntegrityEvent, flushListeningAttemptEvents } = await freshModule();

    await recordListeningIntegrityEvent('lat-1', 'answer_changed');
    await recordListeningIntegrityEvent('lat-2', 'answer_changed');
    await recordListeningIntegrityEvent('lat-1', 'highlight');
    await recordListeningIntegrityEvent('lat-2', 'highlight');
    await flushListeningAttemptEvents();

    expect(urls().sort()).toEqual([
      '/v1/listening-papers/attempts/lat-1/integrity-events/batch',
      '/v1/listening-papers/attempts/lat-2/integrity-events/batch',
    ]);
  });

  it.each([
    'window_blur',
    'window_focus',
    'page_hidden',
    'fullscreen_exit',
    'audio_seek_blocked',
    'audio_error',
    'audio_started',
    'audio_progress',
    'audio_ended',
    'audio_stopped',
    'section_transition',
    'auto_submit',
  ])('still sends %s at once and on its own, exactly as before', async (eventType) => {
    const { recordListeningIntegrityEvent, flushListeningAttemptEvents } = await freshModule();

    await recordListeningIntegrityEvent('lat-1', eventType, undefined, '2026-10-05T10:00:01.000Z');

    expect(urls()).toEqual([SINGLE]);
    expect(bodyOf(mockFetchWithTimeout.mock.calls[0])).toEqual({ eventType, occurredAt: '2026-10-05T10:00:01.000Z' });
    await flushListeningAttemptEvents();
    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(1);
  });

  it('does not retry a failed batch: a dropped event is fine, a retry storm is not', async () => {
    mockFetchWithTimeout.mockImplementation(async () => jsonResponse({ code: 'internal_server_error' }, 500));
    const { recordListeningIntegrityEvent, flushListeningAttemptEvents } = await freshModule();

    await recordListeningIntegrityEvent('lat-1', 'answer_changed');
    await recordListeningIntegrityEvent('lat-1', 'highlight');
    await expect(flushListeningAttemptEvents()).resolves.toBeUndefined();

    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(1);
  });

  it('falls back to single events when the API has no batch route, and stops trying it', async () => {
    mockFetchWithTimeout.mockImplementation(async (url: string) => (
      String(url).endsWith('/batch') ? new Response('', { status: 404 }) : noContent()
    ));
    const { recordListeningIntegrityEvent, flushListeningAttemptEvents } = await freshModule();

    await recordListeningIntegrityEvent('lat-1', 'answer_changed');
    await recordListeningIntegrityEvent('lat-1', 'highlight');
    await flushListeningAttemptEvents();

    expect(urls()).toEqual([BATCH, SINGLE, SINGLE]);

    await recordListeningIntegrityEvent('lat-1', 'answer_changed');
    await recordListeningIntegrityEvent('lat-1', 'highlight');
    await flushListeningAttemptEvents();

    expect(urls().slice(3)).toEqual([SINGLE, SINGLE]);
  });

  it('does not mistake "attempt not found" on the batch route for a missing route', async () => {
    mockFetchWithTimeout.mockImplementation(async () => jsonResponse({ code: 'listening_attempt_not_found', message: 'Listening attempt not found.' }, 404));
    const { recordListeningIntegrityEvent, flushListeningAttemptEvents } = await freshModule();

    await recordListeningIntegrityEvent('lat-1', 'answer_changed');
    await recordListeningIntegrityEvent('lat-1', 'highlight');
    await flushListeningAttemptEvents();
    expect(urls()).toEqual([BATCH]);

    await recordListeningIntegrityEvent('lat-1', 'answer_changed');
    await recordListeningIntegrityEvent('lat-1', 'highlight');
    await flushListeningAttemptEvents();
    expect(urls()).toEqual([BATCH, BATCH]);
  });

  it('flushes with keepalive when the page is hidden, so queued events survive the tab going away', async () => {
    const { recordListeningIntegrityEvent } = await freshModule();

    await recordListeningIntegrityEvent('lat-1', 'answer_changed');
    await recordListeningIntegrityEvent('lat-1', 'highlight');
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'hidden' });
    document.dispatchEvent(new Event('visibilitychange'));

    await vi.waitFor(() => expect(mockFetchWithTimeout).toHaveBeenCalledTimes(1));
    expect(urls()).toEqual([BATCH]);
    expect((mockFetchWithTimeout.mock.calls[0][1] as RequestInit).keepalive).toBe(true);
  });
});
