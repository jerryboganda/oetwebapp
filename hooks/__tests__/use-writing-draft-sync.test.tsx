import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '@/lib/api/client';

const { getWritingDraftV2, putWritingDraftV2 } = vi.hoisted(() => ({
  getWritingDraftV2: vi.fn(),
  putWritingDraftV2: vi.fn(),
}));

vi.mock('@/lib/writing/api', () => ({ getWritingDraftV2, putWritingDraftV2 }));

import { useWritingDraftSync, type DraftSyncBaseline, type UseWritingDraftSyncOptions } from '../use-writing-draft-sync';

const SHADOW_KEY = 'oet:writing-draft:v1:u1:s1:practice';
const SYNCED: DraftSyncBaseline = { text: 'Dear Dr Green,', wordCount: 3, serverText: 'Dear Dr Green,', version: 3 };

function saved(version: number) {
  return { content: '', wordCount: 0, version };
}

function setup(options: Partial<UseWritingDraftSyncOptions> = {}) {
  const props: UseWritingDraftSyncOptions = {
    scenarioId: 's1',
    mode: 'practice',
    userId: 'u1',
    baseline: SYNCED,
    ...options,
  };
  return renderHook((p: UseWritingDraftSyncOptions) => useWritingDraftSync(p), { initialProps: props });
}

async function tick(ms = 0) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

function setOnline(online: boolean) {
  Object.defineProperty(window.navigator, 'onLine', { configurable: true, get: () => online });
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.clearAllMocks();
  localStorage.clear();
  setOnline(true);
  putWritingDraftV2.mockResolvedValue(saved(4));
});

afterEach(() => {
  vi.useRealTimers();
});

describe('useWritingDraftSync', () => {
  it('copies every change to this device synchronously and saves after 1.2 s of quiet', async () => {
    const { result } = setup();

    act(() => result.current.update('Dear Dr Green, a', 4));
    expect(JSON.parse(localStorage.getItem(SHADOW_KEY) ?? '{}')).toMatchObject({ text: 'Dear Dr Green, a', baseVersion: 3 });
    expect(result.current.state).toBe('saving');

    await tick(1_100);
    expect(putWritingDraftV2).not.toHaveBeenCalled();
    await tick(100);
    expect(putWritingDraftV2).toHaveBeenCalledWith(
      's1', 'practice', expect.objectContaining({ content: 'Dear Dr Green, a', expectedVersion: 3 }), undefined,
    );
    await tick();
    expect(result.current.state).toBe('saved');
    // Synced: no plaintext left at rest on the device.
    expect(JSON.parse(localStorage.getItem(SHADOW_KEY) ?? '{}')).not.toHaveProperty('text');
  });

  it('saves within 5 s while the learner keeps typing (max wait)', async () => {
    const { result } = setup();
    for (let i = 0; i < 5; i += 1) {
      act(() => result.current.update(`Dear Dr Green, ${'x'.repeat(i + 1)}`, 4));
      await tick(1_000);
    }
    expect(putWritingDraftV2).toHaveBeenCalledTimes(1);
  });

  it('keeps one save in flight and follows up with the latest text on the next version', async () => {
    let finish: (value: unknown) => void = () => {};
    putWritingDraftV2.mockImplementationOnce(() => new Promise((resolve) => { finish = resolve; }));
    const { result } = setup();

    act(() => result.current.update('one', 1));
    act(() => result.current.flush());
    act(() => result.current.update('one two', 2));
    act(() => result.current.flush());
    expect(putWritingDraftV2).toHaveBeenCalledTimes(1);

    await act(async () => {
      finish(saved(4));
    });
    await tick();
    expect(putWritingDraftV2).toHaveBeenCalledTimes(2);
    expect(putWritingDraftV2.mock.calls[1][2]).toMatchObject({ content: 'one two', expectedVersion: 4 });
  });

  it('flushes with keepalive when the page is hidden, closed or unmounted', async () => {
    const { unmount } = setup();

    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'hidden' });
    act(() => {
      document.dispatchEvent(new Event('visibilitychange'));
    });
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' });
    await tick();
    act(() => {
      window.dispatchEvent(new Event('pagehide'));
    });
    await tick();
    unmount();

    expect(putWritingDraftV2).toHaveBeenCalledTimes(3);
    for (const call of putWritingDraftV2.mock.calls) expect(call[3]).toEqual({ keepalive: true });
  });

  it('holds the text on this device while offline and saves the moment the connection returns', async () => {
    const { result } = setup();
    setOnline(false);
    act(() => {
      window.dispatchEvent(new Event('offline'));
    });
    expect(result.current.state).toBe('offline');

    act(() => result.current.update('Typed offline', 2));
    await tick(10_000);
    expect(putWritingDraftV2).not.toHaveBeenCalled();
    expect(result.current.state).toBe('pending-local');
    expect(result.current.online).toBe(false);

    setOnline(true);
    act(() => {
      window.dispatchEvent(new Event('online'));
    });
    expect(putWritingDraftV2).toHaveBeenCalledWith('s1', 'practice', expect.objectContaining({ content: 'Typed offline' }), undefined);
    await tick();
    expect(result.current.state).toBe('saved');
  });

  it('backs off 2 s then 4 s after server errors, without hammering while the learner types', async () => {
    putWritingDraftV2
      .mockRejectedValueOnce(new ApiError(503, 'unavailable', 'down', true))
      .mockRejectedValueOnce(new ApiError(503, 'unavailable', 'down', true));
    const { result } = setup();

    act(() => result.current.flush());
    await tick();
    expect(result.current.state).toBe('pending-local');
    act(() => result.current.update('more text', 2));
    await tick(1_900);
    expect(putWritingDraftV2).toHaveBeenCalledTimes(1);
    await tick(100);
    expect(putWritingDraftV2).toHaveBeenCalledTimes(2);
    expect(putWritingDraftV2.mock.calls[1][2]).toMatchObject({ content: 'more text' });
    await tick(3_900);
    expect(putWritingDraftV2).toHaveBeenCalledTimes(2);
    await tick(100);
    expect(putWritingDraftV2).toHaveBeenCalledTimes(3);
    await tick();
    expect(result.current.state).toBe('saved');
  });

  it('treats a 409 as saved when the server already holds the same text', async () => {
    putWritingDraftV2.mockRejectedValueOnce(new ApiError(409, 'draft_version_conflict', 'conflict', false));
    getWritingDraftV2.mockResolvedValueOnce({ content: 'same text', wordCount: 2, version: 9 });
    const { result } = setup();

    act(() => result.current.update('same text', 2));
    await tick(1_200);
    await tick();
    expect(result.current.conflict).toBeNull();
    expect(result.current.state).toBe('saved');

    act(() => result.current.update('same text more', 3));
    await tick(1_200);
    expect(putWritingDraftV2.mock.calls[1][2]).toMatchObject({ content: 'same text more', expectedVersion: 9 });
  });

  it('raises a conflict for different text, stops saving, and "keep this version" overwrites on the server version', async () => {
    putWritingDraftV2.mockRejectedValueOnce(new ApiError(409, 'draft_version_conflict', 'conflict', false));
    getWritingDraftV2.mockResolvedValueOnce({ content: 'other device', wordCount: 2, version: 9 });
    const { result } = setup();

    act(() => result.current.update('this device', 2));
    await tick(1_200);
    await tick();
    expect(result.current.conflict).toEqual({ serverText: 'other device' });
    expect(result.current.state).toBe('error');

    act(() => result.current.update('this device!', 2));
    await tick(30_000);
    expect(putWritingDraftV2).toHaveBeenCalledTimes(1);

    act(() => result.current.keepLocal());
    expect(putWritingDraftV2.mock.calls[1][2]).toMatchObject({ content: 'this device!', expectedVersion: 9 });
  });

  it('never re-sends a stale version after a 409 whose re-read failed: it re-reads first, then saves on the new version', async () => {
    // Production 3 Oct 2026: 409 "This draft was changed elsewhere" every ~10 s for
    // minutes — the heartbeat kept PUTting the same stale expectedVersion.
    putWritingDraftV2.mockRejectedValueOnce(new ApiError(409, 'draft_version_conflict', 'conflict', false));
    getWritingDraftV2
      .mockRejectedValueOnce(new ApiError(429, 'rate_limited', 'slow down', true))
      .mockResolvedValueOnce({ content: 'Dear Dr Green, typed', wordCount: 4, version: 7 });
    const { result } = setup({ heartbeatMs: 10_000 });

    act(() => result.current.update('Dear Dr Green, typed', 4));
    await tick(1_200);
    await tick();
    expect(putWritingDraftV2).toHaveBeenCalledTimes(1);
    expect(getWritingDraftV2).toHaveBeenCalledTimes(1);

    // Backoff and heartbeats: the next request is the GET, never the stale PUT.
    await tick(2_000);
    expect(getWritingDraftV2).toHaveBeenCalledTimes(2);
    expect(putWritingDraftV2).toHaveBeenCalledTimes(1);
    expect(result.current.conflict).toBeNull();
    expect(result.current.state).toBe('saved');

    await tick(10_000);
    for (const call of putWritingDraftV2.mock.calls.slice(1)) {
      expect(call[2]).toMatchObject({ content: 'Dear Dr Green, typed', expectedVersion: 7 });
    }
    expect(putWritingDraftV2.mock.calls.length).toBeGreaterThan(1);
  });

  it('a 409 for different text stops the heartbeat and every background save until the learner chooses', async () => {
    putWritingDraftV2.mockRejectedValueOnce(new ApiError(409, 'draft_version_conflict', 'conflict', false));
    getWritingDraftV2.mockResolvedValueOnce({ content: 'other device', wordCount: 2, version: 9 });
    const { result } = setup({ heartbeatMs: 10_000 });

    act(() => result.current.update('this device', 2));
    await tick(1_200);
    await tick();
    expect(result.current.conflict).toEqual({ serverText: 'other device' });

    act(() => result.current.update('this device, more', 3));
    act(() => result.current.flush());
    await tick(60_000);
    expect(putWritingDraftV2).toHaveBeenCalledTimes(1);
    expect(getWritingDraftV2).toHaveBeenCalledTimes(1);
    expect(JSON.parse(localStorage.getItem(SHADOW_KEY) ?? '{}')).toMatchObject({ text: 'this device, more' });
  });

  it('heartbeats the remaining time even without edits', async () => {
    const getClock = () => ({
      phase: 'writing' as const,
      readingSecondsRemaining: 0,
      writingSecondsRemaining: 1500,
      timeSpentSeconds: 900,
    });
    setup({ getClock, heartbeatMs: 10_000 });

    await tick(9_999);
    expect(putWritingDraftV2).not.toHaveBeenCalled();
    await tick(1);
    expect(putWritingDraftV2).toHaveBeenCalledWith(
      's1', 'practice',
      expect.objectContaining({ content: 'Dear Dr Green,', phase: 'writing', writingSecondsRemaining: 1500, timeSpentSeconds: 900 }),
      undefined,
    );
  });

  it('creates the row at once for a new attempt and prompts before unload only while unsynced', async () => {
    const { result } = setup({ baseline: { text: '', wordCount: 0, serverText: null, version: 0 } });
    expect(putWritingDraftV2).toHaveBeenCalledWith('s1', 'practice', expect.objectContaining({ content: '', expectedVersion: 0 }), undefined);
    await tick();

    const synced = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(synced);
    expect(synced.defaultPrevented).toBe(false);

    act(() => result.current.update('unsaved', 1));
    const unsynced = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(unsynced);
    expect(unsynced.defaultPrevented).toBe(true);
  });

  it('discard drops the device copy and stops saving', async () => {
    const { result } = setup();
    act(() => result.current.update('submitted letter', 2));
    act(() => result.current.discard());
    await tick(10_000);
    expect(localStorage.getItem(SHADOW_KEY)).toBeNull();
    expect(putWritingDraftV2).not.toHaveBeenCalled();
  });
});
