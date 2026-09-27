import Database from 'better-sqlite3';
import { appendFileSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
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

  it('records the first user message (redacted, clipped) and never overwrites it', () => {
    const id = ulid();
    store.insertSession(sessionRow(id));
    expect(store.getSession(id)?.firstMessage).toBeNull();
    store.appendEvent(id, 'text', { messageId: 'm', text: 'not a user message' });
    expect(store.getSession(id)?.firstMessage).toBeNull();
    store.appendEvent(id, 'user_message', { text: `Deploy   with ${secret}\nplease ${'x'.repeat(300)}` });
    const first = store.getSession(id)?.firstMessage ?? '';
    expect(first.startsWith('Deploy with [REDACTED:secret] please x')).toBe(true);
    expect(first).not.toContain(secret);
    expect(first).toHaveLength(200);
    store.appendEvent(id, 'user_message', { text: 'second message' });
    expect(store.getSession(id)?.firstMessage).toBe(first);
  });

  it('stores createdBy', () => {
    const id = ulid();
    store.insertSession(sessionRow(id, { createdBy: 'owner-a' }));
    expect(store.getSession(id)?.createdBy).toBe('owner-a');
    const legacy = ulid();
    store.insertSession(sessionRow(legacy));
    expect(store.getSession(legacy)?.createdBy).toBeNull();
  });

  it('migrates a pre-v1.2 database in place and backfills first_message from the JSONL', () => {
    store.close();
    rmSync(path.join(root, 'index.sqlite'), { force: true });
    rmSync(path.join(root, 'index.sqlite-wal'), { force: true });
    rmSync(path.join(root, 'index.sqlite-shm'), { force: true });

    // The original sessions table, without created_by / first_message.
    const legacyDb = new Database(path.join(root, 'index.sqlite'));
    legacyDb.exec(`CREATE TABLE sessions (
      id TEXT PRIMARY KEY, title TEXT NOT NULL, engine TEXT NOT NULL, model TEXT NOT NULL, effort TEXT,
      mode TEXT NOT NULL, status TEXT NOT NULL, branch TEXT NOT NULL, worktree TEXT NOT NULL,
      tainted INTEGER NOT NULL DEFAULT 0, archived INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL, last_seq INTEGER NOT NULL DEFAULT 0, input_tokens INTEGER NOT NULL DEFAULT 0,
      output_tokens INTEGER NOT NULL DEFAULT 0, cost_usd REAL, resume_id TEXT, handoff_from TEXT,
      seed_summary TEXT, pr_number INTEGER, pr_url TEXT, pr_state TEXT)`);
    const withMessage = ulid();
    const withoutMessage = ulid();
    const noLog = ulid();
    const insert = legacyDb.prepare(
      `INSERT INTO sessions (id, title, engine, model, mode, status, branch, worktree, created_at, updated_at)
       VALUES (?, ?, 'claude', 'model-a', 'guarded', 'idle', 'agent/x', '/w', ?, ?)`,
    );
    const now = new Date().toISOString();
    for (const id of [withMessage, withoutMessage, noLog]) insert.run(id, `Legacy ${id.slice(-4)}`, now, now);
    legacyDb.close();

    const writeLog = (id: string, events: object[]): void => {
      mkdirSync(path.join(root, 'sessions', id), { recursive: true });
      writeFileSync(path.join(root, 'sessions', id, 'events.jsonl'), events.map((e) => `${JSON.stringify(e)}\n`).join(''));
    };
    writeLog(withMessage, [
      { seq: 1, sessionId: withMessage, ts: now, type: 'turn_started', data: {} },
      { seq: 2, sessionId: withMessage, ts: now, type: 'user_message', data: { text: `Rotate ${secret} for the T3 runner` } },
      { seq: 3, sessionId: withMessage, ts: now, type: 'user_message', data: { text: 'later message' } },
    ]);
    writeLog(withoutMessage, [{ seq: 1, sessionId: withoutMessage, ts: now, type: 'text', data: { text: 'hi' } }]);

    store = open();
    const columns = (name: string): string[] => {
      const db = new Database(path.join(root, 'index.sqlite'), { readonly: true });
      try {
        return (db.prepare(`PRAGMA table_info(${name})`).all() as { name: string }[]).map((c) => c.name);
      } finally {
        db.close();
      }
    };
    expect(columns('sessions')).toEqual(expect.arrayContaining(['created_by', 'first_message']));
    expect(store.getSession(withMessage)?.firstMessage).toBe('Rotate [REDACTED:secret] for the T3 runner');
    expect(store.getSession(withoutMessage)?.firstMessage).toBeNull();
    expect(store.getSession(noLog)?.firstMessage).toBeNull();
    expect(store.getSession(withMessage)?.createdBy).toBeNull();
    expect(store.listSessions({ q: 't3 RUNNER' }).map((s) => s.id)).toEqual([withMessage]);

    // Idempotent: reopening neither fails nor re-adds columns; a later first message still lands.
    store.close();
    store = open();
    expect(columns('sessions').filter((c) => c === 'created_by' || c === 'first_message')).toHaveLength(2);
    expect(store.getSession(withMessage)?.firstMessage).toBe('Rotate [REDACTED:secret] for the T3 runner');
    store.appendEvent(withoutMessage, 'user_message', { text: 'now there is one' });
    expect(store.getSession(withoutMessage)?.firstMessage).toBe('now there is one');
  });

  it('filters sessions by q, engine, status and archived state', () => {
    const ids = {
      login: ulid(),
      docs: ulid(),
      archived: ulid(),
      failed: ulid(),
    };
    store.insertSession(sessionRow(ids.login, { title: 'Fix login', engine: 'claude', updatedAt: '2026-09-04T00:00:00.000Z' }));
    store.insertSession(sessionRow(ids.docs, { title: 'Docs', engine: 'codex', firstMessage: 'Please update the T3 LOGIN docs', updatedAt: '2026-09-03T00:00:00.000Z' }));
    store.insertSession(sessionRow(ids.archived, { title: 'Old login work', status: 'archived', archived: true, updatedAt: '2026-09-02T00:00:00.000Z' }));
    store.insertSession(sessionRow(ids.failed, { title: 'Broken', status: 'error', engine: 'codex', updatedAt: '2026-09-01T00:00:00.000Z' }));
    const list = (options: Parameters<Store['listSessions']>[0]): string[] => store.listSessions(options).map((s) => s.id);

    expect(list({})).toEqual([ids.login, ids.docs, ids.failed]);
    expect(list({ includeArchived: true })).toEqual([ids.login, ids.docs, ids.archived, ids.failed]);
    // q matches the title or the first message, case-insensitively; % and _ are literal.
    expect(list({ q: 'LOGIN' })).toEqual([ids.login, ids.docs]);
    expect(list({ q: 'login', includeArchived: true })).toEqual([ids.login, ids.docs, ids.archived]);
    expect(list({ q: '%' })).toEqual([]);
    expect(list({ q: 't3 login' })).toEqual([ids.docs]);
    expect(list({ engine: 'codex' })).toEqual([ids.docs, ids.failed]);
    expect(list({ status: 'error' })).toEqual([ids.failed]);
    expect(list({ status: 'error', engine: 'claude' })).toEqual([]);
    expect(list({ status: 'archived' })).toEqual([ids.archived]);
    expect(list({ status: 'idle', includeArchived: true })).toEqual([ids.login, ids.docs]);
  });

  it('pages newest-first with before + limit', () => {
    const ids = Array.from({ length: 5 }, () => ulid());
    ids.forEach((id, i) => store.insertSession(sessionRow(id, { updatedAt: `2026-09-0${i + 1}T12:00:00.000Z` })));
    const newestFirst = [...ids].reverse();

    const page1 = store.listSessions({ limit: 2 });
    expect(page1.map((s) => s.id)).toEqual(newestFirst.slice(0, 2));
    const page2 = store.listSessions({ limit: 2, before: page1[1]?.updatedAt });
    expect(page2.map((s) => s.id)).toEqual(newestFirst.slice(2, 4));
    const page3 = store.listSessions({ limit: 2, before: page2[1]?.updatedAt });
    expect(page3.map((s) => s.id)).toEqual(newestFirst.slice(4));
    expect(store.listSessions({ limit: 2, before: page3[0]?.updatedAt })).toEqual([]);
    // No limit = every row (internal callers such as the lease-expiry sweep rely on it).
    expect(store.listSessions()).toHaveLength(5);
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
