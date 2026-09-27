import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApprovalRegistry, handleProxyApproval, validateProxyBody, type ProxySessionView } from '../src/approvals.js';
import { SYSTEM_SESSION_ID } from '../src/contract.js';
import { HttpError } from '../src/errors.js';

const SESSION = '01J9ZQ4X7V3N8K2M5P6R7S8T9V';

function registry(ttlMs = 30 * 60_000, now?: () => number) {
  const events: { sessionId: string; type: string; data: Record<string, unknown> }[] = [];
  const reg = new ApprovalRegistry({
    ttlMs,
    ...(now ? { now } : {}),
    emit: (sessionId, type, data) => events.push({ sessionId, type, data }),
  });
  return { reg, events };
}

function open(reg: ApprovalRegistry, sessionId = SESSION) {
  return reg.open({ sessionId, source: 'tool', summary: 'Bash: psql -c "DROP TABLE scratch"', uid: 10002, reasons: ['destructive: DROP statement'], tainted: false, toolCallId: 'tc-1' });
}

function httpStatus(fn: () => unknown): number | null {
  try {
    fn();
    return null;
  } catch (error) {
    return error instanceof HttpError ? error.status : -1;
  }
}

afterEach(() => {
  vi.useRealTimers();
});

describe('ApprovalRegistry', () => {
  it('emits the card with a nonce and resolves exactly once (single-use nonce)', async () => {
    const { reg, events } = registry();
    const card = open(reg);
    expect(card.request.nonce.length).toBeGreaterThanOrEqual(24);
    expect(events[0]).toMatchObject({ sessionId: SESSION, type: 'approval_request', data: { approvalId: card.request.approvalId, nonce: card.request.nonce, uid: 10002 } });
    expect(reg.listPending(SESSION)).toHaveLength(1);

    expect(httpStatus(() => reg.resolveByOwner(SESSION, card.request.approvalId, 'approve', 'wrong-nonce'))).toBe(409);
    expect(reg.has(card.request.approvalId)).toBe(true);

    reg.resolveByOwner(SESSION, card.request.approvalId, 'approve', card.request.nonce, 'looks fine');
    await expect(card.result).resolves.toEqual({ decision: 'approve', by: 'owner', note: 'looks fine' });
    expect(events.at(-1)).toMatchObject({ type: 'approval_resolved', data: { approvalId: card.request.approvalId, decision: 'approve', by: 'owner' } });

    // Replaying the same nonce finds nothing.
    expect(httpStatus(() => reg.resolveByOwner(SESSION, card.request.approvalId, 'approve', card.request.nonce))).toBe(404);
    expect(reg.listPending(SESSION)).toHaveLength(0);
  });

  it('rejects resolution through another session id', () => {
    const { reg } = registry();
    const card = open(reg);
    expect(httpStatus(() => reg.resolveByOwner(SYSTEM_SESSION_ID, card.request.approvalId, 'approve', card.request.nonce))).toBe(404);
  });

  it('expires after the TTL as a deny by timeout', async () => {
    vi.useFakeTimers();
    const { reg, events } = registry(1_000);
    const card = open(reg);
    vi.advanceTimersByTime(1_001);
    await expect(card.result).resolves.toEqual({ decision: 'deny', by: 'timeout' });
    expect(events.at(-1)).toMatchObject({ type: 'approval_resolved', data: { decision: 'deny', by: 'timeout' } });
    expect(httpStatus(() => reg.resolveByOwner(SESSION, card.request.approvalId, 'approve', card.request.nonce))).toBe(404);
  });

  it('refuses an owner decision that arrives after expiresAt even if the timer has not fired', async () => {
    let now = 1_000_000;
    const { reg } = registry(60_000, () => now);
    const card = open(reg);
    now += 60_001;
    expect(httpStatus(() => reg.resolveByOwner(SESSION, card.request.approvalId, 'approve', card.request.nonce))).toBe(409);
    await expect(card.result).resolves.toMatchObject({ decision: 'deny', by: 'timeout' });
  });

  it('cancels by session, turn and globally (kill switch)', async () => {
    const { reg } = registry();
    const a = reg.open({ sessionId: SESSION, turnId: 'turn-1', source: 'tool', summary: 'a', uid: 10002, reasons: [], tainted: false });
    const b = reg.open({ sessionId: SESSION, turnId: 'turn-2', source: 'tool', summary: 'b', uid: 10002, reasons: [], tainted: false });
    const c = reg.open({ sessionId: SYSTEM_SESSION_ID, source: 'egress', summary: 'c', uid: 10002, reasons: [], tainted: false });
    expect(reg.cancelTurn('turn-1', 'owner')).toBe(1);
    await expect(a.result).resolves.toEqual({ decision: 'deny', by: 'owner' });
    expect(reg.cancelAll('kill')).toBe(2);
    await expect(b.result).resolves.toEqual({ decision: 'deny', by: 'kill' });
    await expect(c.result).resolves.toEqual({ decision: 'deny', by: 'kill' });
    expect(reg.size).toBe(0);
  });
});

describe('proxy approval bridge (/internal/approvals)', () => {
  function view(mode: ProxySessionView['mode'], tainted = false): ProxySessionView & { grants: Set<string> } {
    const grants = new Set<string>();
    return {
      mode,
      tainted,
      grants,
      hasGrant: (key) => grants.has(key),
      addGrant: (key) => {
        grants.add(key);
      },
    };
  }

  const body = (sessionId: string | null) =>
    validateProxyBody({ source: 'egress', sessionId, summary: 'CONNECT example.invalid:443', target: 'example.invalid', reasons: ['domain not on the allowlist'] });

  function deps(reg: ApprovalRegistry, sessions: Record<string, ProxySessionView>, killed = false) {
    return { approvals: reg, lookup: (id: string) => sessions[id] ?? null, isKilled: () => killed, waitMs: 60_000, uid: 10002 };
  }

  it('denies read_only sessions without a card', async () => {
    const { reg, events } = registry();
    await expect(handleProxyApproval(body(SESSION), deps(reg, { [SESSION]: view('read_only') }))).resolves.toEqual({ decision: 'deny', scope: 'once' });
    expect(events).toHaveLength(0);
  });

  it('auto-approves untainted autopilot sessions and records it', async () => {
    const { reg, events } = registry();
    await expect(handleProxyApproval(body(SESSION), deps(reg, { [SESSION]: view('autopilot') }))).resolves.toEqual({ decision: 'approve', scope: 'once' });
    expect(events.map((e) => e.type)).toEqual(['approval_request', 'approval_resolved']);
    expect(events[1]?.data).toMatchObject({ decision: 'approve', by: 'autopilot' });
  });

  it('asks the owner for tainted autopilot sessions', async () => {
    const { reg, events } = registry();
    const pending = handleProxyApproval(body(SESSION), deps(reg, { [SESSION]: view('autopilot', true) }));
    await vi.waitFor(() => expect(reg.listPending(SESSION)).toHaveLength(1));
    const card = reg.listPending(SESSION)[0]!;
    expect(card.tainted).toBe(true);
    reg.resolveByOwner(SESSION, card.approvalId, 'deny', card.nonce);
    await expect(pending).resolves.toEqual({ decision: 'deny', scope: 'once' });
    expect(events.at(-1)).toMatchObject({ data: { by: 'owner', decision: 'deny' } });
  });

  it('guarded: approve_session creates a session grant that skips later cards', async () => {
    const { reg } = registry();
    const session = view('guarded');
    const first = handleProxyApproval(body(SESSION), deps(reg, { [SESSION]: session }));
    await vi.waitFor(() => expect(reg.listPending(SESSION)).toHaveLength(1));
    const card = reg.listPending(SESSION)[0]!;
    reg.resolveByOwner(SESSION, card.approvalId, 'approve_session', card.nonce);
    await expect(first).resolves.toEqual({ decision: 'approve', scope: 'session' });
    expect(session.grants.has('egress:example.invalid')).toBe(true);
    await expect(handleProxyApproval(body(SESSION), deps(reg, { [SESSION]: session }))).resolves.toEqual({ decision: 'approve', scope: 'session' });
    expect(reg.size).toBe(0);
  });

  it('puts unknown or absent sessions on the system queue', async () => {
    const { reg } = registry();
    const pending = handleProxyApproval(body(null), deps(reg, {}));
    await vi.waitFor(() => expect(reg.listPending(SYSTEM_SESSION_ID)).toHaveLength(1));
    const card = reg.listPending(SYSTEM_SESSION_ID)[0]!;
    reg.resolveByOwner(SYSTEM_SESSION_ID, card.approvalId, 'approve', card.nonce);
    await expect(pending).resolves.toEqual({ decision: 'approve', scope: 'once' });

    const unknown = handleProxyApproval(body('01J9ZQ4X7V3N8K2M5P6R7S8T9Z'), deps(reg, {}));
    await vi.waitFor(() => expect(reg.listPending(SYSTEM_SESSION_ID)).toHaveLength(1));
    expect(reg.listPending(SYSTEM_SESSION_ID)[0]?.reasons.some((r) => r.includes('unknown session'))).toBe(true);
    reg.cancelAll('timeout');
    await expect(unknown).resolves.toEqual({ decision: 'deny', scope: 'once' });
  });

  it('never returns a session-scoped grant from the system queue', async () => {
    const { reg } = registry();
    const pending = handleProxyApproval(body('01J9ZQ4X7V3N8K2M5P6R7S8T9Z'), deps(reg, {}));
    await vi.waitFor(() => expect(reg.listPending(SYSTEM_SESSION_ID)).toHaveLength(1));
    const card = reg.listPending(SYSTEM_SESSION_ID)[0]!;
    reg.resolveByOwner(SYSTEM_SESSION_ID, card.approvalId, 'approve_session', card.nonce);
    await expect(pending).resolves.toEqual({ decision: 'approve', scope: 'once' });
  });

  it('tainted sessions never get a session-scoped grant', async () => {
    const { reg } = registry();
    const session = view('guarded', true);
    const pending = handleProxyApproval(body(SESSION), deps(reg, { [SESSION]: session }));
    await vi.waitFor(() => expect(reg.listPending(SESSION)).toHaveLength(1));
    const card = reg.listPending(SESSION)[0]!;
    reg.resolveByOwner(SESSION, card.approvalId, 'approve_session', card.nonce);
    await expect(pending).resolves.toEqual({ decision: 'approve', scope: 'once' });
    expect(session.grants.size).toBe(0);
  });

  it('denies everything while the kill switch is active', async () => {
    const { reg } = registry();
    await expect(handleProxyApproval(body(SESSION), deps(reg, { [SESSION]: view('autopilot') }, true))).resolves.toEqual({ decision: 'deny', scope: 'once' });
  });

  it('times out to deny and withdraws the card when the proxy disconnects', async () => {
    const { reg } = registry();
    const controller = new AbortController();
    const pending = handleProxyApproval(body(SESSION), deps(reg, { [SESSION]: view('guarded') }), controller.signal);
    await vi.waitFor(() => expect(reg.size).toBe(1));
    controller.abort();
    await expect(pending).resolves.toEqual({ decision: 'deny', scope: 'once' });
    expect(reg.size).toBe(0);
  });

  it('validates the body shape', () => {
    expect(() => validateProxyBody({ source: 'ftp', sessionId: null, summary: 'x', target: 'y', reasons: [] })).toThrow();
    expect(() => validateProxyBody({ source: 'docker', sessionId: 5, summary: 'x', target: 'y', reasons: [] })).toThrow();
    expect(validateProxyBody({ source: 'docker', sessionId: '', summary: 'x', target: 'y', reasons: [] }).sessionId).toBeNull();
  });
});
