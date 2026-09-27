import Database from 'better-sqlite3';
import { EventEmitter } from 'node:events';
import {
  appendFileSync,
  closeSync,
  createReadStream,
  existsSync,
  fstatSync,
  mkdirSync,
  openSync,
  readFileSync,
  readSync,
  renameSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import path from 'node:path';
import { createInterface } from 'node:readline';
import type { AgentEvent, Engine, Mode, SessionStatus, ShipState, TurnStatus } from './contract.js';
import { ULID_PATTERN, SYSTEM_SESSION_ID } from './contract.js';
import type { Redactor } from './redact.js';

// Session store (control identity only; lives under /var/lib/oet-agent, 0700).
// - SQLite index: sessions, turns, approvals, ship states, small kv.
// - One JSONL event log per session with a monotonic `seq` starting at 1
//   (CONTRACT.md §4). Every event is redacted before it is written.
// - 90-day retention sweep by last update.

export interface SessionRow {
  id: string;
  title: string;
  engine: Engine;
  model: string;
  effort: string | null;
  mode: Mode;
  status: SessionStatus;
  branch: string;
  worktree: string;
  tainted: boolean;
  archived: boolean;
  createdAt: string;
  updatedAt: string;
  lastSeq: number;
  inputTokens: number;
  outputTokens: number;
  costUsd: number | null;
  resumeId: string | null;
  handoffFrom: string | null;
  seedSummary: string | null;
  prNumber: number | null;
  prUrl: string | null;
  prState: string | null;
}

export type SessionPatch = Partial<Omit<SessionRow, 'id' | 'createdAt' | 'lastSeq'>>;

export interface TurnRow {
  id: string;
  sessionId: string;
  status: 'running' | TurnStatus;
  model: string;
  effort: string | null;
  mode: Mode;
  startedAt: string;
  endedAt: string | null;
}

export interface ApprovalRecord {
  id: string;
  sessionId: string;
  turnId: string | null;
  toolCallId: string;
  source: string;
  summary: string;
  createdAt: string;
  expiresAt: string;
}

export interface StoreOptions {
  dbPath: string;
  sessionsDir: string;
  redactor: Redactor;
  now?: () => Date;
}

type EventListener = (event: AgentEvent) => void;

const SCHEMA = `
CREATE TABLE IF NOT EXISTS sessions (
  id TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  engine TEXT NOT NULL,
  model TEXT NOT NULL,
  effort TEXT,
  mode TEXT NOT NULL,
  status TEXT NOT NULL,
  branch TEXT NOT NULL,
  worktree TEXT NOT NULL,
  tainted INTEGER NOT NULL DEFAULT 0,
  archived INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  last_seq INTEGER NOT NULL DEFAULT 0,
  input_tokens INTEGER NOT NULL DEFAULT 0,
  output_tokens INTEGER NOT NULL DEFAULT 0,
  cost_usd REAL,
  resume_id TEXT,
  handoff_from TEXT,
  seed_summary TEXT,
  pr_number INTEGER,
  pr_url TEXT,
  pr_state TEXT
);
CREATE INDEX IF NOT EXISTS sessions_updated_at ON sessions(updated_at);
CREATE TABLE IF NOT EXISTS turns (
  id TEXT PRIMARY KEY,
  session_id TEXT NOT NULL,
  status TEXT NOT NULL,
  model TEXT NOT NULL,
  effort TEXT,
  mode TEXT NOT NULL,
  started_at TEXT NOT NULL,
  ended_at TEXT
);
CREATE INDEX IF NOT EXISTS turns_session ON turns(session_id);
CREATE INDEX IF NOT EXISTS turns_status ON turns(status);
CREATE TABLE IF NOT EXISTS approvals (
  id TEXT PRIMARY KEY,
  session_id TEXT NOT NULL,
  turn_id TEXT,
  tool_call_id TEXT NOT NULL,
  source TEXT NOT NULL,
  summary TEXT NOT NULL,
  status TEXT NOT NULL,
  decision TEXT,
  resolved_by TEXT,
  created_at TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  resolved_at TEXT
);
CREATE INDEX IF NOT EXISTS approvals_session ON approvals(session_id);
CREATE INDEX IF NOT EXISTS approvals_status ON approvals(status);
CREATE TABLE IF NOT EXISTS ships (
  id TEXT PRIMARY KEY,
  session_id TEXT NOT NULL,
  phase TEXT NOT NULL,
  state TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ships_session ON ships(session_id, created_at);
CREATE TABLE IF NOT EXISTS kv (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
`;

const TERMINAL_SHIP_PHASES = new Set(['done', 'failed']);
const SYSTEM_SEQ_KEY = 'system_queue_last_seq';

interface SessionDbRow {
  id: string;
  title: string;
  engine: string;
  model: string;
  effort: string | null;
  mode: string;
  status: string;
  branch: string;
  worktree: string;
  tainted: number;
  archived: number;
  created_at: string;
  updated_at: string;
  last_seq: number;
  input_tokens: number;
  output_tokens: number;
  cost_usd: number | null;
  resume_id: string | null;
  handoff_from: string | null;
  seed_summary: string | null;
  pr_number: number | null;
  pr_url: string | null;
  pr_state: string | null;
}

const PATCH_COLUMNS: Record<keyof SessionPatch, string> = {
  title: 'title',
  engine: 'engine',
  model: 'model',
  effort: 'effort',
  mode: 'mode',
  status: 'status',
  branch: 'branch',
  worktree: 'worktree',
  tainted: 'tainted',
  archived: 'archived',
  updatedAt: 'updated_at',
  inputTokens: 'input_tokens',
  outputTokens: 'output_tokens',
  costUsd: 'cost_usd',
  resumeId: 'resume_id',
  handoffFrom: 'handoff_from',
  seedSummary: 'seed_summary',
  prNumber: 'pr_number',
  prUrl: 'pr_url',
  prState: 'pr_state',
};

function toSessionRow(row: SessionDbRow): SessionRow {
  return {
    id: row.id,
    title: row.title,
    engine: row.engine as Engine,
    model: row.model,
    effort: row.effort,
    mode: row.mode as Mode,
    status: row.status as SessionStatus,
    branch: row.branch,
    worktree: row.worktree,
    tainted: row.tainted === 1,
    archived: row.archived === 1,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
    lastSeq: row.last_seq,
    inputTokens: row.input_tokens,
    outputTokens: row.output_tokens,
    costUsd: row.cost_usd,
    resumeId: row.resume_id,
    handoffFrom: row.handoff_from,
    seedSummary: row.seed_summary,
    prNumber: row.pr_number,
    prUrl: row.pr_url,
    prState: row.pr_state,
  };
}

function toDbValue(value: unknown): string | number | null {
  if (value === undefined || value === null) return null;
  if (typeof value === 'boolean') return value ? 1 : 0;
  if (typeof value === 'number' || typeof value === 'string') return value;
  return JSON.stringify(value);
}

export class Store {
  private readonly db: Database.Database;
  private readonly sessionsDir: string;
  private readonly redactor: Redactor;
  private readonly now: () => Date;
  private readonly seqs = new Map<string, number>();
  private readonly checkedTail = new Set<string>();
  private readonly emitter = new EventEmitter();

  constructor(options: StoreOptions) {
    this.sessionsDir = options.sessionsDir;
    this.redactor = options.redactor;
    this.now = options.now ?? (() => new Date());
    mkdirSync(this.sessionsDir, { recursive: true, mode: 0o700 });
    if (options.dbPath !== ':memory:') mkdirSync(path.dirname(options.dbPath), { recursive: true, mode: 0o700 });
    this.db = new Database(options.dbPath);
    this.db.pragma('journal_mode = WAL');
    this.db.pragma('synchronous = NORMAL');
    this.db.pragma('foreign_keys = ON');
    this.db.exec(SCHEMA);
    this.emitter.setMaxListeners(0);
  }

  close(): void {
    this.emitter.removeAllListeners();
    this.db.close();
  }

  nowIso(): string {
    return this.now().toISOString();
  }

  // ---------------------------------------------------------------- sessions

  insertSession(row: SessionRow): void {
    this.db
      .prepare(
        `INSERT INTO sessions (id, title, engine, model, effort, mode, status, branch, worktree, tainted, archived,
          created_at, updated_at, last_seq, input_tokens, output_tokens, cost_usd, resume_id, handoff_from,
          seed_summary, pr_number, pr_url, pr_state)
         VALUES (@id, @title, @engine, @model, @effort, @mode, @status, @branch, @worktree, @tainted, @archived,
          @createdAt, @updatedAt, @lastSeq, @inputTokens, @outputTokens, @costUsd, @resumeId, @handoffFrom,
          @seedSummary, @prNumber, @prUrl, @prState)`,
      )
      .run({
        ...row,
        tainted: row.tainted ? 1 : 0,
        archived: row.archived ? 1 : 0,
      });
  }

  getSession(id: string): SessionRow | null {
    const row = this.db.prepare('SELECT * FROM sessions WHERE id = ?').get(id) as SessionDbRow | undefined;
    return row ? toSessionRow(row) : null;
  }

  listSessions(options: { includeArchived?: boolean } = {}): SessionRow[] {
    const rows = (
      options.includeArchived
        ? this.db.prepare('SELECT * FROM sessions ORDER BY updated_at DESC')
        : this.db.prepare('SELECT * FROM sessions WHERE archived = 0 ORDER BY updated_at DESC')
    ).all() as SessionDbRow[];
    return rows.map(toSessionRow);
  }

  updateSession(id: string, patch: SessionPatch): SessionRow | null {
    const sets: string[] = [];
    const values: Record<string, string | number | null> = { id };
    for (const [key, value] of Object.entries(patch) as [keyof SessionPatch, unknown][]) {
      if (value === undefined) continue;
      const column = PATCH_COLUMNS[key];
      if (!column) continue;
      sets.push(`${column} = @${key}`);
      values[key] = toDbValue(value);
    }
    if (patch.updatedAt === undefined) {
      sets.push('updated_at = @updatedAt');
      values.updatedAt = this.nowIso();
    }
    this.db.prepare(`UPDATE sessions SET ${sets.join(', ')} WHERE id = @id`).run(values);
    return this.getSession(id);
  }

  addUsage(id: string, usage: { inputTokens: number; outputTokens: number; costUsd?: number }): void {
    this.db
      .prepare(
        `UPDATE sessions SET input_tokens = input_tokens + @input, output_tokens = output_tokens + @output,
           cost_usd = CASE WHEN @cost IS NULL THEN cost_usd ELSE COALESCE(cost_usd, 0) + @cost END
         WHERE id = @id`,
      )
      .run({
        id,
        input: Math.max(0, Math.trunc(usage.inputTokens)),
        output: Math.max(0, Math.trunc(usage.outputTokens)),
        cost: typeof usage.costUsd === 'number' && Number.isFinite(usage.costUsd) ? usage.costUsd : null,
      });
  }

  /** Other non-archived sessions sharing a worktree (handoff keeps the worktree alive). */
  worktreeUsers(worktree: string, exceptId: string): string[] {
    const rows = this.db
      .prepare('SELECT id FROM sessions WHERE worktree = ? AND id <> ? AND archived = 0')
      .all(worktree, exceptId) as { id: string }[];
    return rows.map((r) => r.id);
  }

  // ------------------------------------------------------------------- turns

  insertTurn(turn: Omit<TurnRow, 'endedAt' | 'status'>): void {
    this.db
      .prepare(
        `INSERT INTO turns (id, session_id, status, model, effort, mode, started_at)
         VALUES (@id, @sessionId, 'running', @model, @effort, @mode, @startedAt)`,
      )
      .run(turn);
  }

  finishTurn(id: string, status: TurnStatus): void {
    this.db.prepare('UPDATE turns SET status = ?, ended_at = ? WHERE id = ?').run(status, this.nowIso(), id);
  }

  getTurn(id: string): TurnRow | null {
    const row = this.db.prepare('SELECT * FROM turns WHERE id = ?').get(id) as
      | { id: string; session_id: string; status: string; model: string; effort: string | null; mode: string; started_at: string; ended_at: string | null }
      | undefined;
    if (!row) return null;
    return {
      id: row.id,
      sessionId: row.session_id,
      status: row.status as TurnRow['status'],
      model: row.model,
      effort: row.effort,
      mode: row.mode as Mode,
      startedAt: row.started_at,
      endedAt: row.ended_at,
    };
  }

  /**
   * Boot recovery: turns still `running` belong to a process that died.
   * Marks them `interrupted` (resumable) and returns them.
   */
  interruptInFlightTurns(): { turnId: string; sessionId: string; startedAt: string }[] {
    const rows = this.db
      .prepare(`SELECT id, session_id, started_at FROM turns WHERE status = 'running'`)
      .all() as { id: string; session_id: string; started_at: string }[];
    const now = this.nowIso();
    const tx = this.db.transaction(() => {
      this.db.prepare(`UPDATE turns SET status = 'interrupted', ended_at = ? WHERE status = 'running'`).run(now);
      this.db
        .prepare(
          `UPDATE sessions SET status = 'interrupted', updated_at = ?
           WHERE archived = 0 AND status IN ('running', 'awaiting_approval')`,
        )
        .run(now);
    });
    tx();
    return rows.map((r) => ({ turnId: r.id, sessionId: r.session_id, startedAt: r.started_at }));
  }

  // --------------------------------------------------------------- approvals

  recordApproval(record: ApprovalRecord): void {
    this.db
      .prepare(
        `INSERT OR REPLACE INTO approvals (id, session_id, turn_id, tool_call_id, source, summary, status, created_at, expires_at)
         VALUES (@id, @sessionId, @turnId, @toolCallId, @source, @summary, 'pending', @createdAt, @expiresAt)`,
      )
      .run({ ...record, summary: this.redactor.redact(record.summary) });
  }

  resolveApprovalRecord(id: string, decision: string, by: string): void {
    this.db
      .prepare(
        `UPDATE approvals SET status = 'resolved', decision = ?, resolved_by = ?, resolved_at = ?
         WHERE id = ? AND status = 'pending'`,
      )
      .run(decision, by, this.nowIso(), id);
  }

  /** Boot recovery: pending approvals died with the process (their nonces lived in memory). */
  expireOpenApprovals(): { id: string; sessionId: string }[] {
    const rows = this.db
      .prepare(`SELECT id, session_id FROM approvals WHERE status = 'pending'`)
      .all() as { id: string; session_id: string }[];
    this.db
      .prepare(
        `UPDATE approvals SET status = 'resolved', decision = 'deny', resolved_by = 'timeout', resolved_at = ?
         WHERE status = 'pending'`,
      )
      .run(this.nowIso());
    return rows.map((r) => ({ id: r.id, sessionId: r.session_id }));
  }

  // ------------------------------------------------------------------- ships

  saveShip(state: ShipState): void {
    this.db
      .prepare(
        `INSERT INTO ships (id, session_id, phase, state, created_at, updated_at)
         VALUES (@id, @sessionId, @phase, @state, @createdAt, @updatedAt)
         ON CONFLICT(id) DO UPDATE SET phase = excluded.phase, state = excluded.state, updated_at = excluded.updated_at`,
      )
      .run({
        id: state.shipId,
        sessionId: state.sessionId,
        phase: state.phase,
        state: JSON.stringify(state),
        createdAt: state.startedAt,
        updatedAt: state.updatedAt,
      });
  }

  getLatestShip(sessionId: string): ShipState | null {
    const row = this.db
      .prepare('SELECT state FROM ships WHERE session_id = ? ORDER BY created_at DESC, id DESC LIMIT 1')
      .get(sessionId) as { state: string } | undefined;
    return row ? (JSON.parse(row.state) as ShipState) : null;
  }

  listUnfinishedShips(): ShipState[] {
    const rows = this.db.prepare('SELECT state, phase FROM ships ORDER BY created_at ASC').all() as {
      state: string;
      phase: string;
    }[];
    return rows.filter((r) => !TERMINAL_SHIP_PHASES.has(r.phase)).map((r) => JSON.parse(r.state) as ShipState);
  }

  // ---------------------------------------------------------------------- kv

  getKv<T>(key: string): T | null {
    const row = this.db.prepare('SELECT value FROM kv WHERE key = ?').get(key) as { value: string } | undefined;
    return row ? (JSON.parse(row.value) as T) : null;
  }

  setKv(key: string, value: unknown): void {
    this.db
      .prepare(
        `INSERT INTO kv (key, value, updated_at) VALUES (?, ?, ?)
         ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at`,
      )
      .run(key, JSON.stringify(value), this.nowIso());
  }

  deleteKv(key: string): void {
    this.db.prepare('DELETE FROM kv WHERE key = ?').run(key);
  }

  // ------------------------------------------------------------------ events

  private sessionDir(sessionId: string): string {
    if (!ULID_PATTERN.test(sessionId)) throw new Error('Invalid session id.');
    return path.join(this.sessionsDir, sessionId);
  }

  eventsFile(sessionId: string): string {
    return path.join(this.sessionDir(sessionId), 'events.jsonl');
  }

  /** Highest persisted seq for a session (0 when none). */
  lastSeq(sessionId: string): number {
    const cached = this.seqs.get(sessionId);
    if (cached !== undefined) return cached;
    const fromFile = readTailSeq(this.eventsFile(sessionId));
    const row = this.db.prepare('SELECT last_seq FROM sessions WHERE id = ?').get(sessionId) as
      | { last_seq: number }
      | undefined;
    const systemSeq = sessionId === SYSTEM_SESSION_ID ? (this.getKv<number>(SYSTEM_SEQ_KEY) ?? 0) : 0;
    const seq = Math.max(fromFile, row?.last_seq ?? 0, systemSeq);
    this.seqs.set(sessionId, seq);
    return seq;
  }

  /**
   * Redacts, assigns the next seq, appends one JSON line and notifies live
   * subscribers. Synchronous so seq order equals file order.
   */
  appendEvent(sessionId: string, type: string, data: Record<string, unknown>, turnId?: string): AgentEvent {
    const file = this.eventsFile(sessionId);
    const seq = this.lastSeq(sessionId) + 1;
    const event: AgentEvent = {
      seq,
      sessionId,
      ...(turnId ? { turnId } : {}),
      ts: this.nowIso(),
      type,
      data: this.redactor.redactDeep(data),
    };
    mkdirSync(path.dirname(file), { recursive: true, mode: 0o700 });
    if (!this.checkedTail.has(sessionId)) {
      ensureTrailingNewline(file);
      this.checkedTail.add(sessionId);
    }
    appendFileSync(file, `${JSON.stringify(event)}\n`, { mode: 0o600 });
    this.seqs.set(sessionId, seq);
    if (sessionId === SYSTEM_SESSION_ID) this.setKv(SYSTEM_SEQ_KEY, seq);
    else this.db.prepare('UPDATE sessions SET last_seq = ?, updated_at = ? WHERE id = ?').run(seq, event.ts, sessionId);
    this.emitter.emit(sessionId, event);
    return event;
  }

  /** Persisted events with seq > afterSeq, in order. */
  async *iterEvents(sessionId: string, afterSeq = 0): AsyncGenerator<AgentEvent> {
    const file = this.eventsFile(sessionId);
    if (!existsSync(file)) return;
    const stream = createReadStream(file, { encoding: 'utf8' });
    const lines = createInterface({ input: stream, crlfDelay: Infinity });
    try {
      for await (const line of lines) {
        const event = parseEventLine(line);
        if (event && event.seq > afterSeq) yield event;
      }
    } finally {
      lines.close();
      stream.destroy();
    }
  }

  async readEvents(sessionId: string, afterSeq = 0, limit = Number.POSITIVE_INFINITY): Promise<AgentEvent[]> {
    const out: AgentEvent[] = [];
    for await (const event of this.iterEvents(sessionId, afterSeq)) {
      out.push(event);
      if (out.length >= limit) break;
    }
    return out;
  }

  /** Last `count` persisted events (used for handoff summaries). */
  async tailEvents(sessionId: string, count: number): Promise<AgentEvent[]> {
    const ring: AgentEvent[] = [];
    for await (const event of this.iterEvents(sessionId, 0)) {
      ring.push(event);
      if (ring.length > count) ring.shift();
    }
    return ring;
  }

  subscribe(sessionId: string, listener: EventListener): () => void {
    this.emitter.on(sessionId, listener);
    return () => this.emitter.off(sessionId, listener);
  }

  // --------------------------------------------------------------- retention

  deleteSession(sessionId: string): void {
    const tx = this.db.transaction(() => {
      this.db.prepare('DELETE FROM turns WHERE session_id = ?').run(sessionId);
      this.db.prepare('DELETE FROM approvals WHERE session_id = ?').run(sessionId);
      this.db.prepare('DELETE FROM ships WHERE session_id = ?').run(sessionId);
      this.db.prepare('DELETE FROM sessions WHERE id = ?').run(sessionId);
    });
    tx();
    rmSync(this.sessionDir(sessionId), { recursive: true, force: true });
    this.seqs.delete(sessionId);
    this.checkedTail.delete(sessionId);
  }

  /**
   * Deletes sessions whose last update is older than `retentionDays` (never a
   * running one) and prunes old lines of the system queue log.
   * Returns the removed sessions so the caller can drop their worktrees.
   */
  sweep(retentionDays: number): { id: string; worktree: string }[] {
    const cutoff = new Date(this.now().getTime() - retentionDays * 86_400_000).toISOString();
    const rows = this.db
      .prepare(
        `SELECT id, worktree FROM sessions
         WHERE updated_at < ? AND status NOT IN ('running', 'awaiting_approval')`,
      )
      .all(cutoff) as { id: string; worktree: string }[];
    for (const row of rows) this.deleteSession(row.id);
    this.db.prepare(`DELETE FROM approvals WHERE status <> 'pending' AND created_at < ?`).run(cutoff);
    this.pruneEventLines(SYSTEM_SESSION_ID, cutoff);
    return rows;
  }

  private pruneEventLines(sessionId: string, cutoffIso: string): void {
    const file = this.eventsFile(sessionId);
    if (!existsSync(file)) return;
    const lines = readFileSync(file, 'utf8').split('\n');
    const kept = lines.filter((line) => {
      const event = parseEventLine(line);
      return event !== null && event.ts >= cutoffIso;
    });
    if (kept.length === lines.filter((l) => l.trim()).length) return;
    // Keep the highest seq monotonic even when every line is pruned.
    const last = this.lastSeq(sessionId);
    const tmp = `${file}.tmp`;
    writeFileSync(tmp, kept.length ? `${kept.join('\n')}\n` : '', { mode: 0o600 });
    renameSync(tmp, file);
    this.seqs.set(sessionId, last);
  }
}

function parseEventLine(line: string): AgentEvent | null {
  const trimmed = line.trim();
  if (!trimmed) return null;
  try {
    const parsed = JSON.parse(trimmed) as Partial<AgentEvent>;
    if (typeof parsed.seq !== 'number' || typeof parsed.type !== 'string') return null;
    return parsed as AgentEvent;
  } catch {
    return null;
  }
}

/** Reads the seq of the last complete line, growing the read window as needed. */
function readTailSeq(file: string): number {
  if (!existsSync(file)) return 0;
  const fd = openSync(file, 'r');
  try {
    const size = fstatSync(fd).size;
    if (size === 0) return 0;
    let window = 64 * 1024;
    for (;;) {
      const length = Math.min(window, size);
      const buffer = Buffer.alloc(length);
      readSync(fd, buffer, 0, length, size - length);
      const lines = buffer.toString('utf8').split('\n');
      // The first line of a partial window may be cut; skip it unless we read the whole file.
      const candidates = length < size ? lines.slice(1) : lines;
      for (let i = candidates.length - 1; i >= 0; i -= 1) {
        const event = parseEventLine(candidates[i] ?? '');
        if (event) return event.seq;
      }
      if (length >= size) return 0;
      window *= 4;
    }
  } finally {
    closeSync(fd);
  }
}

/** A crash mid-append can leave a partial last line; start the next event on a fresh line. */
function ensureTrailingNewline(file: string): void {
  if (!existsSync(file)) return;
  const fd = openSync(file, 'r');
  try {
    const size = fstatSync(fd).size;
    if (size === 0) return;
    const last = Buffer.alloc(1);
    readSync(fd, last, 0, 1, size - 1);
    if (last[0] === 0x0a) return;
  } finally {
    closeSync(fd);
  }
  appendFileSync(file, '\n');
}
