import { appendFileSync, readFileSync, rmSync } from 'node:fs';
import path from 'node:path';
import { ulid } from 'ulid';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SYSTEM_SESSION_ID } from '../src/contract.js';
import { Redactor } from '../src/redact.js';
import { Store, type SessionRow } from '../src/store.js';
import { tempDir } from './helpers.js';

function sessionRow(id: string, overrides: Partial<SessionRow> = {}): SessionRow {
  const now = new Date().toISOString();
  return {
    id,
    title: 'Test',
    engine: 'claude',
    model: 'model-a',
    effort: null,
    mode: 'guarded',
    status: 'idle',
    branch: `agent/20260927-test-${id.slice(-6).toLowerCase()}`,
    worktree: `/workspace/sessions/${id}`,
    tainted: false,
    archived: false,
    createdAt: now,
    updatedAt: now,
    lastSeq: 0,
    inputTokens: 0,
    outputTokens: 0,
    costUsd: null,
    resumeId: null,
    handoffFrom: null,
    seedSummary: null,
    prNumber: null,
    prUrl: null,
    prState: null,
    ...overrides,
  };
}

describe('Store', () => {
  let root: string;
  let secret: string;
  let store: Store;

  const open = (now?: () => Date): Store =>
    new Store({
      dbPath: path.join(root, 'index.sqlite'),
      sessionsDir: path.join(root, 'sessions'),
      redactor: new Redactor([secret]),
      ...(now ? { now } : {}),
    });

  beforeEach(() => {
    root = tempDir();
    secret = `known-secret-${'z'.repeat(24)}`;
    store = open();
  });

  afterEach(() => {
    store.close();
    rmSync(root, { recursive: true, force: true });
  });

  it('assigns a monotonic seq starting at 1 and replays after a given seq', async () => {
    const id = ulid();
    store.insertSession(sessionRow(id));
    const a = store.appendEvent(id, 'user_message', { text: 'one' });
    const b = store.appendEvent(id, 'text', { messageId: 'm1', text: 'two' }, 'turn-1');
    const c = store.appendEvent(id, 'turn_complete', { status: 'ok', durationMs: 5 }, 'turn-1');
    expect([a.seq, b.seq, c.seq]).toEqual([1, 2, 3]);
    expect(b.turnId).toBe('turn-1');
    expect(a.turnId).toBeUndefined();

    expect((await store.readEvents(id, 0)).map((e) => e.seq)).toEqual([1, 2, 3]);
    expect((await store.readEvents(id, 1)).map((e) => e.seq)).toEqual([2, 3]);
    expect(await store.readEvents(id, 3)).toEqual([]);
    expect(store.getSession(id)?.lastSeq).toBe(3);
  });

  it('continues the sequence after a restart and survives a torn last line', async () => {
    const id = ulid();
    store.insertSession(sessionRow(id));
    store.appendEvent(id, 'user_message', { text: 'one' });
    store.appendEvent(id, 'user_message', { text: 'two' });
    store.close();

    // Simulate a crash in the middle of writing the next line.
    appendFileSync(path.join(root, 'sessions', id, 'events.jsonl'), '{"seq":3,"sessionId":"');
    store = open();
    expect(store.lastSeq(id)).toBe(2);
    const next = store.appendEvent(id, 'user_message', { text: 'three' });
    expect(next.seq).toBe(3);
    const events = await store.readEvents(id, 0);
    expect(events.map((e) => e.seq)).toEqual([1, 2, 3]);
    expect(events[2]?.data).toEqual({ text: 'three' });
  });

  it('redacts before anything reaches disk', async () => {
    const id = ulid();
    store.insertSession(sessionRow(id));
    const ghs = `${'gh'}s_${'Q'.repeat(36)}`;
    store.appendEvent(id, 'tool_result', { toolCallId: 't', ok: true, output: `echo ${secret} ${ghs}` });
    const raw = readFileSync(store.eventsFile(id), 'utf8');
    expect(raw).not.toContain(secret);
    expect(raw).not.toContain(ghs);
    expect(raw).toContain('[REDACTED:secret]');
    const [event] = await store.readEvents(id, 0);
    expect(String(event?.data.output)).toContain('[REDACTED:github_token]');
  });

  it('notifies live subscribers in order', () => {
    const id = ulid();
    store.insertSession(sessionRow(id));
    const seen: number[] = [];
    const off = store.subscribe(id, (e) => seen.push(e.seq));
    store.appendEvent(id, 'a', {});
    store.appendEvent(id, 'b', {});
    off();
    store.appendEvent(id, 'c', {});
    expect(seen).toEqual([1, 2]);
  });

  it('keeps the system queue sequence even without a session row', () => {
    expect(store.appendEvent(SYSTEM_SESSION_ID, 'approval_request', { approvalId: 'x' }).seq).toBe(1);
    store.close();
    store = open();
    expect(store.appendEvent(SYSTEM_SESSION_ID, 'approval_resolved', { approvalId: 'x' }).seq).toBe(2);
  });

  it('rejects ids that are not ULIDs (path traversal)', () => {
    expect(() => store.appendEvent('../../etc', 'x', {})).toThrow();
  });

  it('marks in-flight turns interrupted on boot', () => {
    const id = ulid();
    store.insertSession(sessionRow(id, { status: 'running' }));
    store.insertTurn({ id: 'turn-a', sessionId: id, model: 'model-a', effort: null, mode: 'guarded', startedAt: new Date().toISOString() });
    const interrupted = store.interruptInFlightTurns();
    expect(interrupted).toEqual([expect.objectContaining({ turnId: 'turn-a', sessionId: id })]);
    expect(store.getTurn('turn-a')?.status).toBe('interrupted');
    expect(store.getSession(id)?.status).toBe('interrupted');
    expect(store.interruptInFlightTurns()).toEqual([]);
  });

  it('aggregates usage', () => {
    const id = ulid();
    store.insertSession(sessionRow(id));
    store.addUsage(id, { inputTokens: 100, outputTokens: 20 });
    store.addUsage(id, { inputTokens: 5, outputTokens: 1, costUsd: 0.25 });
    store.addUsage(id, { inputTokens: 1, outputTokens: 1, costUsd: 0.5 });
    expect(store.getSession(id)).toMatchObject({ inputTokens: 106, outputTokens: 22, costUsd: 0.75 });
  });

  it('sweeps sessions older than the retention window', () => {
    const oldId = ulid();
    const freshId = ulid();
    const old = new Date(Date.now() - 91 * 86_400_000).toISOString();
    store.insertSession(sessionRow(oldId, { createdAt: old, updatedAt: old }));
    store.insertSession(sessionRow(freshId));
    store.appendEvent(freshId, 'user_message', { text: 'hi' });
    const removed = store.sweep(90);
    expect(removed.map((r) => r.id)).toEqual([oldId]);
    expect(store.getSession(oldId)).toBeNull();
    expect(store.getSession(freshId)).not.toBeNull();
  });

  it('persists ship states and kv', () => {
    const id = ulid();
    store.insertSession(sessionRow(id));
    const now = new Date().toISOString();
    store.saveShip({ shipId: 'ship-1', sessionId: id, phase: 'pushing', startedAt: now, updatedAt: now });
    expect(store.listUnfinishedShips().map((s) => s.shipId)).toEqual(['ship-1']);
    store.saveShip({ shipId: 'ship-1', sessionId: id, phase: 'done', startedAt: now, updatedAt: now });
    expect(store.listUnfinishedShips()).toEqual([]);
    expect(store.getLatestShip(id)?.phase).toBe('done');
    store.setKv('k', { a: 1 });
    expect(store.getKv<{ a: number }>('k')).toEqual({ a: 1 });
    store.deleteKv('k');
    expect(store.getKv('k')).toBeNull();
  });
});
