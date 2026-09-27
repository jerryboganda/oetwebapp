import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { mockRequest } = vi.hoisted(() => ({ mockRequest: vi.fn() }));

vi.mock('@/lib/api', () => ({
  apiClient: { request: (...args: unknown[]) => mockRequest(...args) },
}));

import { lock, refreshUnlock, resetOwnerAgentApiForTests, unlock } from '../api';
import {
  clearUnlock,
  getUnlockSnapshot,
  getUnlockTicket,
  isSnapshotUnlocked,
  resetUnlockStoreForTests,
  setUnlock,
  subscribeUnlock,
} from '../unlock-store';

const NOW = Date.UTC(2026, 8, 27, 9, 0, 0);
const minutes = (n: number) => n * 60_000;
const iso = (ms: number) => new Date(ms).toISOString();

function headersOf(callIndex: number): Record<string, string> {
  const init = mockRequest.mock.calls[callIndex]?.[1] as RequestInit | undefined;
  return (init?.headers ?? {}) as Record<string, string>;
}

describe('owner-agent unlock store', () => {
  let storageSpies: Array<ReturnType<typeof vi.spyOn>>;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    mockRequest.mockReset();
    resetUnlockStoreForTests();
    resetOwnerAgentApiForTests();
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
    resetOwnerAgentApiForTests();
    resetUnlockStoreForTests();
    vi.useRealTimers();
  });

  it('holds the ticket in memory only and never touches web storage or cookies', async () => {
    mockRequest.mockResolvedValueOnce({ ticket: 'ticket-A', expiresAt: iso(NOW + minutes(45)), absoluteExpiresAt: iso(NOW + minutes(480)) });

    await unlock({ password: 'fixture-password', code: '123456' });

    expect(getUnlockTicket()).toBe('ticket-A');
    expect(mockRequest).toHaveBeenCalledWith(
      '/v1/owner-agent/unlock',
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ password: 'fixture-password', code: '123456' }) }),
      expect.objectContaining({ maxRetries: 0 }),
    );
    // afterEach asserts no Storage/cookie API was touched and both stores are empty.
  });

  it('reports an expired ticket as locked even before the timer fires', () => {
    setUnlock({ ticket: 'ticket-B', expiresAt: iso(NOW + 1_000), absoluteExpiresAt: iso(NOW + minutes(480)) });
    expect(getUnlockTicket(NOW)).toBe('ticket-B');
    expect(getUnlockTicket(NOW + 1_000)).toBeNull();
    expect(isSnapshotUnlocked(getUnlockSnapshot(), NOW + 2_000)).toBe(false);
  });

  it('notifies subscribers on set and clear, and clearing twice is a no-op', () => {
    const listener = vi.fn();
    const unsubscribe = subscribeUnlock(listener);
    setUnlock({ ticket: 'ticket-C', expiresAt: iso(NOW + minutes(45)), absoluteExpiresAt: iso(NOW + minutes(480)) });
    clearUnlock('locked');
    clearUnlock('locked');
    expect(listener).toHaveBeenCalledTimes(2);
    expect(getUnlockSnapshot().clearedReason).toBe('locked');
    unsubscribe();
  });

  it('re-mints the ticket before the sliding expiry, sending the current ticket', async () => {
    mockRequest.mockResolvedValueOnce({ ticket: 'ticket-1', expiresAt: iso(NOW + minutes(45)), absoluteExpiresAt: iso(NOW + minutes(480)) });
    await unlock({ password: 'fixture-password', code: '123456' });

    mockRequest.mockResolvedValueOnce({ ticket: 'ticket-2', expiresAt: iso(NOW + minutes(85)), absoluteExpiresAt: iso(NOW + minutes(480)) });
    await vi.advanceTimersByTimeAsync(minutes(40));

    expect(mockRequest).toHaveBeenCalledTimes(2);
    expect(mockRequest.mock.calls[1]?.[0]).toBe('/v1/owner-agent/unlock/refresh');
    expect(headersOf(1)['X-Owner-Agent-Unlock']).toBe('ticket-1');
    expect(getUnlockTicket()).toBe('ticket-2');
  });

  it('locks at the absolute cap instead of refreshing past it', async () => {
    mockRequest.mockResolvedValueOnce({ ticket: 'ticket-cap', expiresAt: iso(NOW + minutes(45)), absoluteExpiresAt: iso(NOW + minutes(3)) });
    await unlock({ password: 'fixture-password', code: '123456' });

    await vi.advanceTimersByTimeAsync(minutes(3));

    expect(mockRequest).toHaveBeenCalledTimes(1);
    expect(getUnlockTicket()).toBeNull();
    expect(getUnlockSnapshot().clearedReason).toBe('expired');
  });

  it('drops the ticket when the refresh is rejected', async () => {
    setUnlock({ ticket: 'ticket-old', expiresAt: iso(NOW + minutes(45)), absoluteExpiresAt: iso(NOW + minutes(480)) });
    mockRequest.mockRejectedValueOnce(Object.assign(new Error('Unlock expired'), { status: 401, code: 'owner_agent_unlock_expired' }));

    await expect(refreshUnlock()).rejects.toThrow('Unlock expired');
    expect(getUnlockTicket()).toBeNull();
  });

  it('lock() revokes server-side and forgets the ticket even if the call fails', async () => {
    setUnlock({ ticket: 'ticket-lock', expiresAt: iso(NOW + minutes(45)), absoluteExpiresAt: iso(NOW + minutes(480)) });
    mockRequest.mockRejectedValueOnce(new Error('network'));

    await expect(lock()).rejects.toThrow('network');
    expect(mockRequest.mock.calls[0]?.[0]).toBe('/v1/owner-agent/lock');
    expect(headersOf(0)['X-Owner-Agent-Unlock']).toBe('ticket-lock');
    expect(getUnlockTicket()).toBeNull();
  });
});
