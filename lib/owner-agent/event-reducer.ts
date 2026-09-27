/**
 * Folds the AgentEvent stream (CONTRACT §4) into the render model the console
 * draws: conversation items (messages with streaming text, collapsible
 * thinking, tool cards with streamed output/results, file changes, approvals,
 * snapshots, taint/mode notices, turn dividers, errors) plus session-level
 * aggregates (pending approvals, taint, mode, usage, rate limits, ship log).
 *
 * Replay-safe: events are applied strictly once, keyed by `seq`. After a hub
 * reconnect the stream resumes with `afterSeq = lastSeq`, but if the server (or
 * a racing resubscribe) re-sends older events they are dropped here, so text is
 * never duplicated. `heartbeat` (no seq) and unknown event types are ignored.
 *
 * Pure and framework-free so it is unit-tested directly; `useOwnerAgentSession`
 * batches incoming events and dispatches them through `sessionRenderReducer`.
 */

import {
  SYSTEM_QUEUE_SESSION_ID,
  isEngine,
  isMode,
  type AgentEvent,
  type ApprovalDecision,
  type ApprovalRequest,
  type ApprovalResolvedBy,
  type Engine,
  type FileChangeKind,
  type Mode,
  type RateLimit,
  type ToolClassification,
  type TurnCompleteStatus,
} from './types';

/** Streamed tool output kept per tool card (the final result carries its own 64 KB cap). */
export const MAX_STREAMED_TOOL_OUTPUT = 256 * 1024;

interface ItemBase {
  key: string;
  seq: number;
  ts: string;
  turnId?: string;
}

export type ConsoleItem =
  | (ItemBase & { kind: 'turn'; model: string; effort?: string; mode: Mode | null })
  | (ItemBase & { kind: 'user'; text: string })
  | (ItemBase & { kind: 'assistant'; messageId: string; text: string; streaming: boolean })
  | (ItemBase & { kind: 'thinking'; text: string; streaming: boolean })
  | (ItemBase & {
      kind: 'tool';
      toolCallId: string;
      name: string;
      input: unknown;
      command?: string;
      cwd?: string;
      classification?: ToolClassification;
      streamedOutput: string;
      outputTruncated: boolean;
      result?: { ok: boolean; output: string; exitCode?: number };
      status: 'running' | 'ok' | 'error' | 'no_result';
    })
  | (ItemBase & { kind: 'file_change'; path: string; changeKind: FileChangeKind; diff?: string })
  | (ItemBase & {
      kind: 'approval';
      request: ApprovalRequest;
      resolution?: { decision: ApprovalDecision; by: ApprovalResolvedBy; seq: number; ts: string };
    })
  | (ItemBase & { kind: 'snapshot'; approvalId?: string; label: string; file?: string; ok: boolean; error?: string })
  | (ItemBase & { kind: 'taint'; reason: string; source: string })
  | (ItemBase & { kind: 'mode_changed'; mode: Mode; reason: string })
  | (ItemBase & { kind: 'turn_complete'; status: TurnCompleteStatus; durationMs: number })
  | (ItemBase & { kind: 'error'; code: string; message: string });

export type ConsoleItemKind = ConsoleItem['kind'];

export interface UsageBucket {
  inputTokens: number;
  outputTokens: number;
  cacheReadTokens: number;
  /** null until an engine reports a cost. */
  costUsd: number | null;
}

export interface UsageTotals extends UsageBucket {
  byModel: Record<string, UsageBucket>;
}

export interface ShipLogEntry {
  seq: number;
  ts: string;
  phase: string;
  message: string;
  level: 'info' | 'warn' | 'error';
}

export interface FileChangeEntry {
  seq: number;
  ts: string;
  path: string;
  changeKind: FileChangeKind;
  diff?: string;
}

export interface TaintEntry {
  seq: number;
  ts: string;
  turnId?: string;
  reason: string;
  source: string;
}

export interface SessionRenderModel {
  sessionId: string | null;
  lastSeq: number;
  items: ConsoleItem[];
  /** item key → index in `items`. */
  keyIndex: Record<string, number>;
  /** Unresolved approvals, oldest first. */
  pendingApprovals: ApprovalRequest[];
  resolvedApprovals: Record<string, { decision: ApprovalDecision; by: ApprovalResolvedBy }>;
  /** Taint seen in the current (or most recent) turn; reset when a new turn starts. */
  tainted: boolean;
  taints: TaintEntry[];
  mode: Mode | null;
  running: boolean;
  activeTurnId: string | null;
  lastTurnStatus: TurnCompleteStatus | null;
  /** Number of `turn_complete` events applied (lets views refresh server state per turn). */
  completedTurns: number;
  usage: UsageTotals;
  rateLimits: Partial<Record<Engine, RateLimit[]>>;
  shipLog: ShipLogEntry[];
  fileChanges: FileChangeEntry[];
  lastError: { code: string; message: string } | null;
  lastEventAt: string | null;
  /** Key of the thinking block still receiving deltas, if any. */
  openThinkingKey: string | null;
}

export type SessionRenderAction =
  | { type: 'reset'; sessionId: string | null }
  | { type: 'events'; events: readonly AgentEvent[] };

function emptyUsage(): UsageTotals {
  return { inputTokens: 0, outputTokens: 0, cacheReadTokens: 0, costUsd: null, byModel: {} };
}

export function createInitialRenderModel(sessionId: string | null = null): SessionRenderModel {
  return {
    sessionId,
    lastSeq: 0,
    items: [],
    keyIndex: {},
    pendingApprovals: [],
    resolvedApprovals: {},
    tainted: false,
    taints: [],
    mode: null,
    running: false,
    activeTurnId: null,
    lastTurnStatus: null,
    completedTurns: 0,
    usage: emptyUsage(),
    rateLimits: {},
    shipLog: [],
    fileChanges: [],
    lastError: null,
    lastEventAt: null,
    openThinkingKey: null,
  };
}

// ─── Defensive field readers (the stream is data, never trusted to be well formed) ───

type Data = Record<string, unknown>;

function str(data: Data, key: string, fallback = ''): string {
  const value = data[key];
  return typeof value === 'string' ? value : fallback;
}

function optStr(data: Data, key: string): string | undefined {
  const value = data[key];
  return typeof value === 'string' ? value : undefined;
}

function num(data: Data, key: string, fallback = 0): number {
  const value = data[key];
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

function optNum(data: Data, key: string): number | undefined {
  const value = data[key];
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

function bool(data: Data, key: string): boolean {
  return data[key] === true;
}

function strArray(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];
}

function toClassification(value: unknown): ToolClassification | undefined {
  if (!value || typeof value !== 'object') return undefined;
  const data = value as Data;
  return {
    destructive: bool(data, 'destructive'),
    unparseable: bool(data, 'unparseable'),
    reasons: strArray(data.reasons),
  };
}

function toApprovalRequest(data: Data): ApprovalRequest | null {
  const approvalId = str(data, 'approvalId');
  if (!approvalId) return null;
  return {
    approvalId,
    nonce: str(data, 'nonce'),
    toolCallId: str(data, 'toolCallId'),
    summary: str(data, 'summary'),
    command: optStr(data, 'command'),
    cwd: optStr(data, 'cwd'),
    uid: num(data, 'uid', -1),
    target: optStr(data, 'target'),
    reasons: strArray(data.reasons),
    tainted: bool(data, 'tainted'),
    expiresAt: str(data, 'expiresAt'),
  };
}

function asDecision(value: unknown): ApprovalDecision {
  return value === 'approve' || value === 'approve_session' ? value : 'deny';
}

function asResolvedBy(value: unknown): ApprovalResolvedBy {
  return value === 'autopilot' || value === 'lease_expired' || value === 'kill' || value === 'timeout'
    ? value
    : 'owner';
}

function asTurnStatus(value: unknown): TurnCompleteStatus {
  return value === 'interrupted' || value === 'error' || value === 'max_turns' ? value : 'ok';
}

function asChangeKind(value: unknown): FileChangeKind {
  return value === 'add' || value === 'delete' ? value : 'modify';
}

function appendCapped(current: string, addition: string, cap: number): { text: string; truncated: boolean } {
  const combined = current + addition;
  if (combined.length <= cap) return { text: combined, truncated: false };
  return { text: combined.slice(combined.length - cap), truncated: true };
}

// ─── Mutable draft helpers (one clone per batch, not per event) ───

function cloneForBatch(model: SessionRenderModel): SessionRenderModel {
  return {
    ...model,
    items: model.items.slice(),
    keyIndex: { ...model.keyIndex },
    pendingApprovals: model.pendingApprovals.slice(),
    resolvedApprovals: { ...model.resolvedApprovals },
    taints: model.taints.slice(),
    usage: { ...model.usage, byModel: { ...model.usage.byModel } },
    rateLimits: { ...model.rateLimits },
    shipLog: model.shipLog.slice(),
    fileChanges: model.fileChanges.slice(),
  };
}

function closeOpenThinking(draft: SessionRenderModel): void {
  if (!draft.openThinkingKey) return;
  const index = draft.keyIndex[draft.openThinkingKey];
  const item = index === undefined ? undefined : draft.items[index];
  if (item && item.kind === 'thinking' && item.streaming) {
    draft.items[index] = { ...item, streaming: false };
  }
  draft.openThinkingKey = null;
}

function appendItem(draft: SessionRenderModel, item: ConsoleItem): void {
  if (item.kind !== 'thinking') closeOpenThinking(draft);
  draft.keyIndex[item.key] = draft.items.length;
  draft.items.push(item);
}

function getItem<K extends ConsoleItemKind>(
  draft: SessionRenderModel,
  key: string,
  kind: K,
): { index: number; item: Extract<ConsoleItem, { kind: K }> } | null {
  const index = draft.keyIndex[key];
  if (index === undefined) return null;
  const item = draft.items[index];
  if (!item || item.kind !== kind) return null;
  return { index, item: item as Extract<ConsoleItem, { kind: K }> };
}

function base(event: AgentEvent, key: string, seq: number): ItemBase {
  const item: ItemBase = { key, seq, ts: typeof event.ts === 'string' ? event.ts : '' };
  if (typeof event.turnId === 'string') item.turnId = event.turnId;
  return item;
}

function emptyTool(event: AgentEvent, seq: number, toolCallId: string): Extract<ConsoleItem, { kind: 'tool' }> {
  return {
    ...base(event, `tool:${toolCallId}`, seq),
    kind: 'tool',
    toolCallId,
    name: 'tool',
    input: undefined,
    streamedOutput: '',
    outputTruncated: false,
    status: 'running',
  };
}

function upsertTool(
  draft: SessionRenderModel,
  event: AgentEvent,
  seq: number,
  toolCallId: string,
  update: (tool: Extract<ConsoleItem, { kind: 'tool' }>) => Extract<ConsoleItem, { kind: 'tool' }>,
): void {
  const key = `tool:${toolCallId}`;
  const existing = getItem(draft, key, 'tool');
  if (existing) {
    draft.items[existing.index] = update(existing.item);
  } else {
    appendItem(draft, update(emptyTool(event, seq, toolCallId)));
  }
}

function addUsage(bucket: UsageBucket, data: Data): UsageBucket {
  const cost = optNum(data, 'costUsd');
  return {
    inputTokens: bucket.inputTokens + num(data, 'inputTokens'),
    outputTokens: bucket.outputTokens + num(data, 'outputTokens'),
    cacheReadTokens: bucket.cacheReadTokens + num(data, 'cacheReadTokens'),
    costUsd: cost === undefined ? bucket.costUsd : (bucket.costUsd ?? 0) + cost,
  };
}

function finishTurn(draft: SessionRenderModel, status: TurnCompleteStatus): void {
  closeOpenThinking(draft);
  draft.running = false;
  draft.activeTurnId = null;
  draft.lastTurnStatus = status;
  draft.completedTurns += 1;
  for (let i = 0; i < draft.items.length; i += 1) {
    const item = draft.items[i];
    if (item.kind === 'assistant' && item.streaming) {
      draft.items[i] = { ...item, streaming: false };
    } else if (item.kind === 'thinking' && item.streaming) {
      draft.items[i] = { ...item, streaming: false };
    } else if (item.kind === 'tool' && item.status === 'running') {
      draft.items[i] = { ...item, status: 'no_result' };
    }
  }
}

function applyOne(draft: SessionRenderModel, event: AgentEvent, seq: number): void {
  const data: Data = event.data && typeof event.data === 'object' ? (event.data as Data) : {};

  switch (event.type) {
    case 'turn_started': {
      closeOpenThinking(draft);
      draft.running = true;
      draft.activeTurnId = typeof event.turnId === 'string' ? event.turnId : null;
      draft.tainted = false;
      const mode = isMode(data.mode) ? data.mode : null;
      if (mode) draft.mode = mode;
      appendItem(draft, {
        ...base(event, `turn:${event.turnId ?? seq}:${seq}`, seq),
        kind: 'turn',
        model: str(data, 'model'),
        effort: optStr(data, 'effort'),
        mode,
      });
      return;
    }
    case 'user_message':
      appendItem(draft, { ...base(event, `user:${seq}`, seq), kind: 'user', text: str(data, 'text') });
      return;
    case 'text_delta':
    case 'text': {
      const messageId = str(data, 'messageId') || `seq-${seq}`;
      const key = `assistant:${messageId}`;
      const text = str(data, 'text');
      const isFinal = event.type === 'text';
      const existing = getItem(draft, key, 'assistant');
      if (existing) {
        draft.items[existing.index] = {
          ...existing.item,
          text: isFinal ? text : existing.item.text + text,
          streaming: !isFinal,
        };
        if (!isFinal) closeOpenThinking(draft);
      } else {
        appendItem(draft, {
          ...base(event, key, seq),
          kind: 'assistant',
          messageId,
          text,
          streaming: !isFinal,
        });
      }
      return;
    }
    case 'thinking_delta': {
      const text = str(data, 'text');
      const openKey = draft.openThinkingKey;
      const open = openKey ? getItem(draft, openKey, 'thinking') : null;
      const isLast = open !== null && open.index === draft.items.length - 1;
      const sameTurn = open !== null && open.item.turnId === (typeof event.turnId === 'string' ? event.turnId : undefined);
      if (open && isLast && sameTurn) {
        draft.items[open.index] = { ...open.item, text: open.item.text + text, streaming: true };
      } else {
        closeOpenThinking(draft);
        const key = `thinking:${seq}`;
        appendItem(draft, { ...base(event, key, seq), kind: 'thinking', text, streaming: true });
        draft.openThinkingKey = key;
      }
      return;
    }
    case 'tool_call': {
      const toolCallId = str(data, 'toolCallId') || `seq-${seq}`;
      upsertTool(draft, event, seq, toolCallId, (tool) => ({
        ...tool,
        name: str(data, 'name', tool.name) || tool.name,
        input: data.input,
        command: optStr(data, 'command') ?? tool.command,
        cwd: optStr(data, 'cwd') ?? tool.cwd,
        classification: toClassification(data.classification) ?? tool.classification,
      }));
      return;
    }
    case 'tool_output_delta': {
      const toolCallId = str(data, 'toolCallId');
      if (!toolCallId) return;
      upsertTool(draft, event, seq, toolCallId, (tool) => {
        const next = appendCapped(tool.streamedOutput, str(data, 'text'), MAX_STREAMED_TOOL_OUTPUT);
        return { ...tool, streamedOutput: next.text, outputTruncated: tool.outputTruncated || next.truncated };
      });
      return;
    }
    case 'tool_result': {
      const toolCallId = str(data, 'toolCallId');
      if (!toolCallId) return;
      const ok = bool(data, 'ok');
      upsertTool(draft, event, seq, toolCallId, (tool) => ({
        ...tool,
        result: { ok, output: str(data, 'output'), exitCode: optNum(data, 'exitCode') },
        status: ok ? 'ok' : 'error',
      }));
      return;
    }
    case 'file_change': {
      const entry: FileChangeEntry = {
        seq,
        ts: typeof event.ts === 'string' ? event.ts : '',
        path: str(data, 'path'),
        changeKind: asChangeKind(data.changeKind),
        diff: optStr(data, 'diff'),
      };
      draft.fileChanges.push(entry);
      appendItem(draft, {
        ...base(event, `file:${seq}`, seq),
        kind: 'file_change',
        path: entry.path,
        changeKind: entry.changeKind,
        diff: entry.diff,
      });
      return;
    }
    case 'approval_request': {
      const request = toApprovalRequest(data);
      if (!request) return;
      const key = `approval:${request.approvalId}`;
      const resolved = draft.resolvedApprovals[request.approvalId];
      const existing = getItem(draft, key, 'approval');
      if (existing) {
        draft.items[existing.index] = { ...existing.item, request };
      } else {
        const item: Extract<ConsoleItem, { kind: 'approval' }> = { ...base(event, key, seq), kind: 'approval', request };
        // Resolution seen first (out-of-order replay): show the card as decided.
        if (resolved) item.resolution = { ...resolved, seq, ts: item.ts };
        appendItem(draft, item);
      }
      if (!resolved && !draft.pendingApprovals.some((p) => p.approvalId === request.approvalId)) {
        draft.pendingApprovals.push(request);
      }
      return;
    }
    case 'approval_resolved': {
      const approvalId = str(data, 'approvalId');
      if (!approvalId) return;
      const resolution = { decision: asDecision(data.decision), by: asResolvedBy(data.by) };
      draft.resolvedApprovals[approvalId] = resolution;
      draft.pendingApprovals = draft.pendingApprovals.filter((p) => p.approvalId !== approvalId);
      const existing = getItem(draft, `approval:${approvalId}`, 'approval');
      if (existing) {
        draft.items[existing.index] = {
          ...existing.item,
          resolution: { ...resolution, seq, ts: typeof event.ts === 'string' ? event.ts : '' },
        };
      }
      return;
    }
    case 'snapshot':
      appendItem(draft, {
        ...base(event, `snapshot:${seq}`, seq),
        kind: 'snapshot',
        approvalId: optStr(data, 'approvalId'),
        label: str(data, 'label'),
        file: optStr(data, 'file'),
        ok: bool(data, 'ok'),
        error: optStr(data, 'error'),
      });
      return;
    case 'taint': {
      const entry: TaintEntry = {
        seq,
        ts: typeof event.ts === 'string' ? event.ts : '',
        reason: str(data, 'reason'),
        source: str(data, 'source'),
      };
      if (typeof event.turnId === 'string') entry.turnId = event.turnId;
      draft.tainted = true;
      draft.taints.push(entry);
      appendItem(draft, { ...base(event, `taint:${seq}`, seq), kind: 'taint', reason: entry.reason, source: entry.source });
      return;
    }
    case 'mode_changed': {
      if (!isMode(data.mode)) return;
      draft.mode = data.mode;
      appendItem(draft, {
        ...base(event, `mode:${seq}`, seq),
        kind: 'mode_changed',
        mode: data.mode,
        reason: str(data, 'reason'),
      });
      return;
    }
    case 'usage': {
      const model = str(data, 'model') || 'unknown';
      const totals = addUsage(draft.usage, data);
      draft.usage = {
        ...totals,
        byModel: {
          ...draft.usage.byModel,
          [model]: addUsage(
            draft.usage.byModel[model] ?? { inputTokens: 0, outputTokens: 0, cacheReadTokens: 0, costUsd: null },
            data,
          ),
        },
      };
      return;
    }
    case 'rate_limit': {
      if (!isEngine(data.engine) || !Array.isArray(data.limits)) return;
      draft.rateLimits[data.engine] = (data.limits as unknown[]).filter(
        (limit): limit is RateLimit => Boolean(limit) && typeof limit === 'object' && typeof (limit as RateLimit).label === 'string',
      );
      return;
    }
    case 'turn_complete': {
      const status = asTurnStatus(data.status);
      finishTurn(draft, status);
      appendItem(draft, {
        ...base(event, `turn_complete:${seq}`, seq),
        kind: 'turn_complete',
        status,
        durationMs: num(data, 'durationMs'),
      });
      return;
    }
    case 'error': {
      const error = { code: str(data, 'code', 'error'), message: str(data, 'message') };
      draft.lastError = error;
      appendItem(draft, { ...base(event, `error:${seq}`, seq), kind: 'error', ...error });
      return;
    }
    case 'ship': {
      const level = data.level === 'warn' || data.level === 'error' ? data.level : 'info';
      draft.shipLog.push({
        seq,
        ts: typeof event.ts === 'string' ? event.ts : '',
        phase: str(data, 'phase'),
        message: str(data, 'message'),
        level,
      });
      return;
    }
    default:
      // Unknown or future event types are ignored (forward compatible).
      return;
  }
}

/**
 * Apply a batch of events. Returns the previous model object unchanged when
 * nothing new was applied (heartbeats, duplicates), so React can bail out.
 */
export function applyAgentEvents(model: SessionRenderModel, events: readonly AgentEvent[]): SessionRenderModel {
  let draft: SessionRenderModel | null = null;
  for (const event of events) {
    if (!event || typeof event !== 'object' || typeof event.type !== 'string') continue;
    if (event.type === 'heartbeat') continue;
    const seq = typeof event.seq === 'number' && Number.isFinite(event.seq) ? event.seq : null;
    if (seq === null) continue; // Only persisted (sequenced) events carry state.
    const current: SessionRenderModel = draft ?? model;
    if (seq <= current.lastSeq) continue; // replay / reconnect de-dup
    if (
      current.sessionId
      && current.sessionId !== SYSTEM_QUEUE_SESSION_ID
      && typeof event.sessionId === 'string'
      && event.sessionId.length > 0
      && event.sessionId !== current.sessionId
    ) {
      continue; // never mix another session's events into this view
    }
    if (!draft) draft = cloneForBatch(model);
    draft.lastSeq = seq;
    if (typeof event.ts === 'string') draft.lastEventAt = event.ts;
    applyOne(draft, event, seq);
  }
  return draft ?? model;
}

export function sessionRenderReducer(model: SessionRenderModel, action: SessionRenderAction): SessionRenderModel {
  switch (action.type) {
    case 'reset':
      return model.sessionId === action.sessionId && model.lastSeq === 0 && model.items.length === 0
        ? model
        : createInitialRenderModel(action.sessionId);
    case 'events':
      return action.events.length === 0 ? model : applyAgentEvents(model, action.events);
    default:
      return model;
  }
}
