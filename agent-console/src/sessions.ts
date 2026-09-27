import { ulid } from 'ulid';
import type { ApprovalRegistry, ProxySessionView } from './approvals.js';
import type { AppConfig } from './config.js';
import type {
  AgentEvent,
  CreateSession,
  Engine,
  Mode,
  ResolvedBy,
  SessionDetail,
  SessionDiff,
  SessionStatus,
  SessionSummary,
  TurnStatus,
} from './contract.js';
import { APPROVAL_DECISIONS, ENGINES, MODES, SESSION_STATUSES, SYSTEM_SESSION_ID } from './contract.js';
import type {
  EngineAdapter,
  EngineEvent,
  EngineHooks,
  EngineSession,
  EngineStatus,
  SessionEngineOptions,
  ToolCallRequest,
  ToolDecision,
} from './engines/types.js';
import { buildAgentEnv, ensureDockerConfig } from './env.js';
import { HttpError, badRequest, conflict, describeError, locked, notFound, tooManyTurns } from './errors.js';
import { applyTaint, classifyToolCall, decide, type Classification } from './guard.js';
import type { ControlState, LeaseManager } from './lease.js';
import type { Logger } from './log.js';
import type { SnapshotResult } from './snapshot.js';
import type { ListSessionsOptions, SessionRow, Store } from './store.js';
import { asObject, optBoolean, optEnum, optOpaqueId, optString, reqEnum, reqOpaqueId, reqString } from './validate.js';
import type { WorkspaceApi } from './workspace.js';

// Session lifecycle (plan "src/sessions.ts", control identity):
// create → worktree → engine openSession; runTurn with the Guard in
// hooks.onToolCall (classification, owner approvals, pre-snapshots, taint);
// concurrency cap (429); idle close after 10 min; boot recovery marks
// in-flight turns interrupted; handoff to another engine in the same worktree.

export interface EngineSource {
  get(engine: Engine): Promise<EngineAdapter>;
  status(engine: Engine, fresh?: boolean): Promise<EngineStatus>;
  invalidate(engine: Engine): void;
}

export interface SnapshotTaker {
  take(request: { label: string; tables?: string[] }): Promise<SnapshotResult>;
}

export type SessionConfig = Pick<
  AppConfig,
  | 'maxConcurrentTurns'
  | 'idleCloseMs'
  | 'leasePauseMaxMs'
  | 'agentUid'
  | 'retentionDays'
  | 'handoffEventCount'
  | 'agentHome'
  | 'claudeConfigDir'
  | 'codexHome'
  | 'egressProxyUrl'
  | 'noProxy'
  | 'dockerHost'
  | 'dockerConfigRoot'
  | 'agentDatabaseUrl'
>;

export interface SessionManagerDeps {
  config: SessionConfig;
  store: Store;
  approvals: ApprovalRegistry;
  engines: EngineSource;
  workspace: WorkspaceApi;
  snapshots: SnapshotTaker;
  lease: LeaseManager;
  control: ControlState;
  logger: Logger;
  /** etc/MANUAL.md contents (engine system prompt). */
  readManual?: () => Promise<string>;
  /** Per-session DOCKER_CONFIG writer (default: src/env.ts ensureDockerConfig). */
  prepareDockerConfig?: (sessionId: string) => Promise<unknown>;
  /** Drops the proxies' cached approve-for-session grants (src/proxies.ts). */
  revokeProxyGrants?: (sessionId: string) => Promise<void>;
  /** Retention of engine-native transcripts (src/retention.ts); returns files removed. */
  pruneEngineTranscripts?: (days: number) => Promise<number>;
  /** Erasure of the engine-native transcripts of these engine session ids (src/retention.ts). */
  removeEngineTranscripts?: (engineSessionIds: string[]) => Promise<number>;
  now?: () => number;
}

interface ActiveTurn {
  turnId: string;
  abort: AbortController;
  startedAt: number;
  done: Promise<void>;
  classifications: Map<string, Classification>;
  pendingApprovals: number;
  abortedBy: ResolvedBy | null;
}

interface Live {
  engineSession: EngineSession | null;
  opening: Promise<EngineSession> | null;
  idleTimer: NodeJS.Timeout | null;
  turn: ActiveTurn | null;
  grants: Set<string>;
  tainted: boolean;
  lastSnapshotAt: number;
}

const MAX_MESSAGE = 200_000;
const MAX_TOOL_OUTPUT = 64 * 1024;
const MAX_SYSTEM_PROMPT = 256 * 1024;
const PROXY_SNAPSHOT_REUSE_MS = 120_000;

/** GET /v1/sessions paging (CONTRACT.md §3). No `limit` → the max, so the plain list is unchanged. */
export const SESSION_LIST_MAX_LIMIT = 200;
export const SESSION_LIST_DEFAULT_LIMIT = 200;
export const SESSION_LIST_MAX_QUERY = 100;
const ISO_TIMESTAMP = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,9})?)?(Z|[+-]\d{2}:\d{2})$/;

/**
 * Validates the GET /v1/sessions query string (400 `bad_request` on bad values).
 * Unknown keys are ignored. Every filter is optional.
 */
export function parseSessionListQuery(query: unknown): ListSessionsOptions {
  const raw = query && typeof query === 'object' ? (query as Record<string, unknown>) : {};
  const single = (key: string): string | undefined => {
    const value = raw[key];
    if (value === undefined || value === null) return undefined;
    if (typeof value !== 'string') throw badRequest('bad_request', `${key} must be given once.`);
    return value;
  };
  const options: ListSessionsOptions = { limit: SESSION_LIST_DEFAULT_LIMIT };

  const q = single('q')?.trim();
  if (q !== undefined && q !== '') {
    if (q.length > SESSION_LIST_MAX_QUERY) throw badRequest('bad_request', `q is longer than ${SESSION_LIST_MAX_QUERY} characters.`);
    if (/[\u0000-\u001f\u007f]/.test(q)) throw badRequest('bad_request', 'q contains control characters.');
    options.q = q;
  }

  const engine = single('engine');
  if (engine !== undefined && engine !== '') {
    if (!(ENGINES as readonly string[]).includes(engine)) throw badRequest('bad_request', `engine must be one of: ${ENGINES.join(', ')}.`);
    options.engine = engine as Engine;
  }

  const status = single('status');
  if (status !== undefined && status !== '') {
    if (!(SESSION_STATUSES as readonly string[]).includes(status)) {
      throw badRequest('bad_request', `status must be one of: ${SESSION_STATUSES.join(', ')}.`);
    }
    options.status = status as SessionStatus;
  }

  const includeArchived = single('includeArchived');
  if (includeArchived !== undefined && includeArchived !== '') {
    const normalized = includeArchived.toLowerCase();
    if (normalized !== 'true' && normalized !== 'false') throw badRequest('bad_request', 'includeArchived must be true or false.');
    options.includeArchived = normalized === 'true';
  }

  const before = single('before');
  if (before !== undefined && before !== '') {
    const ms = Date.parse(before);
    if (before.length > 40 || !ISO_TIMESTAMP.test(before) || Number.isNaN(ms)) {
      throw badRequest('bad_request', 'before must be an ISO-8601 timestamp (the last updatedAt of the previous page).');
    }
    // Stored timestamps are toISOString() values, so compare in that exact form.
    options.before = new Date(ms).toISOString();
  }

  const limit = single('limit');
  if (limit !== undefined && limit !== '') {
    const n = /^\d{1,4}$/.test(limit) ? Number(limit) : Number.NaN;
    if (!Number.isInteger(n) || n < 1 || n > SESSION_LIST_MAX_LIMIT) {
      throw badRequest('bad_request', `limit must be an integer from 1 to ${SESSION_LIST_MAX_LIMIT}.`);
    }
    options.limit = n;
  }
  return options;
}

const TAINT_REASONS: Record<string, string> = {
  db_read: 'read application rows from the database (may contain learner-authored content)',
  docker_logs: 'read container logs',
  web: 'fetched web content',
  github_comments: 'read GitHub issue / pull request content',
  ci_logs: 'read CI logs',
};

function turnStatusToSession(status: TurnStatus): SessionStatus {
  if (status === 'interrupted') return 'interrupted';
  if (status === 'error') return 'error';
  return 'idle';
}

function clipText(text: string, max: number): string {
  return text.length > max ? `${text.slice(0, max)}\n…[truncated ${text.length - max} chars]` : text;
}

export function summarizeToolCall(req: ToolCallRequest): string {
  if (req.command) return `${req.name}: ${req.command.split('\n')[0]?.slice(0, 200) ?? ''}`;
  if (req.writePaths && req.writePaths.length > 0) return `${req.name}: ${req.writePaths.slice(0, 5).join(', ')}`;
  let input = '';
  try {
    input = JSON.stringify(req.input);
  } catch {
    input = '';
  }
  return `${req.name}${input ? ` ${input.slice(0, 200)}` : ''}`;
}

/** Plain-text summary of recent events used to seed a handoff session. */
export function buildHandoffSummary(
  source: Pick<SessionRow, 'id' | 'engine' | 'model' | 'branch' | 'worktree'>,
  events: readonly AgentEvent[],
  maxChars = 12_000,
): string {
  const lines: string[] = [];
  for (const event of events) {
    const d = event.data;
    switch (event.type) {
      case 'user_message':
        lines.push(`- Owner: ${clipText(String(d.text ?? ''), 2000)}`);
        break;
      case 'text':
        lines.push(`- Assistant: ${clipText(String(d.text ?? ''), 1500)}`);
        break;
      case 'tool_call':
        lines.push(`- Tool ${String(d.name ?? '?')}: ${clipText(String(d.command ?? JSON.stringify(d.input ?? {})), 300)}`);
        break;
      case 'tool_result':
        lines.push(`  → ${d.ok ? 'ok' : 'failed'}${typeof d.exitCode === 'number' ? ` (exit ${d.exitCode})` : ''}: ${clipText(String(d.output ?? ''), 300)}`);
        break;
      case 'file_change':
        lines.push(`- File ${String(d.changeKind ?? 'change')}: ${String(d.path ?? '')}`);
        break;
      case 'approval_resolved':
        lines.push(`- Approval ${String(d.decision ?? '')} by ${String(d.by ?? '')}`);
        break;
      case 'snapshot':
        lines.push(`- Snapshot ${String(d.label ?? '')}: ${d.ok ? 'ok' : `failed (${String(d.error ?? '')})`}`);
        break;
      case 'taint':
        lines.push(`- Session tainted: ${String(d.reason ?? '')}`);
        break;
      case 'turn_complete':
        lines.push(`- Turn ended: ${String(d.status ?? '')}`);
        break;
      default:
        break;
    }
  }
  const header = [
    `Previous session ${source.id} (${source.engine}, model ${source.model}) worked on branch ${source.branch} in ${source.worktree}.`,
    'Continue from its state; the worktree already contains its changes. Recent activity, oldest first:',
  ].join('\n');
  let body = lines.join('\n');
  const budget = Math.max(0, maxChars - header.length - 1);
  if (body.length > budget) body = `…\n${body.slice(body.length - budget + 2)}`;
  return `${header}\n${body}`;
}

export class SessionManager {
  private readonly live = new Map<string, Live>();
  private readonly now: () => number;
  private manualCache: string | null = null;

  constructor(private readonly deps: SessionManagerDeps) {
    this.now = deps.now ?? Date.now;
  }

  // ---------------------------------------------------------------- queries

  /** `true`/`false` keeps the original call shape; an options object applies the GET /v1/sessions filters. */
  list(options: boolean | ListSessionsOptions = false): SessionSummary[] {
    const filters: ListSessionsOptions = typeof options === 'boolean' ? { includeArchived: options } : options;
    return this.deps.store.listSessions(filters).map((row) => this.toSummary(row));
  }

  get(id: string): SessionDetail {
    return this.toDetail(this.requireSession(id));
  }

  activeTurnCount(): number {
    let count = 0;
    for (const live of this.live.values()) if (live.turn) count += 1;
    return count;
  }

  exists(id: string): boolean {
    return id === SYSTEM_SESSION_ID || this.deps.store.getSession(id) !== null;
  }

  async diff(id: string): Promise<SessionDiff> {
    const row = this.requireSession(id);
    if (row.archived) throw conflict('session_archived', 'The session is archived; its worktree was removed.');
    return this.deps.workspace.diff({ path: row.worktree, branch: row.branch });
  }

  // -------------------------------------------------------------- mutations

  /** `createdBy` is the (already allow-listed) X-Oet-Owner-Account of the request. */
  async create(body: unknown, createdBy?: string): Promise<SessionDetail> {
    const input = this.parseCreate(body);
    this.assertOperational();
    if ((input.mode === 'autopilot' || input.initialMessage) && !this.deps.lease.isActive()) {
      throw locked('lease_expired', 'The owner lease has lapsed; reopen the console to start work.');
    }
    if (input.initialMessage && this.activeTurnCount() >= this.deps.config.maxConcurrentTurns) {
      throw tooManyTurns(`At most ${this.deps.config.maxConcurrentTurns} turns may run at once.`);
    }
    await this.validateModel(input.engine, input.model, input.effort);
    const disk = await this.deps.workspace.checkDisk();
    if (!disk.ok) {
      throw conflict('disk_low', `Workspace disk is low (${Math.round(disk.freeBytes / 1024 / 1024)} MiB free); archive old sessions first.`);
    }

    const id = ulid();
    const title = (input.title ?? input.initialMessage?.split('\n')[0] ?? `${input.engine} session`).trim().slice(0, 120) || `${input.engine} session`;
    let worktree: { path: string; branch: string };
    try {
      worktree = await this.deps.workspace.createWorktree(id, title);
    } catch (error) {
      this.deps.logger.error({ err: describeError(error), sessionId: id }, 'worktree creation failed');
      throw new HttpError(500, 'workspace_error', `Could not prepare the session worktree: ${describeError(error).slice(0, 300)}`);
    }
    const nowIso = new Date(this.now()).toISOString();
    this.deps.store.insertSession({
      id,
      title,
      engine: input.engine,
      model: input.model,
      effort: input.effort ?? null,
      mode: input.mode,
      status: 'idle',
      branch: worktree.branch,
      worktree: worktree.path,
      tainted: false,
      archived: false,
      createdAt: nowIso,
      updatedAt: nowIso,
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
      createdBy: createdBy?.trim().toLowerCase() || null,
      firstMessage: null,
    });
    if (input.initialMessage) await this.sendMessage(id, { text: input.initialMessage });
    return this.get(id);
  }

  async patch(id: string, body: unknown): Promise<SessionDetail> {
    const obj = asObject(body, false);
    const title = optString(obj, 'title', 200);
    const mode = optEnum(obj, 'mode', MODES);
    const model = optOpaqueId(obj, 'model');
    const effort = optOpaqueId(obj, 'effort');
    const archived = optBoolean(obj, 'archived');
    let row = this.requireSession(id);
    const live = this.getLive(row);

    if (archived === false && row.archived) {
      await this.deps.workspace.restoreWorktree({ path: row.worktree, branch: row.branch });
      row = this.deps.store.updateSession(id, { archived: false, status: 'idle' }) ?? row;
    } else if (row.archived && (title !== undefined || mode !== undefined || model !== undefined || effort !== undefined || archived === true)) {
      throw conflict('session_archived', 'Unarchive the session first.');
    }

    if (mode !== undefined && mode !== row.mode) {
      if (mode === 'autopilot') {
        this.assertOperational();
        if (!this.deps.lease.isActive()) throw locked('lease_expired', 'Autopilot needs an active owner lease.');
      }
      row = this.deps.store.updateSession(id, { mode }) ?? row;
      this.emit(id, 'mode_changed', { mode, reason: 'owner' }, live.turn?.turnId);
    }
    if (model !== undefined || effort !== undefined) {
      const nextModel = model ?? row.model;
      const nextEffort = effort ?? (model !== undefined ? undefined : row.effort ?? undefined);
      await this.validateModel(row.engine, nextModel, nextEffort);
      row = this.deps.store.updateSession(id, { model: nextModel, effort: nextEffort ?? null }) ?? row;
    }
    if (title !== undefined) row = this.deps.store.updateSession(id, { title: title.trim() }) ?? row;

    if (archived === true && !row.archived) {
      if (live.turn) throw conflict('turn_in_progress', 'Interrupt the running turn before archiving.');
      await this.archive(row, live);
      row = this.requireSession(id);
    }
    return this.toDetail(row);
  }

  async sendMessage(id: string, body: unknown): Promise<{ turnId: string }> {
    const obj = asObject(body, false);
    const text = reqString(obj, 'text', MAX_MESSAGE);
    const model = optOpaqueId(obj, 'model');
    const effort = optOpaqueId(obj, 'effort');
    this.assertCanStartTurn();
    let row = this.requireSession(id);
    if (row.archived) throw conflict('session_archived', 'The session is archived.');
    const live = this.getLive(row);
    if (live.turn) throw conflict('turn_in_progress', 'A turn is already running in this session.');
    if (model !== undefined || effort !== undefined) {
      const nextModel = model ?? row.model;
      const nextEffort = effort ?? (model !== undefined ? undefined : row.effort ?? undefined);
      await this.validateModel(row.engine, nextModel, nextEffort);
      row = this.deps.store.updateSession(id, { model: nextModel, effort: nextEffort ?? null }) ?? row;
    }
    // Synchronous check-and-reserve (no await between) so the cap cannot be raced.
    this.assertCanStartTurn();
    if (live.turn) throw conflict('turn_in_progress', 'A turn is already running in this session.');
    if (this.activeTurnCount() >= this.deps.config.maxConcurrentTurns) {
      throw tooManyTurns(`At most ${this.deps.config.maxConcurrentTurns} turns may run at once.`);
    }
    const turnId = ulid();
    const turn: ActiveTurn = {
      turnId,
      abort: new AbortController(),
      startedAt: this.now(),
      done: Promise.resolve(),
      classifications: new Map(),
      pendingApprovals: 0,
      abortedBy: null,
    };
    live.turn = turn;
    this.clearIdle(live);

    this.deps.store.insertTurn({
      id: turnId,
      sessionId: id,
      model: row.model,
      effort: row.effort,
      mode: row.mode,
      startedAt: new Date(turn.startedAt).toISOString(),
    });
    this.deps.store.updateSession(id, { status: 'running' });
    this.emit(id, 'turn_started', { model: row.model, ...(row.effort ? { effort: row.effort } : {}), mode: row.mode }, turnId);
    this.emit(id, 'user_message', { text }, turnId);
    turn.done = this.runTurn(id, live, turn, text).catch((error: unknown) => {
      this.deps.logger.error({ err: describeError(error), sessionId: id, turnId }, 'turn runner crashed');
    });
    return { turnId };
  }

  async interrupt(id: string): Promise<{ ok: true }> {
    const row = this.requireSession(id);
    const live = this.getLive(row);
    const turn = live.turn;
    if (!turn) return { ok: true };
    this.abortTurn(live, turn, 'owner');
    return { ok: true };
  }

  async handoff(id: string, body: unknown): Promise<SessionDetail> {
    const obj = asObject(body, false);
    const engine = reqEnum(obj, 'engine', ENGINES);
    const model = reqOpaqueId(obj, 'model');
    const effort = optOpaqueId(obj, 'effort');
    this.assertOperational();
    const source = this.requireSession(id);
    if (source.archived) throw conflict('session_archived', 'The session is archived.');
    const sourceLive = this.getLive(source);
    if (sourceLive.turn) throw conflict('turn_in_progress', 'Interrupt the running turn before handing off.');
    await this.validateModel(engine, model, effort);

    const events = await this.deps.store.tailEvents(id, this.deps.config.handoffEventCount);
    const summary = buildHandoffSummary(source, events);
    const newId = ulid();
    const nowIso = new Date(this.now()).toISOString();
    const mode: Mode = source.mode === 'autopilot' && !this.deps.lease.isActive() ? 'guarded' : source.mode;
    this.deps.store.insertSession({
      id: newId,
      title: source.title,
      engine,
      model,
      effort: effort ?? null,
      mode,
      status: 'idle',
      branch: source.branch,
      worktree: source.worktree,
      tainted: source.tainted,
      archived: false,
      createdAt: nowIso,
      updatedAt: nowIso,
      lastSeq: 0,
      inputTokens: 0,
      outputTokens: 0,
      costUsd: null,
      resumeId: null,
      handoffFrom: id,
      seedSummary: summary,
      prNumber: source.prNumber,
      prUrl: source.prUrl,
      prState: source.prState,
      // Same conversation continued on another engine: keep who started it and what it was about.
      createdBy: source.createdBy ?? null,
      firstMessage: source.firstMessage ?? null,
    });
    this.emit(newId, 'text', { messageId: 'handoff', text: `Handed off from session ${id} (${source.engine}).\n\n${summary}` });
    if (source.tainted) this.emit(newId, 'taint', { reason: 'inherited from the handed-off session', source: 'handoff' });
    this.emit(id, 'text', { messageId: 'handoff', text: `Handed off to session ${newId} (${engine}, model ${model}).` });
    // The source keeps its transcript but gives up the worktree to the new session.
    await this.closeEngine(sourceLive);
    this.deps.approvals.cancelSession(id, 'owner');
    this.deps.store.updateSession(id, { archived: true, status: 'archived' });
    sourceLive.grants.clear();
    this.revokeProxyGrants(id);
    return this.get(newId);
  }

  resolveApproval(sessionId: string, approvalId: string, body: unknown): { ok: true } {
    if (sessionId !== SYSTEM_SESSION_ID) this.requireSession(sessionId);
    const obj = asObject(body, false);
    const decision = reqEnum(obj, 'decision', APPROVAL_DECISIONS);
    const nonce = reqString(obj, 'nonce', 200);
    const note = optString(obj, 'note', 1000, { allowEmpty: true });
    this.deps.approvals.resolveByOwner(sessionId, approvalId, decision, nonce, note);
    return { ok: true };
  }

  /** Lease lapsed: every Autopilot session drops to Guarded. */
  onLeaseExpired(): void {
    for (const row of this.deps.store.listSessions()) {
      if (row.mode !== 'autopilot') continue;
      this.deps.store.updateSession(row.id, { mode: 'guarded' });
      this.emit(row.id, 'mode_changed', { mode: 'guarded', reason: 'lease_expired' }, this.live.get(row.id)?.turn?.turnId);
    }
  }

  /** Kill switch: abort every running turn and drop engine sessions. */
  async abortAllTurns(): Promise<number> {
    const turns: Promise<void>[] = [];
    let count = 0;
    for (const live of this.live.values()) {
      if (live.turn) {
        count += 1;
        turns.push(live.turn.done);
        this.abortTurn(live, live.turn, 'kill');
      }
    }
    await Promise.race([Promise.allSettled(turns), new Promise((resolve) => setTimeout(resolve, 5_000).unref())]);
    await Promise.allSettled([...this.live.values()].map((live) => this.closeEngine(live)));
    return count;
  }

  /** Boot recovery: in-flight turns → interrupted (resumable); pending approvals → expired. */
  boot(): void {
    const now = this.now();
    for (const turn of this.deps.store.interruptInFlightTurns()) {
      this.emit(turn.sessionId, 'turn_complete', { status: 'interrupted', durationMs: Math.max(0, now - Date.parse(turn.startedAt)) }, turn.turnId);
    }
    for (const approval of this.deps.store.expireOpenApprovals()) {
      this.emit(approval.sessionId, 'approval_resolved', { approvalId: approval.id, decision: 'deny', by: 'timeout' });
    }
    // A fresh process has no owner lease yet: Autopilot never survives a restart.
    if (!this.deps.lease.isActive()) this.onLeaseExpired();
  }

  /** 90-day retention: removes old transcripts and their worktrees. */
  async sweep(): Promise<number> {
    const removed = this.deps.store.sweep(this.deps.config.retentionDays);
    for (const session of removed) {
      const live = this.live.get(session.id);
      if (live) {
        await this.closeEngine(live);
        this.live.delete(session.id);
      }
      if (this.deps.store.worktreeUsers(session.worktree, session.id).length === 0) {
        await this.deps.workspace.removeWorktree(session.worktree).catch((error: unknown) => {
          this.deps.logger.warn({ err: describeError(error), sessionId: session.id }, 'retention: worktree removal failed');
        });
      }
    }
    // Same window for the engines' own transcripts (runbook §11.1).
    if (this.deps.pruneEngineTranscripts) {
      const files = await this.deps.pruneEngineTranscripts(this.deps.config.retentionDays).catch(() => 0);
      if (files > 0) this.deps.logger.info({ files }, 'retention: removed engine-native transcripts');
    }
    return removed.length;
  }

  /**
   * GDPR erasure of one session (runbook §11.2): closes its engine, cancels its
   * approvals, drops proxy grants, deletes the JSONL + index rows, the worktree
   * (when no other session shares it) and the engine-native transcripts.
   * Refused while a turn is running (interrupt first). Handoff chains are
   * separate sessions: erase each one.
   */
  async erase(id: string): Promise<{ erased: true; engineTranscripts: number }> {
    const row = this.requireSession(id);
    const live = this.live.get(id);
    if (live?.turn) throw conflict('session_running', 'Interrupt the running turn before erasing the session.');
    if (live) {
      await this.closeEngine(live);
      this.live.delete(id);
    }
    this.deps.approvals.cancelSession(id, 'owner');
    this.revokeProxyGrants(id);
    this.deps.store.deleteSession(id);
    if (this.deps.store.worktreeUsers(row.worktree, id).length === 0) {
      await this.deps.workspace.removeWorktree(row.worktree).catch((error: unknown) => {
        this.deps.logger.warn({ err: describeError(error), sessionId: id }, 'erase: worktree removal failed');
      });
    }
    const engineTranscripts =
      row.resumeId && this.deps.removeEngineTranscripts ? await this.deps.removeEngineTranscripts([row.resumeId]).catch(() => 0) : 0;
    this.deps.logger.warn({ sessionId: id, engineTranscripts }, 'session erased');
    return { erased: true, engineTranscripts };
  }

  async shutdown(): Promise<void> {
    for (const live of this.live.values()) {
      if (live.turn) this.abortTurn(live, live.turn, 'kill');
      this.clearIdle(live);
    }
    await Promise.allSettled([...this.live.values()].map((live) => this.closeEngine(live)));
  }

  /** View used by the proxy approval bridge (CONTRACT.md §6). */
  proxyView(sessionId: string): ProxySessionView | null {
    const row = this.deps.store.getSession(sessionId);
    if (!row || row.archived) return null;
    const live = this.getLive(row);
    const view: ProxySessionView = {
      mode: row.mode,
      tainted: live.tainted,
      hasGrant: (key) => !live.tainted && live.grants.has(key),
      addGrant: (key) => {
        if (!live.tainted) live.grants.add(key);
      },
    };
    if (live.turn) view.turnId = live.turn.turnId;
    return view;
  }

  /** Pre-snapshot requested by the proxy bridge (docker exec into oet-postgres in Autopilot). */
  async proxySnapshot(sessionId: string, approvalId: string): Promise<boolean> {
    const row = this.deps.store.getSession(sessionId);
    if (!row) return false;
    const live = this.getLive(row);
    if (this.now() - live.lastSnapshotAt < PROXY_SNAPSHOT_REUSE_MS) return true;
    return this.preSnapshot(row.id, live, live.turn?.turnId, approvalId, []);
  }

  /** Records a PR opened by the Ship executor on the session. */
  setPullRequest(sessionId: string, pr: { number: number; url: string; state: string }): void {
    this.deps.store.updateSession(sessionId, { prNumber: pr.number, prUrl: pr.url, prState: pr.state });
  }

  isRunning(sessionId: string): boolean {
    return Boolean(this.live.get(sessionId)?.turn);
  }

  requireSession(id: string): SessionRow {
    const row = this.deps.store.getSession(id);
    if (!row) throw notFound('session_not_found', 'No such session.');
    return row;
  }

  // ------------------------------------------------------------- internals

  private parseCreate(body: unknown): CreateSession {
    const obj = asObject(body, false);
    const input: CreateSession = {
      engine: reqEnum(obj, 'engine', ENGINES),
      model: reqOpaqueId(obj, 'model'),
      mode: reqEnum(obj, 'mode', MODES),
    };
    const effort = optOpaqueId(obj, 'effort');
    const title = optString(obj, 'title', 200);
    const initialMessage = optString(obj, 'initialMessage', MAX_MESSAGE);
    if (effort !== undefined) input.effort = effort;
    if (title !== undefined) input.title = title;
    if (initialMessage !== undefined) input.initialMessage = initialMessage;
    return input;
  }

  private assertOperational(): void {
    if (this.deps.control.killed) throw locked('killed', 'The console was stopped (kill switch). Resume it before starting work.');
    if (this.deps.control.draining) throw locked('draining', 'The console is draining for an update; no new work is accepted.');
  }

  private assertCanStartTurn(): void {
    this.assertOperational();
    if (!this.deps.lease.isActive()) throw locked('lease_expired', 'The owner lease has lapsed; reopen the console to continue.');
  }

  private async validateModel(engine: Engine, model: string, effort: string | undefined): Promise<void> {
    const status = await this.deps.engines.status(engine);
    if (status.auth.state !== 'signed_in') {
      throw conflict('engine_not_signed_in', `${engine} is not signed in (${status.auth.state}).`);
    }
    if (status.models.length === 0) return;
    const info = status.models.find((m) => m.value === model);
    if (!info) throw badRequest('unknown_model', `${engine} does not offer model "${model}".`);
    if (effort !== undefined) {
      if (!info.supportsEffort) throw badRequest('effort_not_supported', `Model "${model}" does not take a reasoning effort.`);
      if (info.efforts.length > 0 && !info.efforts.includes(effort)) {
        throw badRequest('unknown_effort', `Model "${model}" does not support effort "${effort}".`);
      }
    }
  }

  private getLive(row: SessionRow): Live {
    let live = this.live.get(row.id);
    if (!live) {
      live = {
        engineSession: null,
        opening: null,
        idleTimer: null,
        turn: null,
        grants: new Set(),
        tainted: row.tainted,
        lastSnapshotAt: 0,
      };
      this.live.set(row.id, live);
    }
    return live;
  }

  private emit(sessionId: string, type: string, data: Record<string, unknown>, turnId?: string): AgentEvent | null {
    try {
      return this.deps.store.appendEvent(sessionId, type, data, turnId);
    } catch (error) {
      this.deps.logger.error({ err: describeError(error), sessionId, type }, 'failed to persist event');
      return null;
    }
  }

  private toSummary(row: SessionRow): SessionSummary {
    const summary: SessionSummary = {
      id: row.id,
      title: row.title,
      engine: row.engine,
      model: row.model,
      mode: row.mode,
      status: row.archived ? 'archived' : row.status,
      branch: row.branch,
      tainted: this.live.get(row.id)?.tainted ?? row.tainted,
      createdAt: row.createdAt,
      updatedAt: row.updatedAt,
      lastSeq: row.lastSeq,
      usage: { inputTokens: row.inputTokens, outputTokens: row.outputTokens },
    };
    if (row.effort) summary.effort = row.effort;
    if (row.costUsd !== null) summary.usage.costUsd = row.costUsd;
    if (row.createdBy) summary.createdBy = row.createdBy;
    if (row.firstMessage) summary.firstMessage = row.firstMessage;
    return summary;
  }

  private toDetail(row: SessionRow): SessionDetail {
    const detail: SessionDetail = { ...this.toSummary(row), pendingApprovals: this.deps.approvals.listPending(row.id) };
    if (row.prNumber !== null && row.prUrl) detail.pr = { number: row.prNumber, url: row.prUrl, state: row.prState ?? 'open' };
    if (row.handoffFrom) detail.handoffFrom = row.handoffFrom;
    return detail;
  }

  private clearIdle(live: Live): void {
    if (live.idleTimer) clearTimeout(live.idleTimer);
    live.idleTimer = null;
  }

  private scheduleIdle(live: Live): void {
    this.clearIdle(live);
    live.idleTimer = setTimeout(() => {
      live.idleTimer = null;
      if (!live.turn) void this.closeEngine(live);
    }, this.deps.config.idleCloseMs);
    live.idleTimer.unref();
  }

  private async closeEngine(live: Live): Promise<void> {
    this.clearIdle(live);
    const session = live.engineSession;
    live.engineSession = null;
    if (!session) return;
    try {
      await session.close();
    } catch (error) {
      this.deps.logger.warn({ err: describeError(error) }, 'engine session close failed');
    }
  }

  private abortTurn(live: Live, turn: ActiveTurn, by: ResolvedBy): void {
    if (turn.abortedBy === null) turn.abortedBy = by;
    turn.abort.abort();
    this.deps.approvals.cancelTurn(turn.turnId, by);
    const session = live.engineSession;
    if (session) {
      session.interrupt().catch((error: unknown) => {
        this.deps.logger.warn({ err: describeError(error) }, 'engine interrupt failed');
      });
    }
  }

  private async archive(row: SessionRow, live: Live): Promise<void> {
    await this.closeEngine(live);
    this.deps.approvals.cancelSession(row.id, 'owner');
    this.deps.store.updateSession(row.id, { archived: true, status: 'archived' });
    live.grants.clear();
    this.revokeProxyGrants(row.id);
    if (this.deps.store.worktreeUsers(row.worktree, row.id).length === 0) {
      await this.deps.workspace.removeWorktree(row.worktree).catch((error: unknown) => {
        this.deps.logger.warn({ err: describeError(error), sessionId: row.id }, 'worktree removal failed on archive');
      });
    }
  }

  private async readManual(): Promise<string> {
    if (this.manualCache !== null) return this.manualCache;
    try {
      this.manualCache = this.deps.readManual ? await this.deps.readManual() : '';
    } catch (error) {
      this.deps.logger.warn({ err: describeError(error) }, 'operating manual unavailable');
      this.manualCache = '';
    }
    return this.manualCache;
  }

  private async systemPrompt(row: SessionRow): Promise<string> {
    const parts: string[] = [];
    const manual = await this.readManual();
    if (manual.trim()) parts.push(manual.trim());
    parts.push(
      [
        '# Session',
        `- Session id: ${row.id}`,
        `- Branch: ${row.branch} (ship happens through the console's Ship button; never push to main)`,
        `- Worktree: ${row.worktree}`,
        `- Mode: ${row.mode}`,
      ].join('\n'),
    );
    const agentsMd = await this.deps.workspace.readAgentsMd(row.worktree).catch(() => '');
    if (agentsMd.trim()) parts.push(`# Repository AGENTS.md (from the session worktree)\n\n${agentsMd.trim()}`);
    if (row.seedSummary) parts.push(`# Handoff context\n\n${row.seedSummary}`);
    const prompt = parts.join('\n\n');
    return prompt.length > MAX_SYSTEM_PROMPT ? prompt.slice(0, MAX_SYSTEM_PROMPT) : prompt;
  }

  private openEngineSession(row: SessionRow, live: Live): Promise<EngineSession> {
    if (live.engineSession) return Promise.resolve(live.engineSession);
    if (live.opening) return live.opening;
    live.opening = (async () => {
      const adapter = await this.deps.engines.get(row.engine);
      await (this.deps.prepareDockerConfig ?? ((sessionId: string) => ensureDockerConfig(this.deps.config, sessionId)))(row.id);
      const options: SessionEngineOptions = {
        sessionId: row.id,
        cwd: row.worktree,
        model: row.model,
        mode: row.mode,
        appendSystemPrompt: await this.systemPrompt(row),
        env: buildAgentEnv(this.deps.config, { sessionId: row.id }),
      };
      if (row.effort) options.effort = row.effort;
      if (row.resumeId) options.resumeId = row.resumeId;
      const session = await adapter.openSession(options);
      live.engineSession = session;
      return session;
    })().finally(() => {
      live.opening = null;
    });
    return live.opening;
  }

  private async runTurn(sessionId: string, live: Live, turn: ActiveTurn, text: string): Promise<void> {
    let status: TurnStatus = 'error';
    let resumeId: string | undefined;
    try {
      const row = this.requireSession(sessionId);
      const engineSession = await this.openEngineSession(row, live);
      const current = this.requireSession(sessionId);
      const hooks: EngineHooks = {
        emit: (event) => this.onEngineEvent(sessionId, turn, event),
        onToolCall: (req, signal) => this.onToolCall(sessionId, live, turn, req, signal),
      };
      const opts: { model: string; effort?: string; mode: Mode } = { model: current.model, mode: current.mode };
      if (current.effort) opts.effort = current.effort;
      const result = await engineSession.runTurn(text, opts, hooks, turn.abort.signal);
      status = turn.abort.signal.aborted && result.status === 'ok' ? 'interrupted' : result.status;
      resumeId = result.resumeId;
      if (result.error) this.emit(sessionId, 'error', { code: result.error.code, message: result.error.message }, turn.turnId);
    } catch (error) {
      if (turn.abort.signal.aborted) {
        status = 'interrupted';
      } else {
        status = 'error';
        this.emit(sessionId, 'error', { code: 'engine_error', message: describeError(error).slice(0, 2000) }, turn.turnId);
        // A broken engine session is dropped so the next turn reopens (and resumes).
        await this.closeEngine(live);
      }
    } finally {
      this.deps.approvals.cancelTurn(turn.turnId, turn.abortedBy ?? 'timeout');
      live.turn = null;
      this.deps.store.finishTurn(turn.turnId, status);
      const patch: Parameters<Store['updateSession']>[1] = { status: turnStatusToSession(status) };
      if (resumeId) patch.resumeId = resumeId;
      if (this.deps.store.getSession(sessionId)?.archived) delete patch.status;
      this.deps.store.updateSession(sessionId, patch);
      this.emit(sessionId, 'turn_complete', { status, durationMs: Math.max(0, this.now() - turn.startedAt) }, turn.turnId);
      if (live.engineSession) this.scheduleIdle(live);
    }
  }

  private onEngineEvent(sessionId: string, turn: ActiveTurn, event: EngineEvent): void {
    let data: Record<string, unknown> = { ...event.data };
    switch (event.type) {
      case 'tool_call': {
        const row = this.deps.store.getSession(sessionId);
        const req: ToolCallRequest = {
          toolCallId: event.data.toolCallId,
          name: event.data.name,
          input: isRecord(event.data.input) ? event.data.input : {},
        };
        if (event.data.command !== undefined) req.command = event.data.command;
        if (event.data.cwd !== undefined) req.cwd = event.data.cwd;
        const c = turn.classifications.get(event.data.toolCallId) ?? classifyToolCall(req, { worktree: row?.worktree });
        turn.classifications.set(event.data.toolCallId, c);
        data = { ...data, classification: { destructive: c.destructive, unparseable: c.unparseable, reasons: c.reasons } };
        break;
      }
      case 'tool_result': {
        const output = event.data.output;
        if (Buffer.byteLength(output, 'utf8') > MAX_TOOL_OUTPUT) {
          data = { ...data, output: `${Buffer.from(output, 'utf8').subarray(0, MAX_TOOL_OUTPUT).toString('utf8')}\n…[output truncated at 64 KB]` };
        }
        break;
      }
      case 'usage': {
        const usage: { inputTokens: number; outputTokens: number; costUsd?: number } = {
          inputTokens: event.data.inputTokens,
          outputTokens: event.data.outputTokens,
        };
        if (event.data.costUsd !== undefined) usage.costUsd = event.data.costUsd;
        this.deps.store.addUsage(sessionId, usage);
        break;
      }
      default:
        break;
    }
    this.emit(sessionId, event.type, data, turn.turnId);
    if (event.type === 'tool_result') {
      // Defense in depth: a read the engine ran without asking (e.g. a Codex
      // command it trusts, or a built-in web search) still taints the session
      // once its output has reached the model. Idempotent with onToolCall. A call
      // that never ran (denied: not ok and no exit code) does not taint.
      const ran = event.data.ok || event.data.exitCode !== undefined;
      const classification = turn.classifications.get(event.data.toolCallId);
      const live = this.live.get(sessionId);
      if (ran && classification?.taintSource && live) this.taint(sessionId, live, turn, classification);
    }
  }

  private async onToolCall(
    sessionId: string,
    live: Live,
    turn: ActiveTurn,
    req: ToolCallRequest,
    signal: AbortSignal,
  ): Promise<ToolDecision> {
    const deny = (message: string): ToolDecision => ({ behavior: 'deny', message });
    if (this.deps.control.killed) return deny('The owner stopped the console (kill switch).');
    const combined = anySignal([signal, turn.abort.signal]);

    // Lease lapsed: pause at this tool boundary until the owner is back.
    if (!this.deps.lease.isActive()) {
      this.emit(sessionId, 'error', { code: 'lease_paused', message: 'Owner console disconnected: paused at a tool boundary until the lease is renewed.' }, turn.turnId);
      const resumed = await this.deps.lease.waitForActive(combined, this.deps.config.leasePauseMaxMs);
      if (!resumed) {
        if (!combined.aborted) this.abortTurn(live, turn, 'lease_expired');
        return deny('The owner lease lapsed; the turn was stopped at a tool boundary.');
      }
    }
    if (combined.aborted) return deny('The turn was interrupted.');

    const row = this.requireSession(sessionId);
    const classification = classifyToolCall(req, { worktree: row.worktree });
    turn.classifications.set(req.toolCallId, classification);
    const decision = decide({ mode: row.mode, classification, tainted: live.tainted, grants: live.grants });

    if (decision.action === 'deny') return deny(decision.message);

    if (decision.action === 'allow') {
      if (decision.autoApproved) {
        const card = this.deps.approvals.open({
          sessionId,
          turnId: turn.turnId,
          toolCallId: req.toolCallId,
          source: 'tool',
          summary: summarizeToolCall(req),
          ...(req.command !== undefined ? { command: req.command } : {}),
          cwd: req.cwd ?? row.worktree,
          uid: this.deps.config.agentUid,
          reasons: decision.reasons,
          tainted: false,
          grantKey: classification.grantKey,
        });
        if (decision.snapshot) {
          const ok = await this.preSnapshot(sessionId, live, turn.turnId, card.request.approvalId, classification.snapshotTables);
          if (!ok) {
            this.deps.approvals.resolveInternal(card.request.approvalId, 'deny', 'autopilot');
            return deny('Pre-snapshot failed, so the destructive operation was not run. Scope it to a single table or retry later.');
          }
        }
        this.deps.approvals.resolveInternal(card.request.approvalId, 'approve', 'autopilot');
      } else if (decision.snapshot) {
        const ok = await this.preSnapshot(sessionId, live, turn.turnId, undefined, classification.snapshotTables);
        if (!ok) return deny('Pre-snapshot failed, so the destructive operation was not run.');
      }
      this.taint(sessionId, live, turn, classification);
      return { behavior: 'allow' };
    }

    // decision.action === 'ask': owner card.
    const card = this.deps.approvals.open({
      sessionId,
      turnId: turn.turnId,
      toolCallId: req.toolCallId,
      source: 'tool',
      summary: summarizeToolCall(req),
      ...(req.command !== undefined ? { command: req.command } : {}),
      cwd: req.cwd ?? row.worktree,
      uid: this.deps.config.agentUid,
      reasons: decision.reasons,
      tainted: live.tainted,
      grantKey: classification.grantKey,
    });
    const approvalId = card.request.approvalId;
    turn.pendingApprovals += 1;
    if (turn.pendingApprovals === 1) this.deps.store.updateSession(sessionId, { status: 'awaiting_approval' });
    const onAbort = (): void => {
      this.deps.approvals.resolveInternal(approvalId, 'deny', turn.abortedBy ?? (this.deps.control.killed ? 'kill' : 'owner'));
    };
    combined.addEventListener('abort', onAbort, { once: true });
    if (combined.aborted) onAbort();
    const resolution = await card.result.finally(() => {
      combined.removeEventListener('abort', onAbort);
      turn.pendingApprovals -= 1;
      if (turn.pendingApprovals === 0 && live.turn === turn) this.deps.store.updateSession(sessionId, { status: 'running' });
    });
    if (resolution.decision === 'deny') {
      return deny(`Denied (${resolution.by})${resolution.note ? `: ${resolution.note}` : '.'}`);
    }
    if (resolution.decision === 'approve_session' && !live.tainted) live.grants.add(classification.grantKey);
    if (classification.needsDbSnapshot) {
      const ok = await this.preSnapshot(sessionId, live, turn.turnId, approvalId, classification.snapshotTables);
      if (!ok) return deny('Pre-snapshot failed, so the approved destructive operation was not run.');
    }
    this.taint(sessionId, live, turn, classification);
    return { behavior: 'allow' };
  }

  private taint(sessionId: string, live: Live, turn: ActiveTurn, classification: Classification): void {
    const state = { tainted: live.tainted, grants: live.grants };
    const result = applyTaint(state, classification);
    if (!result.changed || !result.source) return;
    live.tainted = true;
    this.deps.store.updateSession(sessionId, { tainted: true });
    this.emit(sessionId, 'taint', { reason: TAINT_REASONS[result.source] ?? result.source, source: result.source }, turn.turnId);
    this.revokeProxyGrants(sessionId);
  }

  private revokeProxyGrants(sessionId: string): void {
    const revoke = this.deps.revokeProxyGrants;
    if (!revoke) return;
    void revoke(sessionId).catch((error: unknown) => {
      this.deps.logger.warn({ err: describeError(error), sessionId }, 'proxy grant revocation failed');
    });
  }

  private async preSnapshot(
    sessionId: string,
    live: Live,
    turnId: string | undefined,
    approvalId: string | undefined,
    tables: string[],
  ): Promise<boolean> {
    const stamp = new Date(this.now()).toISOString().replace(/[-:]/g, '').replace(/\..*$/, '');
    const label = `s${sessionId.slice(-8).toLowerCase()}-${stamp}`;
    const result = await this.deps.snapshots.take({ label, tables });
    const data: Record<string, unknown> = { label: result.label, ok: result.ok };
    if (approvalId) data.approvalId = approvalId;
    if (result.file) data.file = result.file;
    if (result.error) data.error = result.error;
    this.emit(sessionId, 'snapshot', data, turnId);
    if (result.ok) live.lastSnapshotAt = this.now();
    return result.ok;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** AbortSignal that fires when any input fires (AbortSignal.any where available). */
export function anySignal(signals: AbortSignal[]): AbortSignal {
  const withAny = AbortSignal as unknown as { any?: (s: AbortSignal[]) => AbortSignal };
  if (typeof withAny.any === 'function') return withAny.any(signals);
  const controller = new AbortController();
  for (const s of signals) {
    if (s.aborted) {
      controller.abort();
      break;
    }
    s.addEventListener('abort', () => controller.abort(), { once: true });
  }
  return controller.signal;
}
