import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AgentEvent } from '../src/contract.js';
import type { ToolDecision } from '../src/engines/types.js';
import { HttpError } from '../src/errors.js';
import { buildHandoffSummary } from '../src/sessions.js';
import { activateLease, createHarness, type TestHarness } from './helpers.js';

const DB = 'psql "$OET_AGENT_DATABASE_URL" -c';

async function events(h: TestHarness, sessionId: string): Promise<AgentEvent[]> {
  return h.store.readEvents(sessionId, 0);
}

async function types(h: TestHarness, sessionId: string): Promise<string[]> {
  return (await events(h, sessionId)).map((e) => e.type);
}

async function expectHttp(promise: Promise<unknown>, status: number, code?: string): Promise<void> {
  try {
    await promise;
  } catch (error) {
    expect(error).toBeInstanceOf(HttpError);
    expect((error as HttpError).status).toBe(status);
    if (code) expect((error as HttpError).code).toBe(code);
    return;
  }
  throw new Error(`expected HTTP ${status}`);
}

describe('SessionManager', () => {
  let h: TestHarness;

  beforeEach(() => {
    h = createHarness();
  });

  afterEach(() => {
    h.cleanup();
  });

  it('runs OpenCode through the shared worktree, lease, proxy attribution and resumeId flow', async () => {
    activateLease(h);
    const detail = await h.sessions.create({ engine: 'opencode', model: 'model-a', mode: 'guarded', title: 'OpenCode session' });
    await h.sessions.sendMessage(detail.id, { text: 'Inspect the repository' });
    await vi.waitFor(() => expect(h.adapters.opencode.sessions).toHaveLength(1));
    const session = h.adapters.opencode.sessions[0]!;
    expect(session.options.cwd).toContain('/workspace/sessions/');
    expect(session.options.env.HTTPS_PROXY).toContain(detail.id);
    expect(session.options.mode).toBe('guarded');

    session.turns[0]!.release();
    await vi.waitFor(() => expect(h.store.getSession(detail.id)?.resumeId).toBe(`resume-${detail.id}`));
  });

  it('creates a session with a worktree branch and runs a turn end to end', async () => {
    activateLease(h);
    const detail = await h.sessions.create({ engine: 'claude', model: 'model-a', effort: 'high', mode: 'guarded', title: 'Docs tweak' });
    expect(detail).toMatchObject({ engine: 'claude', model: 'model-a', effort: 'high', mode: 'guarded', status: 'idle', tainted: false, pendingApprovals: [] });
    expect(detail.branch).toMatch(/^agent\//);

    const { turnId } = await h.sessions.sendMessage(detail.id, { text: 'Update the README' });
    await vi.waitFor(() => expect(h.adapters.claude.turns).toHaveLength(1));
    const turn = h.adapters.claude.turns[0]!;
    expect(turn.opts).toEqual({ model: 'model-a', effort: 'high', mode: 'guarded' });
    expect(h.adapters.claude.sessions[0]?.options.appendSystemPrompt).toContain('Operating manual');
    expect(h.adapters.claude.sessions[0]?.options.env.HTTPS_PROXY).toContain(detail.id);

    turn.hooks.emit({ type: 'text', data: { messageId: 'm1', text: 'Done.' } });
    turn.hooks.emit({ type: 'usage', data: { model: 'model-a', inputTokens: 10, outputTokens: 5, costUsd: 0.01 } });
    turn.release();
    await vi.waitFor(async () => expect(await types(h, detail.id)).toContain('turn_complete'));

    const all = await events(h, detail.id);
    expect(all.map((e) => e.type)).toEqual(['turn_started', 'user_message', 'text', 'usage', 'turn_complete']);
    expect(all.every((e) => e.turnId === turnId)).toBe(true);
    expect(all.at(-1)?.data).toMatchObject({ status: 'ok' });
    const after = h.sessions.get(detail.id);
    expect(after).toMatchObject({ status: 'idle', usage: { inputTokens: 10, outputTokens: 5, costUsd: 0.01 } });
    expect(h.store.getSession(detail.id)?.resumeId).toBe(`resume-${detail.id}`);
  });

  it.each(['claude', 'codex', 'opencode'] as const)('delivers Jev advice to %s without changing the recorded message or native settings', async (engine) => {
    activateLease(h);
    const session = await h.sessions.create({ engine, model: 'model-a', effort: 'high', mode: 'guarded' });
    const text = 'Diagnose the grading exception without changing official marks.';
    const jevAdvisory = {
      status: 'ok', model: 'jev-1.13.0', requiresHumanReview: false,
      taskKind: 'debug', taskConfidence: 1, riskLevel: 'elevated', riskConfidence: 1, reason: null,
    };

    await h.sessions.sendMessage(session.id, { text, jevAdvisory });
    await vi.waitFor(() => expect(h.adapters[engine].turns).toHaveLength(1));
    const turn = h.adapters[engine].turns[0]!;
    expect(turn.text).toContain('Jev development advisory');
    expect(turn.text).toContain('"taskKind":"debug"');
    expect(turn.text).toContain('"riskLevel":"elevated"');
    expect(turn.text.endsWith(text)).toBe(true);
    expect(turn.opts).toEqual({ model: 'model-a', effort: 'high', mode: 'guarded' });
    expect((await events(h, session.id)).find((event) => event.type === 'user_message')?.data.text).toBe(text);
    turn.release();
    await vi.waitFor(async () => expect(await types(h, session.id)).toContain('turn_complete'));
  });

  it.each([
    { taskKind: 'grant_permission' },
    { riskLevel: 'approved' },
    { taskConfidence: 0.49 },
    { riskConfidence: Number.NaN },
    { model: 'jev-latest' },
  ])('rejects malformed Jev advice before opening an engine: %j', async (invalid) => {
    activateLease(h);
    const session = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'guarded' });
    const jevAdvisory = {
      status: 'ok', model: 'jev-1.13.0', requiresHumanReview: false,
      taskKind: 'debug', taskConfidence: 1, riskLevel: 'elevated', riskConfidence: 1,
      ...invalid,
    };

    await expectHttp(h.sessions.sendMessage(session.id, { text: 'Investigate', jevAdvisory }), 400);
    expect(h.adapters.claude.turns).toHaveLength(0);
    expect(h.sessions.get(session.id).status).toBe('idle');
  });

  it('does not turn low-risk Jev advice into permission for a read-only write', async () => {
    activateLease(h);
    const decisions: ToolDecision[] = [];
    h.adapters.codex.script = async (turn) => {
      decisions.push(await turn.hooks.onToolCall({ toolCallId: 'write', name: 'fileChange', input: {}, writePaths: ['src/a.ts'] }, turn.signal));
    };
    const session = await h.sessions.create({ engine: 'codex', model: 'model-a', mode: 'read_only' });
    await h.sessions.sendMessage(session.id, {
      text: 'Review the module',
      jevAdvisory: {
        status: 'ok', model: 'jev-1.13.0', requiresHumanReview: false,
        taskKind: 'review', taskConfidence: 1, riskLevel: 'low', riskConfidence: 1,
      },
    });

    await vi.waitFor(() => expect(decisions).toHaveLength(1));
    expect(decisions[0]).toMatchObject({ behavior: 'deny' });
    expect(h.approvals.size).toBe(0);
  });

  it('rejects unknown models, unsupported efforts and signed-out engines', async () => {
    await expectHttp(h.sessions.create({ engine: 'claude', model: 'nope', mode: 'guarded' }), 400, 'unknown_model');
    await expectHttp(h.sessions.create({ engine: 'claude', model: 'model-b', effort: 'high', mode: 'guarded' }), 400, 'effort_not_supported');
    await expectHttp(h.sessions.create({ engine: 'claude', model: 'model-a', effort: 'max', mode: 'guarded' }), 400, 'unknown_effort');
    h.adapters.codex.authState = 'signed_out';
    await expectHttp(h.sessions.create({ engine: 'codex', model: 'model-a', mode: 'guarded' }), 409, 'engine_not_signed_in');
    await expectHttp(h.sessions.create({ engine: 'gemini', model: 'model-a', mode: 'guarded' }), 400);
  });

  it('enforces the concurrency cap of 2 active turns (429)', async () => {
    activateLease(h);
    const a = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'guarded' });
    const b = await h.sessions.create({ engine: 'codex', model: 'model-a', mode: 'guarded' });
    const c = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'guarded' });
    await h.sessions.sendMessage(a.id, { text: 'one' });
    await h.sessions.sendMessage(b.id, { text: 'two' });
    expect(h.sessions.activeTurnCount()).toBe(2);
    await expectHttp(h.sessions.sendMessage(c.id, { text: 'three' }), 429, 'concurrency_limit');
    await expectHttp(h.sessions.sendMessage(a.id, { text: 'again' }), 409, 'turn_in_progress');

    await vi.waitFor(() => expect(h.adapters.claude.turns).toHaveLength(1));
    h.adapters.claude.turns[0]!.release();
    await vi.waitFor(() => expect(h.sessions.activeTurnCount()).toBe(1));
    await expect(h.sessions.sendMessage(c.id, { text: 'three' })).resolves.toHaveProperty('turnId');
  });

  it('blocks new turns without a lease, when draining and after the kill switch (423)', async () => {
    const s = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'guarded' });
    await expectHttp(h.sessions.sendMessage(s.id, { text: 'hi' }), 423, 'lease_expired');
    activateLease(h);
    h.control.draining = true;
    await expectHttp(h.sessions.sendMessage(s.id, { text: 'hi' }), 423, 'draining');
    h.control.draining = false;
    h.control.killed = true;
    await expectHttp(h.sessions.sendMessage(s.id, { text: 'hi' }), 423, 'killed');
  });

  it('guarded: destructive DB call → owner card → approve → table snapshot → allow', async () => {
    activateLease(h);
    const decisions: ToolDecision[] = [];
    h.adapters.claude.script = async (turn) => {
      decisions.push(await turn.hooks.onToolCall({ toolCallId: 'tc-1', name: 'Bash', input: {}, command: `${DB} "DELETE FROM scratch WHERE true"` }, turn.signal));
    };
    const s = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'guarded' });
    await h.sessions.sendMessage(s.id, { text: 'clean scratch' });
    await vi.waitFor(() => expect(h.approvals.listPending(s.id)).toHaveLength(1));
    expect(h.sessions.get(s.id).status).toBe('awaiting_approval');
    const card = h.approvals.listPending(s.id)[0]!;
    expect(card.reasons.join(' ')).toMatch(/DELETE without a restrictive WHERE/);
    expect(card.uid).toBe(10002);
    h.sessions.resolveApproval(s.id, card.approvalId, { decision: 'approve', nonce: card.nonce });
    await vi.waitFor(() => expect(decisions).toEqual([{ behavior: 'allow' }]));
    expect(h.snapshots.requests).toEqual([expect.objectContaining({ tables: ['scratch'] })]);
    await vi.waitFor(async () => expect(await types(h, s.id)).toContain('turn_complete'));
    const snap = (await events(h, s.id)).find((e) => e.type === 'snapshot');
    expect(snap?.data).toMatchObject({ approvalId: card.approvalId, ok: true, file: '/backups/agent-snap-test.dump' });
  });

  it('guarded: denied card returns a deny decision to the engine', async () => {
    activateLease(h);
    const decisions: ToolDecision[] = [];
    h.adapters.claude.script = async (turn) => {
      decisions.push(await turn.hooks.onToolCall({ toolCallId: 'tc-1', name: 'Bash', input: {}, command: 'rm -rf /backups' }, turn.signal));
    };
    const s = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'guarded' });
    await h.sessions.sendMessage(s.id, { text: 'x' });
    await vi.waitFor(() => expect(h.approvals.listPending(s.id)).toHaveLength(1));
    const card = h.approvals.listPending(s.id)[0]!;
    h.sessions.resolveApproval(s.id, card.approvalId, { decision: 'deny', nonce: card.nonce, note: 'no' });
    await vi.waitFor(() => expect(decisions).toHaveLength(1));
    expect(decisions[0]).toMatchObject({ behavior: 'deny' });
    expect(h.snapshots.requests).toHaveLength(0);
  });

  it('autopilot: destructive DB call is pre-snapshotted and auto-approved (audited)', async () => {
    activateLease(h);
    const decisions: ToolDecision[] = [];
    h.adapters.claude.script = async (turn) => {
      decisions.push(await turn.hooks.onToolCall({ toolCallId: 'tc-1', name: 'Bash', input: {}, command: `${DB} "DROP TABLE scratch"` }, turn.signal));
    };
    const s = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'autopilot' });
    await h.sessions.sendMessage(s.id, { text: 'drop it' });
    await vi.waitFor(() => expect(decisions).toEqual([{ behavior: 'allow' }]));
    const ev = await events(h, s.id);
    const resolved = ev.find((e) => e.type === 'approval_resolved');
    expect(resolved?.data).toMatchObject({ decision: 'approve', by: 'autopilot' });
    const order = ev.map((e) => e.type).filter((t) => ['approval_request', 'snapshot', 'approval_resolved'].includes(t));
    expect(order).toEqual(['approval_request', 'snapshot', 'approval_resolved']);
  });

  it('autopilot: a failed pre-snapshot means the operation does not run', async () => {
    activateLease(h);
    h.snapshots.result = { ok: false, error: 'rate limited' };
    const decisions: ToolDecision[] = [];
    h.adapters.claude.script = async (turn) => {
      decisions.push(await turn.hooks.onToolCall({ toolCallId: 'tc-1', name: 'Bash', input: {}, command: `${DB} "TRUNCATE scratch"` }, turn.signal));
    };
    const s = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'autopilot' });
    await h.sessions.sendMessage(s.id, { text: 'x' });
    await vi.waitFor(() => expect(decisions).toHaveLength(1));
    expect(decisions[0]).toMatchObject({ behavior: 'deny' });
    const resolved = (await events(h, s.id)).find((e) => e.type === 'approval_resolved');
    expect(resolved?.data).toMatchObject({ decision: 'deny', by: 'autopilot' });
  });

  it('taint: reading docker logs taints the session; later DB writes need the owner even in autopilot', async () => {
    activateLease(h);
    const decisions: ToolDecision[] = [];
    h.adapters.claude.script = async (turn) => {
      decisions.push(await turn.hooks.onToolCall({ toolCallId: 'a', name: 'Bash', input: {}, command: 'docker logs oet-api-blue --tail 50' }, turn.signal));
      decisions.push(await turn.hooks.onToolCall({ toolCallId: 'b', name: 'Bash', input: {}, command: `${DB} "INSERT INTO scratch VALUES (1)"` }, turn.signal));
      decisions.push(await turn.hooks.onToolCall({ toolCallId: 'c', name: 'Edit', input: { file_path: 'src/a.ts', old_string: 'a', new_string: 'b' } }, turn.signal));
    };
    const s = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'autopilot' });
    await h.sessions.sendMessage(s.id, { text: 'investigate' });
    await vi.waitFor(() => expect(h.approvals.listPending(s.id)).toHaveLength(1));
    expect(decisions).toEqual([{ behavior: 'allow' }]);
    expect(h.sessions.get(s.id).tainted).toBe(true);
    const card = h.approvals.listPending(s.id)[0]!;
    expect(card.tainted).toBe(true);
    expect(card.reasons.join(' ')).toMatch(/tainted/);
    h.sessions.resolveApproval(s.id, card.approvalId, { decision: 'approve', nonce: card.nonce });
    await vi.waitFor(() => expect(decisions).toHaveLength(3));
    expect(decisions).toEqual([{ behavior: 'allow' }, { behavior: 'allow' }, { behavior: 'allow' }]);
    const taint = (await events(h, s.id)).filter((e) => e.type === 'taint');
    expect(taint).toHaveLength(1);
    expect(taint[0]?.data).toMatchObject({ source: 'docker_logs' });
    expect(h.store.getSession(s.id)?.tainted).toBe(true);
  });

  it('taint: a tainting read that ran without an approval request still taints (tool_result)', async () => {
    activateLease(h);
    let sid = '';
    let taintedAfterDenied: boolean | null = null;
    h.adapters.codex.script = async (turn) => {
      // Denied/never-run call: no taint.
      turn.hooks.emit({ type: 'tool_call', data: { toolCallId: 'w0', name: 'web_search', input: { query: 'x' } } });
      turn.hooks.emit({ type: 'tool_result', data: { toolCallId: 'w0', ok: false, output: 'denied' } });
      taintedAfterDenied = h.sessions.get(sid).tainted;
      // Ran: taints.
      turn.hooks.emit({ type: 'tool_call', data: { toolCallId: 'w1', name: 'web_search', input: { query: 'x' } } });
      turn.hooks.emit({ type: 'tool_result', data: { toolCallId: 'w1', ok: true, output: 'results' } });
    };
    const s = await h.sessions.create({ engine: 'codex', model: 'model-a', mode: 'autopilot' });
    sid = s.id;
    await h.sessions.sendMessage(sid, { text: 'search' });
    await vi.waitFor(async () => expect(await types(h, sid)).toContain('turn_complete'));
    expect(taintedAfterDenied).toBe(false);
    expect(h.sessions.get(sid).tainted).toBe(true);
    const ev = await events(h, sid);
    const taint = ev.filter((e) => e.type === 'taint');
    expect(taint).toHaveLength(1);
    expect(taint[0]?.data).toMatchObject({ source: 'web' });
    // The taint event follows the tool_result that carried the untrusted output.
    expect(ev.findIndex((e) => e.type === 'taint')).toBeGreaterThan(ev.findIndex((e) => e.type === 'tool_result' && e.data.toolCallId === 'w1'));
  });

  it('read_only denies writes without asking', async () => {
    activateLease(h);
    const decisions: ToolDecision[] = [];
    h.adapters.codex.script = async (turn) => {
      decisions.push(await turn.hooks.onToolCall({ toolCallId: 'a', name: 'commandExecution', input: { command: ['bash', '-lc', 'git status'] } }, turn.signal));
      decisions.push(await turn.hooks.onToolCall({ toolCallId: 'b', name: 'fileChange', input: {}, writePaths: ['src/a.ts'] }, turn.signal));
    };
    const s = await h.sessions.create({ engine: 'codex', model: 'model-a', mode: 'read_only' });
    await h.sessions.sendMessage(s.id, { text: 'look around' });
    await vi.waitFor(() => expect(decisions).toHaveLength(2));
    expect(decisions[0]).toEqual({ behavior: 'allow' });
    expect(decisions[1]).toMatchObject({ behavior: 'deny' });
    expect(h.approvals.size).toBe(0);
  });

  it('lease lapse drops autopilot to guarded and pauses at the next tool boundary', async () => {
    let now = Date.now();
    const lease = h.lease;
    lease.set(now + 60_000);
    let proceed!: () => void;
    const gate = new Promise<void>((resolve) => {
      proceed = resolve;
    });
    const decisions: ToolDecision[] = [];
    h.adapters.claude.script = async (turn) => {
      await gate;
      decisions.push(await turn.hooks.onToolCall({ toolCallId: 'a', name: 'Bash', input: {}, command: 'git status' }, turn.signal));
    };
    const s = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'autopilot' });
    await h.sessions.sendMessage(s.id, { text: 'x' });
    // Expire the lease.
    lease.set(new Date(now - 1).toISOString());
    expect(h.sessions.get(s.id).mode).toBe('guarded');
    expect((await events(h, s.id)).some((e) => e.type === 'mode_changed' && e.data.reason === 'lease_expired')).toBe(true);
    proceed();
    await vi.waitFor(async () => expect((await events(h, s.id)).some((e) => e.data.code === 'lease_paused')).toBe(true));
    expect(decisions).toHaveLength(0);
    now = Date.now();
    lease.set(now + 60_000);
    await vi.waitFor(() => expect(decisions).toEqual([{ behavior: 'allow' }]));
  });

  it('interrupt aborts the running turn and denies its pending cards', async () => {
    activateLease(h);
    const decisions: ToolDecision[] = [];
    h.adapters.claude.script = async (turn) => {
      decisions.push(await turn.hooks.onToolCall({ toolCallId: 'a', name: 'Bash', input: {}, command: 'bash -c id' }, turn.signal));
      return { status: 'interrupted' };
    };
    const s = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'guarded' });
    await h.sessions.sendMessage(s.id, { text: 'x' });
    await vi.waitFor(() => expect(h.approvals.listPending(s.id)).toHaveLength(1));
    await h.sessions.interrupt(s.id);
    await vi.waitFor(() => expect(decisions).toHaveLength(1));
    expect(decisions[0]).toMatchObject({ behavior: 'deny' });
    await vi.waitFor(() => expect(h.sessions.activeTurnCount()).toBe(0));
    expect(h.sessions.get(s.id).status).toBe('interrupted');
  });

  it('boot marks in-flight turns interrupted and they stay resumable', async () => {
    activateLease(h);
    const s = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'guarded' });
    h.store.insertTurn({ id: 'turn-dead', sessionId: s.id, model: 'model-a', effort: null, mode: 'guarded', startedAt: new Date().toISOString() });
    h.store.updateSession(s.id, { status: 'running' });
    h.sessions.boot();
    expect(h.sessions.get(s.id).status).toBe('interrupted');
    const last = (await events(h, s.id)).at(-1);
    expect(last).toMatchObject({ type: 'turn_complete', turnId: 'turn-dead', data: { status: 'interrupted' } });
    await expect(h.sessions.sendMessage(s.id, { text: 'continue' })).resolves.toHaveProperty('turnId');
  });

  it('patch switches mode/model/effort and needs a lease for autopilot', async () => {
    const s = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'guarded' });
    await expectHttp(h.sessions.patch(s.id, { mode: 'autopilot' }), 423, 'lease_expired');
    activateLease(h);
    const updated = await h.sessions.patch(s.id, { mode: 'autopilot', model: 'model-b', title: 'Renamed' });
    expect(updated).toMatchObject({ mode: 'autopilot', model: 'model-b', title: 'Renamed' });
    expect(updated.effort).toBeUndefined();
    expect((await events(h, s.id)).find((e) => e.type === 'mode_changed')?.data).toEqual({ mode: 'autopilot', reason: 'owner' });
  });

  it('archive removes the worktree unless a handoff session still uses it', async () => {
    activateLease(h);
    const s = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'guarded' });
    const next = await h.sessions.handoff(s.id, { engine: 'codex', model: 'model-a' });
    expect(next).toMatchObject({ engine: 'codex', handoffFrom: s.id, branch: s.branch });
    expect(h.sessions.get(s.id).status).toBe('archived');
    expect(h.workspace.removed).toEqual([]);
    await h.sessions.patch(next.id, { archived: true });
    expect(h.workspace.removed).toEqual([h.store.getSession(next.id)?.worktree]);
  });

  it('handoff seeds the new engine session with a summary of recent events', async () => {
    activateLease(h);
    h.adapters.claude.script = async (turn) => {
      turn.hooks.emit({ type: 'text', data: { messageId: 'm', text: 'I updated the README.' } });
    };
    const s = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'guarded' });
    await h.sessions.sendMessage(s.id, { text: 'Please update the README' });
    await vi.waitFor(() => expect(h.sessions.activeTurnCount()).toBe(0));
    const next = await h.sessions.handoff(s.id, { engine: 'codex', model: 'model-a' });
    await h.sessions.sendMessage(next.id, { text: 'carry on' });
    await vi.waitFor(() => expect(h.adapters.codex.sessions).toHaveLength(1));
    const prompt = h.adapters.codex.sessions[0]!.options.appendSystemPrompt;
    expect(prompt).toContain('Handoff context');
    expect(prompt).toContain('Please update the README');
    expect(prompt).toContain('I updated the README.');
    expect(h.adapters.codex.sessions[0]!.options.cwd).toBe(h.store.getSession(s.id)?.worktree);
  });
});

describe('buildHandoffSummary', () => {
  it('keeps the most recent activity within the budget', () => {
    const ev = (seq: number, type: string, data: Record<string, unknown>): AgentEvent => ({ seq, sessionId: 'S', ts: '', type, data });
    const summary = buildHandoffSummary(
      { id: 'S', engine: 'claude', model: 'm', branch: 'agent/x', worktree: '/workspace/sessions/S' },
      [
        ev(1, 'user_message', { text: 'first request' }),
        ev(2, 'text_delta', { messageId: 'a', text: 'ignored delta' }),
        ev(3, 'tool_call', { name: 'Bash', command: 'git status' }),
        ev(4, 'tool_result', { ok: true, output: 'clean', exitCode: 0 }),
        ev(5, 'text', { messageId: 'a', text: 'All clean.' }),
        ev(6, 'turn_complete', { status: 'ok' }),
      ],
    );
    expect(summary).toContain('branch agent/x');
    expect(summary).toContain('- Owner: first request');
    expect(summary).toContain('- Tool Bash: git status');
    expect(summary).toContain('→ ok (exit 0): clean');
    expect(summary).toContain('- Assistant: All clean.');
    expect(summary).not.toContain('ignored delta');

    const long = buildHandoffSummary({ id: 'S', engine: 'claude', model: 'm', branch: 'b', worktree: 'w' }, [ev(1, 'user_message', { text: 'x'.repeat(50_000) }), ev(2, 'text', { text: 'the latest' })], 3_000);
    expect(long.length).toBeLessThanOrEqual(3_100);
    expect(long).toContain('the latest');
  });
});

describe('SessionManager erasure and retention', () => {
  let h: TestHarness;

  beforeEach(() => {
    h = createHarness();
  });

  afterEach(() => {
    h.cleanup();
  });

  it('erases a session: index row, events and worktree; refused while a turn runs', async () => {
    activateLease(h);
    const detail = await h.sessions.create({ engine: 'claude', model: 'model-a', mode: 'guarded' });
    await h.sessions.sendMessage(detail.id, { text: 'work' });
    await vi.waitFor(() => expect(h.adapters.claude.turns).toHaveLength(1));
    await expectHttp(h.sessions.erase(detail.id), 409, 'session_running');

    h.adapters.claude.turns[0]!.release();
    await vi.waitFor(async () => expect(await types(h, detail.id)).toContain('turn_complete'));

    await expect(h.sessions.erase(detail.id)).resolves.toMatchObject({ erased: true });
    expect(h.sessions.exists(detail.id)).toBe(false);
    expect(await h.store.readEvents(detail.id, 0)).toEqual([]);
    expect(h.workspace.removed).toHaveLength(1);
    await expectHttp(h.sessions.erase(detail.id), 404);
  });
});
