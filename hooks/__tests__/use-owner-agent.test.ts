import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const api = vi.hoisted(() => ({
  getMe: vi.fn(),
  getStatus: vi.fn(),
  listSessions: vi.fn(),
  postLease: vi.fn(),
  getSession: vi.fn(),
  decideApproval: vi.fn(),
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

import { OWNER_AGENT_LEASE_INTERVAL_MS, useOwnerAgent, useOwnerAgentSession } from '../use-owner-agent';
import { getUnlockTicket, resetUnlockStoreForTests, setUnlock } from '@/lib/owner-agent/unlock-store';

const SESSION = '01J9ZQ3V4W5X6Y7Z8A9B0C1D2E';

async function flush(ms = 0) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

describe('useOwnerAgent', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    for (const fn of Object.values(api)) fn.mockReset();
    streamState.opened.length = 0;
    streamState.close.mockClear();
    resetUnlockStoreForTests();
    api.getMe.mockResolvedValue({ isOwner: true, unlocked: true, featureEnabled: true });
    api.getStatus.mockResolvedValue(null);
    api.listSessions.mockResolvedValue([]);
    api.postLease.mockImplementation(async (expiresAt: string) => ({ expiresAt }));
  });

  afterEach(() => {
    resetUnlockStoreForTests();
    vi.useRealTimers();
  });

  it('stays locked without a ticket and sends no lease', async () => {
    api.getMe.mockResolvedValue({ isOwner: true, unlocked: false, featureEnabled: true });
    const { result, unmount } = renderHook(() => useOwnerAgent());
    await flush();
    expect(result.current.isOwner).toBe(true);
    expect(result.current.unlocked).toBe(false);
    expect(api.postLease).not.toHaveBeenCalled();
    expect(api.getStatus).not.toHaveBeenCalled();
    unmount();
  });

  it('sends the lease heartbeat every 60 s while mounted and unlocked, and stops on unmount', async () => {
    setUnlock({
      ticket: 'ticket-fixture',
      expiresAt: new Date(Date.now() + 45 * 60_000).toISOString(),
      absoluteExpiresAt: new Date(Date.now() + 8 * 60 * 60_000).toISOString(),
    });
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

  it('drops the unlock ticket when the hub rejects exactly the ticket it was given', async () => {
    resetUnlockStoreForTests();
    setUnlock({
      ticket: 'ticket-hub',
      expiresAt: new Date(Date.now() + 45 * 60_000).toISOString(),
      absoluteExpiresAt: new Date(Date.now() + 8 * 60 * 60_000).toISOString(),
    });
    const { unmount } = renderHook(() => useOwnerAgentSession(SESSION, { enabled: true }));
    await flush();
    const options = streamState.opened[0] as unknown as { onUnlockRejected?: (ticket: string | null) => void };

    // A rejection for an older ticket must not lock out a newer unlock.
    act(() => options.onUnlockRejected?.('ticket-old'));
    expect(getUnlockTicket()).toBe('ticket-hub');

    act(() => options.onUnlockRejected?.('ticket-hub'));
    expect(getUnlockTicket()).toBeNull();

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
