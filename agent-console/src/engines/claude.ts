// Claude Code engine adapter: @anthropic-ai/claude-agent-sdk `query()` in streaming-input mode,
// one long-lived query per console session, driving the SDK-bundled `claude` binary as uid agent.
//
// Guarantees (plan Phase 1 + review corrections):
//  - The binary is spawned through /usr/local/bin/claude-as-agent (setpriv wrapper, CONTRACT §2)
//    with the allow-listed env from src/env.ts; API-key/3P variables are stripped again here.
//  - settingSources: [] — no user/project/local settings, hooks or CLAUDE.md from the worktree;
//    managed policy comes from /etc/claude-code/managed-settings.json (loaded regardless).
//  - Every tool call, including subagents', passes a PreToolUse hook (no matcher, 900 s) that
//    asks the session layer (Guard + owner approval + snapshot) via hooks.onToolCall.
//  - canUseTool never allows: anything Claude Code still wants to prompt for (protected paths,
//    critical removals, AskUserQuestion) is denied with an explanation.
//  - permissionMode 'default' (guarded/autopilot) or 'dontAsk' (read_only); never
//    bypassPermissions / acceptEdits.
//  - Model/effort change between turns via setModel() + applyFlagSettings({effortLevel}).
//  - Idle queries are closed by src/sessions.ts (close()) and transparently resumed by session_id.
//  - Runs only on first-party Claude subscription auth; anything else is auth.state 'error'.
//
// SDK message shapes are parsed defensively from `unknown` so a minor SDK drift degrades to
// missing events rather than crashes; assumptions are marked VERIFY-ON-PIN.

import { randomUUID } from 'node:crypto';
import type { CanUseTool, HookCallback, HookJSONOutput, Options, Query, SDKUserMessage } from '@anthropic-ai/claude-agent-sdk';
import { ClaudeAuthManager, type ClaudeAuthStatus, type PtySpawner } from '../auth/claude.js';
import type { AppConfig } from '../config.js';
import type { EngineFactoryContext } from '../engine-registry.js';
import type { Runner } from '../exec.js';
import {
  capUtf8,
  consoleEngineLogger,
  EngineError,
  type EngineLogger,
  epochToIso,
  isRecord,
  rateLimitStatus,
  safeErrorMessage,
  toEngineLogger,
} from './codex-protocol.js';
import type {
  ConnectFlow,
  EngineAdapter,
  EngineAuth,
  EngineEvent,
  EngineHooks,
  EngineSession,
  EngineStatus,
  Mode,
  ModelInfo,
  RateLimit,
  SessionEngineOptions,
  ToolCallRequest,
  ToolDecision,
  TurnResult,
} from './types.js';

// ─── Configuration ─────────────────────────────────────────────────────────────────────────

export type QueryFn = (params: { prompt: string | AsyncIterable<SDKUserMessage>; options?: Options }) => Query;

/** Paths come from AppConfig and the image layout; everything here is optional tuning or a test seam. */
export interface ClaudeAdapterOverrides {
  /** Allow-listed env for auth/status/probe children; defaults to context.buildEnv() (src/env.ts). */
  baseEnv?: () => Record<string, string>;
  /** Wrapper that execs the SDK-bundled binary as uid agent (image: /usr/local/bin/claude-as-agent). */
  executablePath?: string;
  /** The SDK-bundled native binary (image: /usr/local/lib/oet-agent/claude → node_modules/...). */
  claudeBinPath?: string;
  /** Agentic round-trips allowed per user turn (SDK maxTurns). VERIFY-ON-PIN: per user message in streaming mode. */
  maxTurnsPerTurn?: number;
  modelCacheMs?: number;
  authStatusCacheMs?: number;
  hookTimeoutSec?: number;
  probeTimeoutMs?: number;
  interruptGraceMs?: number;
  /** Tools pre-approved in read_only mode (dontAsk denies everything else that would prompt). */
  readOnlyAllowedTools?: string[];
  logger?: EngineLogger;
  queryFn?: QueryFn;
  runner?: Runner;
  spawnPty?: PtySpawner;
  now?: () => number;
}

/** Image layout (agent-console/Dockerfile) and tuning defaults. */
export const CLAUDE_DEFAULTS = {
  executablePath: '/usr/local/bin/claude-as-agent',
  claudeBinPath: '/usr/local/lib/oet-agent/claude',
  maxTurnsPerTurn: 200,
  modelCacheMs: 10 * 60_000,
  authStatusCacheMs: 30_000,
  hookTimeoutSec: 900,
  probeTimeoutMs: 45_000,
  interruptGraceMs: 30_000,
  readOnlyAllowedTools: ['Read', 'Glob', 'Grep'],
} as const;

// ─── Pure helpers (exported for tests) ─────────────────────────────────────────────────────

function rec(value: unknown): Record<string, unknown> {
  return isRecord(value) ? value : {};
}

function str(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function num(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

/** Credentials/providers the console must never run on; src/env.ts strips these too. */
const STRIPPED_ENV = /^(?:ANTHROPIC_.*|CLAUDE_CODE_USE_.*|CLAUDE_CODE_OAUTH_TOKEN|AWS_BEARER_TOKEN_BEDROCK|OPENAI_API_KEY|CODEX_API_KEY|OWNER_AGENT_.*)$/;

export function buildClaudeEnv(base: Record<string, string>, paths: { homeDir: string; configDir: string }): Record<string, string> {
  const env: Record<string, string> = {};
  for (const [key, value] of Object.entries(base)) if (!STRIPPED_ENV.test(key)) env[key] = value;
  env['HOME'] ??= paths.homeDir;
  env['CLAUDE_CONFIG_DIR'] ??= paths.configDir;
  env['DISABLE_AUTOUPDATER'] = '1';
  env['CLAUDE_AGENT_SDK_CLIENT_APP'] = 'oet-agent-console';
  return env;
}

export function permissionModeFor(mode: Mode): 'default' | 'dontAsk' {
  return mode === 'read_only' ? 'dontAsk' : 'default';
}

/** SDK ModelInfo[] (supportedModels()) → contract ModelInfo[]; ids stay opaque. */
export function mapClaudeModels(models: unknown): ModelInfo[] {
  const out: ModelInfo[] = [];
  const seen = new Set<string>();
  const list: unknown[] = Array.isArray(models) ? models : [];
  for (const raw of list) {
    const m = rec(raw);
    const value = str(m['value']);
    if (!value || seen.has(value)) continue;
    seen.add(value);
    const levels: unknown[] = Array.isArray(m['supportedEffortLevels']) ? m['supportedEffortLevels'] : [];
    const efforts: string[] = m['supportsEffort'] === false ? [] : levels.filter((e): e is string => typeof e === 'string' && e.length > 0);
    const description = str(m['description']);
    // VERIFY-ON-PIN: the SDK documents 'high' as the default effort; it reports no per-model default.
    const defaultEffort = efforts.includes('high') ? 'high' : undefined;
    out.push({
      value,
      displayName: str(m['displayName']) ?? value,
      ...(description ? { description } : {}),
      supportsEffort: efforts.length > 0,
      efforts,
      ...(defaultEffort ? { defaultEffort } : {}),
    });
  }
  return out;
}

/**
 * API-key sources that mean the CLI is NOT running on the owner's subscription.
 * VERIFY-ON-PIN: subscription sessions report apiKeySource "none" in system/init.
 */
const API_KEY_SOURCES = new Set(['user', 'project', 'org', 'temporary', 'ANTHROPIC_API_KEY', 'apiKeyHelper', '/login managed key']);

export function apiKeySourceProblem(source: string | undefined): string | undefined {
  if (!source || !API_KEY_SOURCES.has(source)) return undefined;
  return `Claude Code is using an API key (source: ${source}) instead of the Claude subscription. Remove it and reconnect Claude Max.`;
}

/** Startup assertion: first-party claude.ai subscription auth, or an error the owner can act on. */
export function assessClaudeAuth(status: ClaudeAuthStatus, account?: unknown): EngineAuth {
  if (!status.loggedIn) return { state: 'signed_out' };
  const a = rec(account);
  const apiProvider = str(a['apiProvider']) ?? status.apiProvider;
  if (apiProvider && apiProvider !== 'firstParty') {
    return { state: 'error', detail: `Claude Code is using a third-party provider (${apiProvider}); the console only runs on a Claude subscription.` };
  }
  const keyProblem = apiKeySourceProblem(str(a['apiKeySource']));
  if (keyProblem) return { state: 'error', detail: keyProblem };
  if (status.authMethod && status.authMethod !== 'claude.ai') {
    return { state: 'error', detail: `Claude Code is signed in via ${status.authMethod}, not a Claude subscription. Log out and connect Claude Max.` };
  }
  const plan = str(a['subscriptionType']) ?? status.subscriptionType;
  if (!plan) return { state: 'error', detail: 'Claude Code is signed in but reports no active Claude subscription.' };
  const email = str(a['email']) ?? status.email;
  const workspace = str(a['organization']) ?? status.orgName;
  return { state: 'signed_in', account: { plan, ...(email ? { email } : {}), ...(workspace ? { workspace } : {}) } };
}

const RATE_LIMIT_LABELS: Record<string, string> = {
  five_hour: '5-hour window',
  seven_day: '7-day window',
  seven_day_opus: '7-day window (Opus)',
  seven_day_sonnet: '7-day window (Sonnet)',
  overage: 'Extra usage',
};

function rateLimitLabel(type: string): string {
  return RATE_LIMIT_LABELS[type] ?? type.replace(/_/g, ' ');
}

/** VERIFY-ON-PIN: utilization is a 0–1 fraction (values > 1 are treated as percentages). */
export function normalizeUtilization(value: unknown): number | undefined {
  const n = num(value);
  if (n === undefined || n < 0) return undefined;
  return Math.round(n <= 1 ? n * 100 : n);
}

function claudeLimitStatus(status: string | undefined, usedPercent: number | undefined): RateLimit['status'] {
  if (status === 'rejected') return 'limited';
  if (status === 'allowed_warning') return 'warning';
  if (status === 'allowed') return usedPercent !== undefined && usedPercent >= 80 ? 'warning' : 'ok';
  return rateLimitStatus(usedPercent);
}

/**
 * rate_limit_event.rate_limit_info → RateLimit[]. Shape: { status: 'allowed' |
 * 'allowed_warning' | 'rejected', resetsAt: unix s, rateLimitType, utilization? } plus, on
 * newer CLIs, unifiedWindows { [type]: { utilization, resetsAt } } (VERIFY-ON-PIN).
 */
export function mapClaudeRateLimit(info: unknown): RateLimit[] {
  const i = rec(info);
  const out: RateLimit[] = [];
  const build = (type: string, status: string | undefined, utilization: unknown, resetsAtRaw: unknown): RateLimit => {
    const usedPercent = normalizeUtilization(utilization);
    const resetsAt = epochToIso(resetsAtRaw);
    return {
      label: rateLimitLabel(type),
      status: claudeLimitStatus(status, usedPercent),
      ...(usedPercent !== undefined ? { usedPercent } : {}),
      ...(resetsAt ? { resetsAt } : {}),
    };
  };
  for (const [type, window] of Object.entries(rec(i['unifiedWindows']))) {
    if (isRecord(window)) out.push(build(type, str(window['status']), window['utilization'], window['resetsAt']));
  }
  const type = str(i['rateLimitType']);
  if (type) {
    const limit = build(type, str(i['status']), i['utilization'], i['resetsAt']);
    const existing = out.findIndex((l) => l.label === limit.label);
    if (existing === -1) out.push(limit);
    else out[existing] = { ...out[existing], ...limit };
  }
  return out;
}

const FILE_WRITE_TOOLS = new Set(['Write', 'Edit', 'MultiEdit', 'NotebookEdit']);

function writePathOf(input: Record<string, unknown>): string | undefined {
  return str(input['file_path']) ?? str(input['notebook_path']);
}

/** PreToolUse hook input → Guard request. */
export function toolCallRequestFromHook(input: unknown, toolUseId: string | undefined): ToolCallRequest | undefined {
  const i = rec(input);
  const event = str(i['hook_event_name']);
  if (event && event !== 'PreToolUse') return undefined;
  const name = str(i['tool_name']);
  if (!name) return undefined;
  const toolInput = rec(i['tool_input']);
  const toolCallId = toolUseId ?? str(i['tool_use_id']) ?? `hook-${randomUUID()}`;
  const command = name === 'Bash' ? str(toolInput['command']) : undefined;
  const cwd = str(i['cwd']);
  const writePath = FILE_WRITE_TOOLS.has(name) ? writePathOf(toolInput) : undefined;
  return {
    toolCallId,
    name,
    input: toolInput,
    ...(command !== undefined ? { command } : {}),
    ...(cwd ? { cwd } : {}),
    ...(writePath ? { writePaths: [writePath] } : {}),
  };
}

/** Guard decision → PreToolUse hook output (hook deny beats every allow rule and mode). */
export function hookOutputFor(decision: ToolDecision): HookJSONOutput {
  if (decision.behavior === 'allow') {
    return {
      hookSpecificOutput: {
        hookEventName: 'PreToolUse',
        permissionDecision: 'allow',
        permissionDecisionReason: 'Approved by the OET console guard.',
      },
    };
  }
  return {
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      permissionDecision: 'deny',
      permissionDecisionReason: decision.message || 'Denied by the OET console guard.',
    },
  };
}

export const CAN_USE_TOOL_DENIAL =
  'Blocked: this action needs an interactive Claude Code permission prompt (for example a write to a protected path such as .git/ or .claude/, or a critical removal). The OET console does not grant those. Choose another approach or ask the owner to do it.';
export const ASK_USER_DENIAL =
  'Interactive questions are not available in the OET console. Put your question in your reply to the owner and end the turn.';

/**
 * Per-turn cost from the running total. In streaming mode total_cost_usd is cumulative for
 * the query (plus spend restored on resume), so the turn's cost is the delta. With no
 * baseline (first turn after a sidecar restart on a resumed session) the cost is unknown.
 * A total below the baseline means the CLI restarted its totals, so the total is the turn.
 */
export function computeTurnCost(
  baseline: number | undefined,
  total: number | undefined,
): { costUsd: number | undefined; baseline: number | undefined } {
  if (total === undefined) return { costUsd: undefined, baseline };
  if (baseline === undefined) return { costUsd: undefined, baseline: total };
  if (total >= baseline) return { costUsd: Math.round((total - baseline) * 1e6) / 1e6, baseline: total };
  return { costUsd: total, baseline: total };
}

function stringifyToolContent(content: unknown): string {
  if (typeof content === 'string') return content;
  if (!Array.isArray(content)) return content === undefined || content === null ? '' : JSON.stringify(content);
  return content
    .map((block) => {
      const b = rec(block);
      if (b['type'] === 'text') return str(b['text']) ?? '';
      if (b['type'] === 'image') return '[image]';
      return JSON.stringify(block);
    })
    .join('\n');
}

export interface ClaudeResultSummary {
  subtype: string;
  isError: boolean;
  totalCostUsd?: number;
  inputTokens: number;
  outputTokens: number;
  cacheReadTokens: number;
  errors: string[];
  resultText?: string;
}

export interface MappedClaudeMessage {
  events: EngineEvent[];
  sessionId?: string;
  init?: { apiKeySource?: string };
  result?: ClaudeResultSummary;
  rateLimits?: RateLimit[];
}

/**
 * SDKMessage → EngineEvents for one turn. Text blocks get per-block message ids
 * (`<api message id>:<n>`) because Claude Code emits one assistant message per content block,
 * all sharing the API message id; stream deltas and final blocks are numbered the same way.
 */
export class ClaudeMessageMapper {
  private streamMessageId: string | undefined;
  private readonly streamOrdinals = new Map<number, number>();
  private readonly streamTextCount = new Map<string, number>();
  private readonly finalTextCount = new Map<string, number>();
  private readonly thinkingStreamed = new Set<string>();
  private readonly tools = new Map<string, { name: string; input: Record<string, unknown> }>();
  /** Last API error reported on an assistant message in this turn. */
  lastError: { code: string; message: string } | undefined;

  /** @param emittedToolCalls ids already announced (shared with the PreToolUse hook). */
  constructor(private readonly emittedToolCalls: Set<string>) {}

  handle(message: unknown): MappedClaudeMessage {
    const m = rec(message);
    const sessionId = str(m['session_id']);
    const base: MappedClaudeMessage = { events: [], ...(sessionId ? { sessionId } : {}) };
    switch (m['type']) {
      case 'stream_event':
        base.events.push(...this.streamEvent(rec(m['event'])));
        return base;
      case 'assistant':
        base.events.push(...this.assistant(m));
        return base;
      case 'user':
        base.events.push(...this.toolResults(m));
        return base;
      case 'system':
        if (m['subtype'] === 'init') {
          const apiKeySource = str(m['apiKeySource']);
          base.init = apiKeySource ? { apiKeySource } : {};
        }
        return base;
      case 'rate_limit_event': {
        const limits = mapClaudeRateLimit(m['rate_limit_info']);
        if (limits.length > 0) {
          base.rateLimits = limits;
          base.events.push({ type: 'rate_limit', data: { engine: 'claude', limits } });
        }
        return base;
      }
      case 'result':
        base.result = summarizeResult(m);
        return base;
      default:
        return base;
    }
  }

  private streamEvent(event: Record<string, unknown>): EngineEvent[] {
    switch (event['type']) {
      case 'message_start':
        this.streamMessageId = str(rec(event['message'])['id']);
        this.streamOrdinals.clear();
        return [];
      case 'content_block_delta': {
        const delta = rec(event['delta']);
        const msgId = this.streamMessageId;
        if (!msgId) return [];
        if (delta['type'] === 'text_delta') {
          const text = str(delta['text']);
          if (!text) return [];
          const index = num(event['index']) ?? 0;
          let ordinal = this.streamOrdinals.get(index);
          if (ordinal === undefined) {
            ordinal = this.streamTextCount.get(msgId) ?? 0;
            this.streamTextCount.set(msgId, ordinal + 1);
            this.streamOrdinals.set(index, ordinal);
          }
          return [{ type: 'text_delta', data: { messageId: `${msgId}:${ordinal}`, text } }];
        }
        if (delta['type'] === 'thinking_delta') {
          const text = str(delta['thinking']);
          if (!text) return [];
          this.thinkingStreamed.add(msgId);
          return [{ type: 'thinking_delta', data: { text } }];
        }
        return [];
      }
      default:
        return [];
    }
  }

  private assistant(m: Record<string, unknown>): EngineEvent[] {
    const message = rec(m['message']);
    const msgId = str(message['id']) ?? `msg-${randomUUID()}`;
    const mainLoop = m['parent_tool_use_id'] === null || m['parent_tool_use_id'] === undefined;
    const events: EngineEvent[] = [];
    const content: unknown[] = Array.isArray(message['content']) ? message['content'] : [];
    for (const raw of content) {
      const block = rec(raw);
      switch (block['type']) {
        case 'text': {
          const text = str(block['text']);
          if (!text || !mainLoop) break;
          const ordinal = this.finalTextCount.get(msgId) ?? 0;
          this.finalTextCount.set(msgId, ordinal + 1);
          events.push({ type: 'text', data: { messageId: `${msgId}:${ordinal}`, text } });
          break;
        }
        case 'thinking': {
          const text = str(block['thinking']);
          if (text && mainLoop && !this.thinkingStreamed.has(msgId)) events.push({ type: 'thinking_delta', data: { text } });
          break;
        }
        case 'tool_use': {
          const id = str(block['id']);
          const name = str(block['name']);
          if (!id || !name) break;
          const input = rec(block['input']);
          this.tools.set(id, { name, input });
          if (!this.emittedToolCalls.has(id)) {
            this.emittedToolCalls.add(id);
            const command = name === 'Bash' ? str(input['command']) : undefined;
            events.push({ type: 'tool_call', data: { toolCallId: id, name, input, ...(command !== undefined ? { command } : {}) } });
          }
          break;
        }
        default:
          break;
      }
    }
    // VERIFY-ON-PIN: API failures surface as an `error` code on the assistant message. Kept for
    // the TurnResult (the session layer emits it once as an `error` event).
    const error = str(m['error']);
    if (error) {
      const text = content.map((b) => str(rec(b)['text']) ?? '').join(' ').trim();
      this.lastError = { code: `claude_${error}`, message: text || `Claude reported ${error}.` };
    }
    return events;
  }

  private toolResults(m: Record<string, unknown>): EngineEvent[] {
    const message = rec(m['message']);
    const content: unknown[] = Array.isArray(message['content']) ? message['content'] : [];
    const results = content.map(rec).filter((b) => b['type'] === 'tool_result');
    const events: EngineEvent[] = [];
    for (const block of results) {
      const toolCallId = str(block['tool_use_id']);
      if (!toolCallId) continue;
      const ok = block['is_error'] !== true;
      events.push({ type: 'tool_result', data: { toolCallId, ok, output: capUtf8(stringifyToolContent(block['content'])) } });
      const tool = this.tools.get(toolCallId);
      const path = tool && FILE_WRITE_TOOLS.has(tool.name) ? writePathOf(tool.input) : undefined;
      if (ok && path) {
        // VERIFY-ON-PIN: Write's structured tool_use_result carries type 'create' | 'update'.
        const structured = results.length === 1 ? rec(m['tool_use_result']) : {};
        const changeKind = structured['type'] === 'create' ? 'add' : 'modify';
        events.push({ type: 'file_change', data: { path, changeKind } });
      }
    }
    return events;
  }
}

function summarizeResult(m: Record<string, unknown>): ClaudeResultSummary {
  const usage = rec(m['usage']);
  const errors: string[] = Array.isArray(m['errors']) ? m['errors'].filter((e): e is string => typeof e === 'string') : [];
  const totalCostUsd = num(m['total_cost_usd']);
  const resultText = str(m['result']);
  return {
    subtype: str(m['subtype']) ?? 'unknown',
    isError: m['is_error'] === true,
    ...(totalCostUsd !== undefined ? { totalCostUsd } : {}),
    inputTokens: num(usage['input_tokens']) ?? 0,
    outputTokens: num(usage['output_tokens']) ?? 0,
    cacheReadTokens: num(usage['cache_read_input_tokens']) ?? 0,
    errors,
    ...(resultText ? { resultText } : {}),
  };
}

/** result.subtype → TurnResult status. */
export function turnStatusFor(result: ClaudeResultSummary, interrupted: boolean): TurnResult['status'] {
  if (interrupted) return 'interrupted';
  if (result.subtype === 'success') return result.isError ? 'error' : 'ok';
  if (result.subtype === 'error_max_turns') return 'max_turns';
  return 'error';
}

// ─── Streaming input queue ─────────────────────────────────────────────────────────────────

/** Push-based AsyncIterable used as the query's prompt stream. */
export class AsyncQueue<T> implements AsyncIterable<T> {
  private readonly items: T[] = [];
  private readonly waiters: ((result: IteratorResult<T>) => void)[] = [];
  private closed = false;

  push(item: T): void {
    if (this.closed) throw new EngineError('engine_unavailable', 'The Claude session input is closed.');
    const waiter = this.waiters.shift();
    if (waiter) waiter({ value: item, done: false });
    else this.items.push(item);
  }

  close(): void {
    this.closed = true;
    for (const waiter of this.waiters.splice(0)) waiter({ value: undefined, done: true });
  }

  [Symbol.asyncIterator](): AsyncIterator<T> {
    return {
      next: () => {
        const item = this.items.shift();
        if (item !== undefined) return Promise.resolve({ value: item, done: false });
        if (this.closed) return Promise.resolve({ value: undefined, done: true });
        return new Promise((resolve) => this.waiters.push(resolve));
      },
      return: () => {
        this.close();
        return Promise.resolve({ value: undefined, done: true });
      },
    };
  }
}

function userMessage(text: string, sessionId: string | undefined): SDKUserMessage {
  return {
    type: 'user',
    message: { role: 'user', content: text },
    parent_tool_use_id: null,
    session_id: sessionId ?? '',
  } as SDKUserMessage;
}

async function withTimeout<T>(promise: Promise<T>, ms: number, what: string): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  try {
    return await Promise.race([
      promise,
      new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new EngineError('engine_unavailable', `${what} timed out after ${ms} ms`)), ms);
      }),
    ]);
  } finally {
    if (timer) clearTimeout(timer);
  }
}

// ─── Session ───────────────────────────────────────────────────────────────────────────────

const FORCE_STOP = Symbol('force-stop');

interface ResolvedConfig {
  executablePath: string;
  homeDir: string;
  configDir: string;
  maxTurnsPerTurn: number;
  hookTimeoutSec: number;
  interruptGraceMs: number;
  readOnlyAllowedTools: string[];
  logger: EngineLogger;
}

/** What a session needs from its adapter. */
interface ClaudeSessionHost {
  readonly config: ResolvedConfig;
  loadQuery(): Promise<QueryFn>;
  noteRateLimits(limits: RateLimit[]): void;
  /** Sticky: the CLI runs on a non-subscription credential (cleared by connect/logout). */
  noteAuthProblem(detail: string): void;
  /** A turn failed with an auth-looking error: re-read `claude auth status` on the next status(). */
  refreshAuth(): void;
  attach(session: ClaudeSession): void;
  detach(session: ClaudeSession): void;
  /** Running total_cost_usd already attributed per console session (survives idle reopen). */
  costBaseline(sessionId: string): number | undefined;
  setCostBaseline(sessionId: string, value: number): void;
}

class ActiveTurn {
  readonly emittedToolCalls = new Set<string>();
  readonly mapper = new ClaudeMessageMapper(this.emittedToolCalls);
  readonly forceStop: Promise<typeof FORCE_STOP>;
  interruptRequested = false;
  private release: (value: typeof FORCE_STOP) => void = () => undefined;
  private stopTimer: ReturnType<typeof setTimeout> | undefined;

  constructor(
    readonly hooks: EngineHooks,
    readonly signal: AbortSignal,
    readonly model: string,
  ) {
    this.forceStop = new Promise((resolve) => {
      this.release = resolve;
    });
  }

  armForceStop(ms: number): void {
    if (this.stopTimer) return;
    this.stopTimer = setTimeout(() => this.release(FORCE_STOP), ms);
    this.stopTimer.unref?.();
  }

  stopNow(): void {
    this.release(FORCE_STOP);
  }

  dispose(): void {
    if (this.stopTimer) clearTimeout(this.stopTimer);
  }
}

/**
 * One console session. The query stays open between turns; src/sessions.ts closes it after
 * the idle period (close()) and a later turn reopens it with `resume: <session_id>`.
 */
export class ClaudeSession implements EngineSession {
  readonly engine = 'claude' as const;
  private query: Query | undefined;
  private input: AsyncQueue<SDKUserMessage> | undefined;
  private sdkSessionId: string | undefined;
  private applied: { model?: string; effort?: string; mode?: Mode } = {};
  private turn: ActiveTurn | undefined;

  constructor(
    private readonly host: ClaudeSessionHost,
    private readonly opts: SessionEngineOptions,
  ) {
    this.sdkSessionId = opts.resumeId;
  }

  /** Engine-native id to persist (Claude session_id). */
  get resumeId(): string | undefined {
    return this.sdkSessionId;
  }

  async runTurn(text: string, opts: { model: string; effort?: string; mode: Mode }, hooks: EngineHooks, signal: AbortSignal): Promise<TurnResult> {
    if (this.turn) throw new EngineError('turn_in_progress', 'A turn is already running in this session.');
    const resume = (): { resumeId?: string } => (this.sdkSessionId ? { resumeId: this.sdkSessionId } : {});
    if (signal.aborted) return { status: 'interrupted', ...resume() };
    this.host.attach(this);
    const turn = new ActiveTurn(hooks, signal, opts.model);
    this.turn = turn;
    const onAbort = (): void => void this.interrupt();
    signal.addEventListener('abort', onAbort, { once: true });
    try {
      await this.prepare(opts);
      if (turn.interruptRequested) return { status: 'interrupted', ...resume() };
      const input = this.input;
      if (!input) throw new EngineError('engine_unavailable', 'Claude Code query is not open.');
      input.push(userMessage(text, this.sdkSessionId));
      return await this.pump(turn);
    } catch (err) {
      this.teardownQuery();
      if (turn.interruptRequested) return { status: 'interrupted', ...resume() };
      const code = err instanceof EngineError ? err.code : 'engine_error';
      this.host.config.logger.error('claude turn failed', { sessionId: this.opts.sessionId, code, message: safeErrorMessage(err) });
      return { status: 'error', ...resume(), error: { code, message: safeErrorMessage(err) } };
    } finally {
      signal.removeEventListener('abort', onAbort);
      turn.dispose();
      this.turn = undefined;
    }
  }

  async interrupt(): Promise<void> {
    const turn = this.turn;
    if (!turn) return;
    turn.interruptRequested = true;
    // Before the query is open the flag alone stops the turn (checked before sending).
    const query = this.query;
    if (!query) return;
    turn.armForceStop(this.host.config.interruptGraceMs);
    try {
      await query.interrupt();
    } catch (err) {
      this.host.config.logger.warn('claude interrupt failed', { sessionId: this.opts.sessionId, message: safeErrorMessage(err) });
      turn.stopNow();
    }
  }

  async close(): Promise<void> {
    if (this.turn) {
      this.turn.interruptRequested = true;
      this.turn.stopNow();
    }
    this.teardownQuery();
    this.host.detach(this);
  }

  // ── internals ──

  private async prepare(opts: { model: string; effort?: string; mode: Mode }): Promise<void> {
    if (!this.query) {
      await this.open(opts);
      return;
    }
    const query = this.query;
    if (opts.model !== this.applied.model) {
      await query.setModel(opts.model);
      this.applied.model = opts.model;
    }
    if (opts.effort && opts.effort !== this.applied.effort) {
      const flags = query as unknown as { applyFlagSettings?: (settings: Record<string, unknown>) => Promise<void> };
      if (typeof flags.applyFlagSettings === 'function') {
        await flags.applyFlagSettings({ effortLevel: opts.effort });
        this.applied.effort = opts.effort;
      } else {
        // Older SDK without live flag settings: reopen the query (resumes by session_id).
        this.teardownQuery();
        await this.open(opts);
        return;
      }
    }
    if (opts.mode !== this.applied.mode) {
      await query.setPermissionMode(permissionModeFor(opts.mode));
      this.applied.mode = opts.mode;
    }
  }

  private async open(opts: { model: string; effort?: string; mode: Mode }): Promise<void> {
    const cfg = this.host.config;
    const queryFn = await this.host.loadQuery();
    const input = new AsyncQueue<SDKUserMessage>();
    const options: Options = {
      pathToClaudeCodeExecutable: cfg.executablePath,
      cwd: this.opts.cwd,
      env: buildClaudeEnv(this.opts.env, cfg),
      settingSources: [],
      systemPrompt: { type: 'preset', preset: 'claude_code', append: this.opts.appendSystemPrompt },
      includePartialMessages: true,
      thinking: { type: 'adaptive', display: 'summarized' },
      model: opts.model,
      ...(opts.effort ? { effort: opts.effort as NonNullable<Options['effort']> } : {}),
      permissionMode: permissionModeFor(opts.mode),
      ...(opts.mode === 'read_only' ? { allowedTools: [...cfg.readOnlyAllowedTools] } : {}),
      disallowedTools: ['AskUserQuestion'],
      canUseTool: this.canUseTool,
      hooks: { PreToolUse: [{ hooks: [this.preToolUse], timeout: cfg.hookTimeoutSec }] },
      ...(this.sdkSessionId ? { resume: this.sdkSessionId } : {}),
      maxTurns: cfg.maxTurnsPerTurn,
      stderr: (data: string) => cfg.logger.info('claude stderr', { sessionId: this.opts.sessionId, line: data.slice(0, 500) }),
    };
    this.input = input;
    this.query = queryFn({ prompt: input, options });
    this.applied = { model: opts.model, mode: opts.mode, ...(opts.effort ? { effort: opts.effort } : {}) };
  }

  private async pump(turn: ActiveTurn): Promise<TurnResult> {
    const query = this.query;
    if (!query) throw new EngineError('engine_unavailable', 'Claude Code query is not open.');
    const resume = (): { resumeId?: string } => (this.sdkSessionId ? { resumeId: this.sdkSessionId } : {});
    for (;;) {
      const next = await Promise.race([query.next(), turn.forceStop]);
      if (next === FORCE_STOP) {
        this.teardownQuery();
        return { status: 'interrupted', ...resume() };
      }
      if (next.done) {
        this.teardownQuery();
        if (turn.interruptRequested) return { status: 'interrupted', ...resume() };
        return { status: 'error', ...resume(), error: { code: 'engine_exited', message: 'Claude Code exited before the turn finished.' } };
      }
      const mapped = turn.mapper.handle(next.value);
      if (mapped.sessionId) this.sdkSessionId = mapped.sessionId;
      const keyProblem = apiKeySourceProblem(mapped.init?.apiKeySource);
      if (keyProblem) {
        this.host.noteAuthProblem(keyProblem);
        this.teardownQuery();
        return { status: 'error', ...resume(), error: { code: 'engine_auth_error', message: keyProblem } };
      }
      if (mapped.rateLimits) this.host.noteRateLimits(mapped.rateLimits);
      for (const event of mapped.events) this.emit(turn, event);
      if (mapped.result) return this.complete(turn, mapped.result);
    }
  }

  private complete(turn: ActiveTurn, result: ClaudeResultSummary): TurnResult {
    // A fresh session starts at 0; a resumed one is unknown until its first result.
    const known = this.host.costBaseline(this.opts.sessionId);
    const { costUsd, baseline } = computeTurnCost(known ?? (this.opts.resumeId ? undefined : 0), result.totalCostUsd);
    if (baseline !== undefined) this.host.setCostBaseline(this.opts.sessionId, baseline);
    this.emit(turn, {
      type: 'usage',
      data: {
        model: turn.model,
        inputTokens: result.inputTokens,
        outputTokens: result.outputTokens,
        cacheReadTokens: result.cacheReadTokens,
        ...(costUsd !== undefined ? { costUsd } : {}),
      },
    });
    const status = turnStatusFor(result, turn.interruptRequested);
    const resume = this.sdkSessionId ? { resumeId: this.sdkSessionId } : {};
    // VERIFY-ON-PIN: if maxTurns counts across the whole streaming query rather than per user
    // message, a query that hit it would max out every later turn. Reopening (resume by
    // session_id) resets the counter either way and costs one CLI respawn.
    if (status === 'max_turns') this.teardownQuery();
    if (status !== 'error') return { status, ...resume };
    const apiError = turn.mapper.lastError;
    const message = result.errors[0] ?? apiError?.message ?? result.resultText ?? `Claude Code ended the turn with ${result.subtype}.`;
    if (/(\/login|oauth|authenticat|not logged in|invalid api key)/i.test(message)) this.host.refreshAuth();
    return { status, ...resume, error: { code: apiError?.code ?? `claude_${result.subtype}`, message: safeErrorMessage(message) } };
  }

  private emit(turn: ActiveTurn, event: EngineEvent): void {
    try {
      turn.hooks.emit(event);
    } catch (err) {
      this.host.config.logger.error('claude event sink failed', { sessionId: this.opts.sessionId, message: safeErrorMessage(err) });
    }
  }

  /** Guard gate for every tool call (main agent and subagents). */
  private readonly preToolUse: HookCallback = async (input, toolUseId, { signal }) => {
    const turn = this.turn;
    if (!turn) return hookOutputFor({ behavior: 'deny', message: 'No console turn is active.' });
    const request = toolCallRequestFromHook(input, toolUseId);
    if (!request) return {};
    if (!turn.emittedToolCalls.has(request.toolCallId)) {
      turn.emittedToolCalls.add(request.toolCallId);
      this.emit(turn, {
        type: 'tool_call',
        data: {
          toolCallId: request.toolCallId,
          name: request.name,
          input: request.input,
          ...(request.command !== undefined ? { command: request.command } : {}),
          ...(request.cwd ? { cwd: request.cwd } : {}),
        },
      });
    }
    let decision: ToolDecision;
    try {
      decision = await turn.hooks.onToolCall(request, AbortSignal.any([signal, turn.signal]));
    } catch (err) {
      this.host.config.logger.error('guard failed; denying tool call', {
        sessionId: this.opts.sessionId,
        tool: request.name,
        message: safeErrorMessage(err),
      });
      decision = { behavior: 'deny', message: 'The console guard could not decide on this call, so it was denied.' };
    }
    return hookOutputFor(decision);
  };

  /** Reached only for prompts the hook cannot pre-approve; never allows. */
  private readonly canUseTool: CanUseTool = async (toolName) => {
    this.host.config.logger.warn('claude permission prompt denied', { sessionId: this.opts.sessionId, tool: toolName });
    return { behavior: 'deny', message: toolName === 'AskUserQuestion' ? ASK_USER_DENIAL : CAN_USE_TOOL_DENIAL };
  };

  private teardownQuery(): void {
    this.input?.close();
    const query = this.query;
    this.query = undefined;
    this.input = undefined;
    this.applied = {};
    if (query) {
      try {
        query.close();
      } catch {
        // already closed
      }
    }
  }
}

// ─── Adapter ───────────────────────────────────────────────────────────────────────────────

class ClaudeAdapter implements EngineAdapter, ClaudeSessionHost {
  readonly engine = 'claude' as const;
  readonly config: ResolvedConfig;
  private readonly auth: ClaudeAuthManager;
  private readonly now: () => number;
  private readonly sessions = new Set<ClaudeSession>();
  private readonly rateLimits = new Map<string, RateLimit>();
  private readonly costBaselines = new Map<string, number>();
  private authStatusCache: { at: number; value: ClaudeAuthStatus } | undefined;
  private probeCache: { at: number; models: ModelInfo[]; account: unknown } | undefined;
  private probeInFlight: Promise<{ models: ModelInfo[]; account: unknown }> | undefined;
  private versionCache: string | null | undefined;
  private authProblem: string | undefined;
  private queryFn: QueryFn | undefined;

  constructor(private readonly options: ClaudeAdapterSettings) {
    this.now = options.now ?? Date.now;
    this.config = {
      executablePath: options.executablePath ?? CLAUDE_DEFAULTS.executablePath,
      homeDir: options.homeDir,
      configDir: options.configDir,
      maxTurnsPerTurn: options.maxTurnsPerTurn ?? CLAUDE_DEFAULTS.maxTurnsPerTurn,
      hookTimeoutSec: options.hookTimeoutSec ?? CLAUDE_DEFAULTS.hookTimeoutSec,
      interruptGraceMs: options.interruptGraceMs ?? CLAUDE_DEFAULTS.interruptGraceMs,
      readOnlyAllowedTools: options.readOnlyAllowedTools ?? [...CLAUDE_DEFAULTS.readOnlyAllowedTools],
      logger: options.logger,
    };
    this.queryFn = options.queryFn;
    this.auth = new ClaudeAuthManager(
      {
        asAgentPath: options.asAgentPath,
        claudeBinPath: options.claudeBinPath ?? CLAUDE_DEFAULTS.claudeBinPath,
        homeDir: this.config.homeDir,
        env: () => buildClaudeEnv(this.options.baseEnv(), this.config),
      },
      {
        now: this.now,
        onAuthChanged: () => this.invalidateAuth(),
        ...(options.runner ? { runner: options.runner } : {}),
        ...(options.spawnPty ? { spawnPty: options.spawnPty } : {}),
      },
    );
  }

  // ── EngineAdapter ──

  async status(): Promise<EngineStatus> {
    const version = await this.version();
    const rateLimits = this.rateLimits.size > 0 ? [...this.rateLimits.values()] : null;
    const base = { engine: 'claude' as const, version, rateLimits };
    let authStatus: ClaudeAuthStatus;
    try {
      authStatus = await this.readAuthStatus();
    } catch (err) {
      return { ...base, auth: { state: 'error', detail: `Could not read Claude Code sign-in status: ${safeErrorMessage(err)}` }, models: [] };
    }
    if (!authStatus.loggedIn) {
      return { ...base, auth: { state: this.auth.hasActiveFlow() ? 'signing_in' : 'signed_out' }, models: [] };
    }
    let probe: { models: ModelInfo[]; account: unknown };
    try {
      probe = await this.probe();
    } catch (err) {
      return { ...base, auth: { state: 'error', detail: `Claude Code did not answer the model/account probe: ${safeErrorMessage(err)}` }, models: [] };
    }
    let auth = assessClaudeAuth(authStatus, probe.account);
    if (auth.state === 'signed_in' && this.authProblem) auth = { ...auth, state: 'error', detail: this.authProblem };
    return { ...base, auth, models: auth.state === 'signed_in' ? probe.models : [] };
  }

  async openSession(opts: SessionEngineOptions): Promise<EngineSession> {
    const status = await this.status();
    if (status.auth.state !== 'signed_in') {
      throw new EngineError(
        status.auth.state === 'error' ? 'engine_auth_error' : 'engine_not_signed_in',
        status.auth.detail ?? 'Claude Code is not signed in with a Claude subscription. Connect Claude Max first.',
      );
    }
    return new ClaudeSession(this, opts);
  }

  connect(): Promise<ConnectFlow> {
    return this.auth.connect();
  }

  getFlow(flowId: string): ConnectFlow | undefined {
    return this.auth.getFlow(flowId);
  }

  submitCode(flowId: string, code: string): Promise<ConnectFlow> {
    return this.auth.submitCode(flowId, code);
  }

  cancel(flowId: string): Promise<ConnectFlow> {
    return this.auth.cancel(flowId);
  }

  async logout(): Promise<EngineAuth> {
    for (const session of [...this.sessions]) await session.close();
    await this.auth.logout();
    this.invalidateAuth();
    return (await this.status()).auth;
  }

  async shutdown(): Promise<void> {
    this.auth.shutdown();
    await Promise.all([...this.sessions].map((s) => s.close()));
  }

  // ── ClaudeSessionHost ──

  async loadQuery(): Promise<QueryFn> {
    if (!this.queryFn) {
      const sdk = await import('@anthropic-ai/claude-agent-sdk');
      this.queryFn = sdk.query;
    }
    return this.queryFn;
  }

  noteRateLimits(limits: RateLimit[]): void {
    for (const limit of limits) this.rateLimits.set(limit.label, limit);
  }

  noteAuthProblem(detail: string): void {
    this.authProblem = detail;
    this.refreshAuth();
  }

  refreshAuth(): void {
    this.authStatusCache = undefined;
    this.probeCache = undefined;
  }

  attach(session: ClaudeSession): void {
    this.sessions.add(session);
  }

  detach(session: ClaudeSession): void {
    this.sessions.delete(session);
  }

  costBaseline(sessionId: string): number | undefined {
    return this.costBaselines.get(sessionId);
  }

  setCostBaseline(sessionId: string, value: number): void {
    this.costBaselines.set(sessionId, value);
  }

  // ── internals ──

  private invalidateAuth(): void {
    this.authProblem = undefined;
    this.authStatusCache = undefined;
    this.probeCache = undefined;
  }

  private async version(): Promise<string | null> {
    if (this.versionCache === undefined) {
      try {
        this.versionCache = await this.auth.version();
      } catch {
        return null;
      }
    }
    return this.versionCache;
  }

  private async readAuthStatus(): Promise<ClaudeAuthStatus> {
    const ttl = this.options.authStatusCacheMs ?? CLAUDE_DEFAULTS.authStatusCacheMs;
    if (this.authStatusCache && this.now() - this.authStatusCache.at < ttl) return this.authStatusCache.value;
    const value = await this.auth.readStatus();
    this.authStatusCache = { at: this.now(), value };
    return value;
  }

  /** supportedModels() + accountInfo() from a short-lived, prompt-less query (cached 10 min). */
  private async probe(): Promise<{ models: ModelInfo[]; account: unknown }> {
    const ttl = this.options.modelCacheMs ?? CLAUDE_DEFAULTS.modelCacheMs;
    if (this.probeCache && this.now() - this.probeCache.at < ttl) return this.probeCache;
    this.probeInFlight ??= this.runProbe().finally(() => {
      this.probeInFlight = undefined;
    });
    const result = await this.probeInFlight;
    this.probeCache = { at: this.now(), ...result };
    return result;
  }

  private async runProbe(): Promise<{ models: ModelInfo[]; account: unknown }> {
    const queryFn = await this.loadQuery();
    const input = new AsyncQueue<SDKUserMessage>();
    const query = queryFn({
      prompt: input,
      options: {
        pathToClaudeCodeExecutable: this.config.executablePath,
        cwd: this.config.homeDir,
        env: buildClaudeEnv(this.options.baseEnv(), this.config),
        settingSources: [],
        persistSession: false,
        permissionMode: 'dontAsk',
        maxTurns: 1,
      },
    });
    const drain = (async () => {
      try {
        for await (const message of query) void message;
      } catch {
        // closed below
      }
    })();
    try {
      const [models, account] = await withTimeout(
        Promise.all([query.supportedModels(), query.accountInfo()]),
        this.options.probeTimeoutMs ?? CLAUDE_DEFAULTS.probeTimeoutMs,
        'Claude Code probe',
      );
      return { models: mapClaudeModels(models), account };
    } finally {
      input.close();
      try {
        query.close();
      } catch {
        // already closed
      }
      void drain;
    }
  }
}


interface ClaudeAdapterSettings extends ClaudeAdapterOverrides {
  baseEnv: () => Record<string, string>;
  asAgentPath: string;
  homeDir: string;
  configDir: string;
  logger: EngineLogger;
}

/**
 * Factory used by src/engine-registry.ts. Paths come from AppConfig (CONTRACT §2 layout);
 * the child env for sign-in/status/probe processes is src/env.ts's allow-listed agent env.
 */
export function createClaudeAdapter(
  config: AppConfig,
  context?: EngineFactoryContext,
  overrides: ClaudeAdapterOverrides = {},
): EngineAdapter {
  return new ClaudeAdapter({
    ...overrides,
    baseEnv: overrides.baseEnv ?? (() => context?.buildEnv() ?? {}),
    asAgentPath: config.asAgentPath,
    homeDir: config.agentHome,
    configDir: config.claudeConfigDir,
    logger: overrides.logger ?? (context ? toEngineLogger(context.logger, 'engine.claude') : consoleEngineLogger('engine.claude')),
  });
}
