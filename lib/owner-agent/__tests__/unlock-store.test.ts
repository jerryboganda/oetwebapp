import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { mockRequest } = vi.hoisted(() => ({ mockRequest: vi.fn() }));

vi.mock('@/lib/api', () => ({
  apiClient: { request: (...args: unknown[]) => mockRequest(...args) },
}));

import { lockNow, unlock } from '../api';
import * as api from '../api';
import {
  applyMeUnlock,
  clearUnlock,
  getUnlockKey,
  getUnlockSnapshot,
  isSnapshotUnlocked,
  isUnlocked,
  resetUnlockStoreForTests,
  setUnlocked,
  subscribeUnlock,
} from '../unlock-store';

const NOW = Date.UTC(2026, 8, 27, 9, 0, 0);
const minutes = (n: number) => n * 60_000;
const iso = (ms: number) => new Date(ms).toISOString();

function initOf(callIndex: number): RequestInit {
  return (mockRequest.mock.calls[callIndex]?.[1] ?? {}) as RequestInit;
}

describe('owner-agent unlock store', () => {
  let storageSpies: Array<ReturnType<typeof vi.spyOn>>;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    mockRequest.mockReset();
    resetUnlockStoreForTests();
    window.localStorage.clear();
    window.sessionStorage.clear();
    storageSpies = [
      vi.spyOn(Storage.prototype, 'setItem'),
      vi.spyOn(Storage.prototype, 'getItem'),
      vi.spyOn(Storage.prototype, 'removeItem'),
      vi.spyOn(Storage.prototype, 'key'),
      vi.spyOn(Document.prototype, 'cookie', 'set'),
    ];
  });

  afterEach(() => {
    for (const spy of storageSpies) {
      expect(spy).not.toHaveBeenCalled();
      spy.mockRestore();
    }
    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(0);
    resetUnlockStoreForTests();
    vi.useRealTimers();
  });

  it('keeps only { unlocked, expiresAt } after unlocking — never a ticket, never web storage', async () => {
    // The API may still return a ticket in the body; it must be ignored.
    mockRequest.mockResolvedValueOnce({ ticket: 'ticket-must-not-be-kept', expiresAt: iso(NOW + minutes(60)), absoluteExpiresAt: iso(NOW + minutes(60)) });

    await unlock({ password: 'fixture-password', code: '123456' });

    const snapshot = getUnlockSnapshot();
    expect(snapshot).toEqual({ unlocked: true, expiresAt: iso(NOW + minutes(60)), clearedReason: null });
    expect(JSON.stringify(snapshot)).not.toContain('ticket-must-not-be-kept');
    expect(Object.keys(snapshot).sort()).toEqual(['clearedReason', 'expiresAt', 'unlocked']);
    expect(mockRequest).toHaveBeenCalledWith(
      '/v1/owner-agent/unlock',
      expect.objectContaining({ method: 'POST', credentials: 'include', body: JSON.stringify({ password: 'fixture-password', code: '123456' }) }),
      expect.objectContaining({ maxRetries: 0 }),
    );
    // The cookie is HttpOnly and set by the server; no unlock header is ever sent.
    expect(initOf(0).headers).toBeUndefined();
    // afterEach asserts no Storage/cookie API was touched and both stores are empty.
  });

  it('takes its state from GET /me (reloads / new tabs come back unlocked)', () => {
    expect(isUnlocked()).toBe(false);

    applyMeUnlock({ isOwner: true, unlocked: true, unlockExpiresAt: iso(NOW + minutes(42)), absoluteExpiresAt: iso(NOW + minutes(42)), featureEnabled: true });
    expect(getUnlockSnapshot()).toMatchObject({ unlocked: true, expiresAt: iso(NOW + minutes(42)) });

    // /me later reports the unlock gone (locked elsewhere, revoked, expired server-side).
    applyMeUnlock({ isOwner: true, unlocked: false, featureEnabled: true });
    expect(isUnlocked()).toBe(false);
    expect(getUnlockSnapshot().clearedReason).toBe('server_locked');
  });

  it('never notifies when /me repeats the same unlock state', () => {
    const listener = vi.fn();
    subscribeUnlock(listener);
    const me = { isOwner: true, unlocked: true, unlockExpiresAt: iso(NOW + minutes(30)), absoluteExpiresAt: iso(NOW + minutes(30)) };
    applyMeUnlock(me);
    applyMeUnlock(me);
    applyMeUnlock(me);
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it('uses the earlier of expiresAt / absoluteExpiresAt', () => {
    setUnlocked({ expiresAt: iso(NOW + minutes(60)), absoluteExpiresAt: iso(NOW + minutes(10)) });
    expect(getUnlockSnapshot().expiresAt).toBe(iso(NOW + minutes(10)));
  });

  it('reports an expired unlock as locked even before the timer fires', () => {
    setUnlocked({ expiresAt: iso(NOW + 1_000) });
    expect(isSnapshotUnlocked(getUnlockSnapshot(), NOW)).toBe(true);
    expect(isSnapshotUnlocked(getUnlockSnapshot(), NOW + 1_000)).toBe(false);
    expect(getUnlockKey(NOW + 2_000)).toBeNull();
  });

  it('flips to locked exactly at expiresAt with one timer — no refresh request', async () => {
    mockRequest.mockResolvedValueOnce({ expiresAt: iso(NOW + minutes(60)), absoluteExpiresAt: iso(NOW + minutes(60)) });
    await unlock({ password: 'fixture-password', code: '123456' });

    await vi.advanceTimersByTimeAsync(minutes(60) - 1);
    expect(isUnlocked()).toBe(true);

    await vi.advanceTimersByTimeAsync(1);
    expect(isUnlocked()).toBe(false);
    expect(getUnlockSnapshot().clearedReason).toBe('expired');
    // Only the unlock call: nothing re-mints or extends the 60 minutes.
    expect(mockRequest).toHaveBeenCalledTimes(1);
  });

  it('notifies subscribers on set and clear, and clearing twice is a no-op', () => {
    const listener = vi.fn();
    const unsubscribe = subscribeUnlock(listener);
    setUnlocked({ expiresAt: iso(NOW + minutes(45)) });
    clearUnlock('locked');
    clearUnlock('locked');
    expect(listener).toHaveBeenCalledTimes(2);
    expect(getUnlockSnapshot().clearedReason).toBe('locked');
    unsubscribe();
  });

  it('gives a new unlock key after unlocking again', () => {
    setUnlocked({ expiresAt: iso(NOW + minutes(45)) });
    const first = getUnlockKey();
    clearUnlock('locked');
    expect(getUnlockKey()).toBeNull();
    setUnlocked({ expiresAt: iso(NOW + minutes(60)) });
    expect(getUnlockKey()).not.toBeNull();
    expect(getUnlockKey()).not.toBe(first);
  });

  it('lockNow() posts /lock and forgets the state even if the call fails', async () => {
    setUnlocked({ expiresAt: iso(NOW + minutes(45)) });
    mockRequest.mockRejectedValueOnce(new Error('network'));

    await expect(lockNow()).rejects.toThrow('network');
    expect(mockRequest.mock.calls[0]?.[0]).toBe('/v1/owner-agent/lock');
    expect(initOf(0)).toMatchObject({ method: 'POST', credentials: 'include' });
    expect(initOf(0).headers).toBeUndefined();
    expect(isUnlocked()).toBe(false);
    expect(getUnlockSnapshot().clearedReason).toBe('locked');
  });

  it('lockNow() still asks the server to clear the cookie when the page thinks it is locked', async () => {
    mockRequest.mockResolvedValueOnce({ ok: true });
    await lockNow();
    expect(mockRequest.mock.calls[0]?.[0]).toBe('/v1/owner-agent/lock');
  });

  it('no longer exposes the sliding refresh or step-up helpers', () => {
    const exported = api as unknown as Record<string, unknown>;
    expect(exported.refreshUnlock).toBeUndefined();
    expect(exported.scheduleUnlockRefresh).toBeUndefined();
    expect(exported.stepUp).toBeUndefined();
  });
});
