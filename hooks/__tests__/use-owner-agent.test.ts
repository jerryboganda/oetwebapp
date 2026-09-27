import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const api = vi.hoisted(() => ({
  getMe: vi.fn(),
  getStatus: vi.fn(),
  listSessions: vi.fn(),
  postLease: vi.fn(),
  getSession: vi.fn(),
  decideApproval: vi.fn(),
  unlock: vi.fn(),
  lockNow: vi.fn(),
}));

const streamState = vi.hoisted(() => ({
  opened: [] as Array<{ sessionId: string; afterSeq?: number; onEvent: (e: unknown) => void }>,
  close: vi.fn(async () => undefined),
}));

vi.mock('@/lib/owner-agent/api', () => ({
  ...api,
  describeOwnerAgentError: (_e: unknown, fallback: string) => fallback,
}));

vi.mock('@/lib/owner-agent/signalr', () => ({
  openOwnerAgentEventStream: (options: { sessionId: string; afterSeq?: number; onEvent: (e: unknown) => void }) => {
    streamState.opened.push(options);
    return { sessionId: options.sessionId, lastSeq: () => 0, close: streamState.close };
  },
}));

import {
  OWNER_AGENT_LEASE_INTERVAL_MS,
  useOwnerAgent,
  useOwnerAgentHistory,
  useOwnerAgentSession,
  type OwnerAgentHistoryFilters,
} from '../use-owner-agent';
import { clearUnlock, getUnlockKey, isUnlocked, resetUnlockStoreForTests, setUnlocked } from '@/lib/owner-agent/unlock-store';

const SESSION = '01J9ZQ3V4W5X6Y7Z8A9B0C1D2E';

async function flush(ms = 0) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

function meUnlocked(minutesLeft: number) {
  const expiresAt = new Date(Date.now() + minutesLeft * 60_000).toISOString();
  return { isOwner: true, unlocked: true, unlockExpiresAt: expiresAt, absoluteExpiresAt: expiresAt, featureEnabled: true };
}

describe('useOwnerAgent', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    for (const fn of Object.values(api)) fn.mockReset();
    streamState.opened.length = 0;
    streamState.close.mockClear();
    resetUnlockStoreForTests();
    api.getMe.mockResolvedValue({ isOwner: true, unlocked: false, featureEnabled: true });
    api.getStatus.mockResolvedValue(null);
    api.listSessions.mockResolvedValue([]);
    api.postLease.mockImplementation(async (expiresAt: string) => ({ expiresAt }));
  });

  afterEach(() => {
    resetUnlockStoreForTests();
    vi.useRealTimers();
  });

  it('stays locked while /me says locked and sends no lease', async () => {
    const { result, unmount } = renderHook(() => useOwnerAgent());
    await flush();
    expect(result.current.isOwner).toBe(true);
    expect(result.current.unlocked).toBe(false);
    expect(api.postLease).not.toHaveBeenCalled();
    expect(api.getStatus).not.toHaveBeenCalled();
    unmount();
  });

  it('comes back unlocked from /me alone (reload or new tab with the unlock cookie)', async () => {
    const me = meUnlocked(42);
    api.getMe.mockResolvedValue(me);
    const { result, unmount } = renderHook(() => useOwnerAgent({ statusPollMs: 600_000, sessionsPollMs: 600_000 }));
    await flush();
    expect(result.current.unlocked).toBe(true);
    expect(result.current.unlock.expiresAt).toBe(me.unlockExpiresAt);
    expect(api.getStatus).toHaveBeenCalled();
    expect(api.listSessions).toHaveBeenCalledWith({ includeArchived: false });
    unmount();
  });

  it('re-reads /me after unlocking, and Lock now posts /lock and locks', async () => {
    const me = meUnlocked(60);
    api.unlock.mockImplementation(async () => {
      setUnlocked({ expiresAt: me.unlockExpiresAt });
      return { expiresAt: me.unlockExpiresAt };
    });
    const { result, unmount } = renderHook(() => useOwnerAgent({ statusPollMs: 600_000, sessionsPollMs: 600_000 }));
    await flush();
    expect(api.getMe).toHaveBeenCalledTimes(1);

    api.getMe.mockResolvedValue(me);
    await act(async () => {
      await result.current.unlockConsole('fixture-password', '123456');
    });
    expect(api.unlock).toHaveBeenCalledWith({ password: 'fixture-password', code: '123456' });
    expect(api.getMe).toHaveBeenCalledTimes(2);
    expect(result.current.unlocked).toBe(true);

    api.lockNow.mockImplementation(async () => {
      clearUnlock('locked');
    });
    await act(async () => {
      await result.current.lockConsole();
    });
    expect(api.lockNow).toHaveBeenCalledTimes(1);
    expect(isUnlocked()).toBe(false);
    expect(result.current.unlocked).toBe(false);
    expect(result.current.unlock.clearedReason).toBe('locked');
    unmount();
  });

  it('flips to the unlock screen when the unlock expires', async () => {
    api.getMe.mockResolvedValue(meUnlocked(5));
    const { result, unmount } = renderHook(() => useOwnerAgent({ statusPollMs: 600_000, sessionsPollMs: 600_000 }));
    await flush();
    expect(result.current.unlocked).toBe(true);

    await flush(5 * 60_000);
    expect(result.current.unlocked).toBe(false);
    expect(result.current.unlock.clearedReason).toBe('expired');
    unmount();
  });

  it('sends the lease heartbeat every 60 s while mounted and unlocked, and stops on unmount', async () => {
    api.getMe.mockResolvedValue(meUnlocked(60));
    const { result, unmount } = renderHook(() => useOwnerAgent({ statusPollMs: 600_000, sessionsPollMs: 600_000 }));
    await flush();
    expect(result.current.unlocked).toBe(true);
    expect(api.postLease).toHaveBeenCalledTimes(1);
    const requested = Date.parse(api.postLease.mock.calls[0][0] as string) - Date.now();
    expect(requested).toBeGreaterThan(2 * 60_000);
    expect(requested).toBeLessThanOrEqual(3 * 60_000);

    await flush(OWNER_AGENT_LEASE_INTERVAL_MS);
    expect(api.postLease).toHaveBeenCalledTimes(2);

    unmount();
    await flush(OWNER_AGENT_LEASE_INTERVAL_MS * 3);
    expect(api.postLease).toHaveBeenCalledTimes(2);
  });
});

describe('useOwnerAgentSession', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    for (const fn of Object.values(api)) fn.mockReset();
    streamState.opened.length = 0;
    streamState.close.mockClear();
    api.getSession.mockResolvedValue({ id: SESSION, status: 'idle', pendingApprovals: [], tainted: false });
    api.decideApproval.mockResolvedValue({ ok: true });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('opens the stream from seq 0, batches events into the render model and closes on unmount', async () => {
    const { result, unmount } = renderHook(() => useOwnerAgentSession(SESSION, { enabled: true }));
    await flush();
    expect(streamState.opened).toHaveLength(1);
    expect(streamState.opened[0]).toMatchObject({ sessionId: SESSION, afterSeq: 0 });

    const onEvent = streamState.opened[0].onEvent;
    act(() => {
      onEvent({ seq: 1, sessionId: SESSION, turnId: 't', ts: '2026-09-27T10:00:00Z', type: 'turn_started', data: { model: 'm', mode: 'guarded' } });
      onEvent({ seq: 2, sessionId: SESSION, turnId: 't', ts: '2026-09-27T10:00:01Z', type: 'text_delta', data: { messageId: 'x', text: 'hi' } });
      onEvent({ seq: 2, sessionId: SESSION, turnId: 't', ts: '2026-09-27T10:00:01Z', type: 'text_delta', data: { messageId: 'x', text: 'hi' } });
    });
    await flush(20);

    expect(result.current.model.lastSeq).toBe(2);
    expect(result.current.running).toBe(true);
    const assistant = result.current.model.items.find((item) => item.kind === 'assistant');
    expect(assistant && assistant.kind === 'assistant' ? assistant.text : null).toBe('hi');

    unmount();
    expect(streamState.close).toHaveBeenCalled();
  });

  it('decides approvals with the approval nonce', async () => {
    const { result } = renderHook(() => useOwnerAgentSession(SESSION, { enabled: true }));
    await flush();
    await act(async () => {
      await result.current.decide(
        {
          approvalId: 'ap-1',
          nonce: 'nonce-1',
          toolCallId: 'tc',
          summary: 's',
          uid: 10002,
          reasons: [],
          tainted: false,
          expiresAt: '2026-09-27T10:10:00Z',
        },
        'deny',
        'no',
      );
    });
    expect(api.decideApproval).toHaveBeenCalledWith(SESSION, 'ap-1', { decision: 'deny', nonce: 'nonce-1', note: 'no' });
  });

  it('clears the unlock when the hub rejects exactly the unlock it started under', async () => {
    resetUnlockStoreForTests();
    setUnlocked({ expiresAt: new Date(Date.now() + 60 * 60_000).toISOString() });
    const current = getUnlockKey();
    const { unmount } = renderHook(() => useOwnerAgentSession(SESSION, { enabled: true }));
    await flush();
    const options = streamState.opened[0] as unknown as {
      getUnlockKey?: () => string | null;
      onUnlockRejected?: (key: string | null) => void;
    };
    expect(options.getUnlockKey?.()).toBe(current);

    // A rejection for an older unlock must not lock out a newer one.
    act(() => options.onUnlockRejected?.('unlock-older'));
    expect(isUnlocked()).toBe(true);

    act(() => options.onUnlockRejected?.(current));
    expect(isUnlocked()).toBe(false);

    unmount();
    resetUnlockStoreForTests();
  });

  it('does not stream while disabled', async () => {
    renderHook(() => useOwnerAgentSession(SESSION, { enabled: false }));
    await flush();
    expect(streamState.opened).toHaveLength(0);
    expect(api.getSession).not.toHaveBeenCalled();
  });
});

describe('useOwnerAgentHistory', () => {
  const FILTERS: OwnerAgentHistoryFilters = { q: '', engine: '', status: '', includeArchived: true };
  const row = (n: number) => ({
    id: `01J9ZQ3V4W5X6Y7Z8A9B0C1D${String(n).padStart(2, '0')}`,
    title: `Session ${n}`,
    updatedAt: new Date(Date.UTC(2026, 8, 27, 10, 0, 0) - n * 60_000).toISOString(),
  });

  beforeEach(() => {
    vi.useFakeTimers();
    for (const fn of Object.values(api)) fn.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('loads the first page with the filters and pages with before = last updatedAt', async () => {
    api.listSessions
      .mockResolvedValueOnce([row(1), row(2)])
      .mockResolvedValueOnce([row(2), row(3)])
      .mockResolvedValueOnce([row(4)]);
    const { result } = renderHook(() => useOwnerAgentHistory(FILTERS, { enabled: true, pageSize: 2 }));
    await flush();
    expect(api.listSessions).toHaveBeenLastCalledWith({ includeArchived: true, limit: 2 });
    expect(result.current.rows.map((r) => r.title)).toEqual(['Session 1', 'Session 2']);
    expect(result.current.hasMore).toBe(true);

    await act(async () => {
      await result.current.loadMore();
    });
    expect(api.listSessions).toHaveBeenLastCalledWith({ includeArchived: true, limit: 2, before: row(2).updatedAt });
    // A row repeated across pages is not duplicated.
    expect(result.current.rows.map((r) => r.title)).toEqual(['Session 1', 'Session 2', 'Session 3']);
    expect(result.current.hasMore).toBe(true);

    await act(async () => {
      await result.current.loadMore();
    });
    expect(api.listSessions).toHaveBeenLastCalledWith({ includeArchived: true, limit: 2, before: row(3).updatedAt });
    expect(result.current.rows).toHaveLength(4);
    expect(result.current.hasMore).toBe(false);
  });

  it('reloads from the first page when the filters change', async () => {
    api.listSessions.mockResolvedValue([row(1)]);
    const { rerender } = renderHook(
      (filters: OwnerAgentHistoryFilters) => useOwnerAgentHistory(filters, { enabled: true }),
      { initialProps: FILTERS },
    );
    await flush();
    rerender({ q: ' T3 ', engine: 'codex', status: 'archived', includeArchived: false });
    await flush();
    expect(api.listSessions).toHaveBeenLastCalledWith({ q: 'T3', engine: 'codex', status: 'archived', includeArchived: false, limit: 50 });
  });

  it('does not load while disabled', async () => {
    renderHook(() => useOwnerAgentHistory(FILTERS, { enabled: false }));
    await flush();
    expect(api.listSessions).not.toHaveBeenCalled();
  });
});
