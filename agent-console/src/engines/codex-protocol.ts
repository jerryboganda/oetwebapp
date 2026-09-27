// Wire protocol for `codex app-server` (JSON-RPC 2.0 semantics, newline-delimited JSON over
// stdio, the "jsonrpc" member omitted on the wire) plus the pure mappers from its v2 thread /
// turn / item / account messages to the engine-neutral shapes in ./types.ts.
//
// Everything here is side-effect free so it can be unit-tested with fixture objects. Shapes
// were checked against the published app-server docs (learn.chatgpt.com/docs/app-server) and
// openai/codex PR #15525 (device-code login). Where the docs are silent or ambiguous the
// assumption is isolated in one small function marked VERIFY-ON-PIN: re-check it against
// `codex app-server generate-ts` output of the pinned @openai/codex version before bumping.

import { HttpError } from '../errors.js';
import type { Logger } from '../log.js';
import type { EngineAuth, EngineEvent, ModelInfo, RateLimit, ToolCallRequest, ToolDecision } from './types.js';

// ─── Engine-neutral helpers (also used by the Claude adapter) ─────────────────────────────

const ENGINE_ERROR_STATUS: Record<string, number> = {
  engine_not_signed_in: 409,
  engine_auth_error: 409,
  turn_in_progress: 409,
};

/**
 * Error thrown by an adapter to the session layer; an HttpError so an unhandled one still
 * becomes a CONTRACT §3 envelope. `code` is stable (engine_not_signed_in, engine_auth_error,
 * turn_in_progress, engine_unavailable, codex_policy_tampered, ...); `message` is safe to
 * show the owner (never credentials or raw payloads).
 */
export class EngineError extends HttpError {
  constructor(code: string, message: string) {
    super(ENGINE_ERROR_STATUS[code] ?? 500, code, message);
    this.name = 'EngineError';
  }
}

/** Minimal logging surface the adapters use (message first, like console). */
export interface EngineLogger {
  info(message: string, meta?: Record<string, unknown>): void;
  warn(message: string, meta?: Record<string, unknown>): void;
  error(message: string, meta?: Record<string, unknown>): void;
}

/** Adapts the sidecar's pino logger (object first, message second). */
export function toEngineLogger(logger: Logger, component: string): EngineLogger {
  return {
    info: (message, meta) => logger.info({ component, ...meta }, message),
    warn: (message, meta) => logger.warn({ component, ...meta }, message),
    error: (message, meta) => logger.error({ component, ...meta }, message),
  };
}

/** Logger used when no context logger is supplied (tests / scripts): warnings and errors only. */
export function consoleEngineLogger(component: string): EngineLogger {
  const line = (level: string, message: string, meta?: Record<string, unknown>): string =>
    JSON.stringify({ level, component, message, ...meta });
  return {
    info: () => undefined,
    warn: (message, meta) => console.warn(line('warn', message, meta)),
    error: (message, meta) => console.error(line('error', message, meta)),
  };
}

/** First line of an error message, bounded, for owner-facing details and logs. */
export function safeErrorMessage(error: unknown, max = 300): string {
  const raw = error instanceof Error ? error.message : typeof error === 'string' ? error : 'unknown error';
  const line = raw.split('\n')[0] ?? '';
  return line.length > max ? `${line.slice(0, max - 1)}…` : line;
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function str(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined;
}

function nonEmpty(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function num(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

function rec(value: unknown): Record<string, unknown> {
  return isRecord(value) ? value : {};
}

/** Max bytes of tool output carried by a `tool_result` / streamed as deltas (CONTRACT §4). */
export const TOOL_OUTPUT_CAP_BYTES = 64 * 1024;

/** Truncate to at most `maxBytes` UTF-8 bytes, appending a marker when anything was cut. */
export function capUtf8(text: string, maxBytes = TOOL_OUTPUT_CAP_BYTES): string {
  const size = Buffer.byteLength(text, 'utf8');
  if (size <= maxBytes) return text;
  const head = Buffer.from(text, 'utf8').subarray(0, maxBytes).toString('utf8');
  return `${head}\n…[truncated ${size - maxBytes} bytes]`;
}

/** Unix seconds (or milliseconds) → ISO-8601; undefined for anything else. */
export function epochToIso(value: unknown): string | undefined {
  const n = num(value);
  if (n === undefined || n <= 0) return undefined;
  const ms = n < 1e12 ? n * 1000 : n;
  const date = new Date(ms);
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString();
}

// ─── Framing ─────────────────────────────────────────────────────────────────────────────

export type JsonRpcId = number | string;

export interface JsonRpcErrorObject {
  code: number;
  message: string;
  data?: unknown;
}

export type IncomingMessage =
  | { kind: 'request'; id: JsonRpcId; method: string; params: unknown }
  | { kind: 'notification'; method: string; params: unknown }
  | { kind: 'response'; id: JsonRpcId; result?: unknown; error?: JsonRpcErrorObject }
  | { kind: 'invalid'; reason: string };

/** Standard JSON-RPC error codes used in replies to the server. */
export const RPC_METHOD_NOT_FOUND = -32601;
export const RPC_INTERNAL_ERROR = -32603;

/** Serialises one outgoing message as a single JSONL line (no "jsonrpc" member). */
export function encodeMessage(message: Record<string, unknown>): string {
  return `${JSON.stringify(message)}\n`;
}

function isId(value: unknown): value is JsonRpcId {
  return (typeof value === 'number' && Number.isFinite(value)) || typeof value === 'string';
}

/** Classifies one parsed JSON value received from the app-server. */
export function classifyMessage(raw: unknown): IncomingMessage {
  if (!isRecord(raw)) return { kind: 'invalid', reason: 'not an object' };
  const method = raw['method'];
  const id = raw['id'];
  if (typeof method === 'string') {
    if (isId(id)) return { kind: 'request', id, method, params: raw['params'] };
    return { kind: 'notification', method, params: raw['params'] };
  }
  if (isId(id) && ('result' in raw || 'error' in raw)) {
    const error = raw['error'];
    if (error !== undefined && error !== null) {
      const e = rec(error);
      return {
        kind: 'response',
        id,
        error: {
          code: num(e['code']) ?? RPC_INTERNAL_ERROR,
          message: str(e['message']) ?? 'unknown error',
          ...(e['data'] !== undefined ? { data: e['data'] } : {}),
        },
      };
    }
    return { kind: 'response', id, result: raw['result'] };
  }
  return { kind: 'invalid', reason: 'neither request, notification nor response' };
}

/**
 * Splits a byte/char stream into complete lines. Tolerates CRLF, chunks split anywhere and
 * blank lines. A line longer than `maxLineBytes` is dropped (reported via `onOversize`) so a
 * misbehaving child cannot make the control plane buffer without bound.
 */
export class LineDecoder {
  private buffer = '';
  private discarding = false;

  constructor(
    private readonly maxLineBytes = 32 * 1024 * 1024,
    private readonly onOversize: (bytes: number) => void = () => undefined,
  ) {}

  push(chunk: string | Buffer): string[] {
    this.buffer += typeof chunk === 'string' ? chunk : chunk.toString('utf8');
    const lines: string[] = [];
    let newline = this.buffer.indexOf('\n');
    while (newline !== -1) {
      const line = this.buffer.slice(0, newline);
      this.buffer = this.buffer.slice(newline + 1);
      if (this.discarding) {
        this.discarding = false;
      } else {
        const trimmed = line.endsWith('\r') ? line.slice(0, -1) : line;
        if (trimmed.trim().length > 0) lines.push(trimmed);
      }
      newline = this.buffer.indexOf('\n');
    }
    if (this.buffer.length > this.maxLineBytes) {
      this.onOversize(this.buffer.length);
      this.buffer = '';
      this.discarding = true;
    }
    return lines;
  }

  /** Returns a trailing unterminated line, if any (call when the stream ends). */
  flush(): string[] {
    const rest = this.discarding ? '' : this.buffer.trim();
    this.buffer = '';
    this.discarding = false;
    return rest.length > 0 ? [rest] : [];
  }
}

// ─── Connection (transport-agnostic request/response correlation) ─────────────────────────

export class RpcError extends Error {
  constructor(
    readonly code: number,
    message: string,
    readonly data?: unknown,
  ) {
    super(message);
    this.name = 'RpcError';
  }
}

export interface RpcConnectionOptions {
  /** Writes one encoded line to the peer. */
  write(line: string): void;
  onNotification?(method: string, params: unknown): void;
  /** Handles a server → client request; resolve with the `result`, throw RpcError to reply with an error. */
  onRequest?(method: string, params: unknown, id: JsonRpcId): Promise<unknown>;
  /** Called for lines that are not valid JSON-RPC (logged by the owner). */
  onInvalid?(reason: string): void;
  /** Default timeout for client → server requests. */
  requestTimeoutMs?: number;
}

interface PendingRequest {
  method: string;
  resolve(value: unknown): void;
  reject(error: Error): void;
  cleanup(): void;
}

/** Correlates requests and responses over any line transport (stdio child in production). */
export class RpcConnection {
  private nextId = 1;
  private readonly pending = new Map<JsonRpcId, PendingRequest>();
  private closedError: Error | undefined;

  constructor(private readonly options: RpcConnectionOptions) {}

  get pendingCount(): number {
    return this.pending.size;
  }

  request<T = unknown>(
    method: string,
    params?: unknown,
    opts: { timeoutMs?: number; signal?: AbortSignal } = {},
  ): Promise<T> {
    if (this.closedError) return Promise.reject(this.closedError);
    if (opts.signal?.aborted) return Promise.reject(new RpcError(RPC_INTERNAL_ERROR, `${method} aborted`));
    const id = this.nextId++;
    const timeoutMs = opts.timeoutMs ?? this.options.requestTimeoutMs ?? 60_000;
    return new Promise<T>((resolve, reject) => {
      const settleWithError = (message: string): void => {
        const current = this.pending.get(id);
        if (!current) return;
        current.cleanup();
        this.pending.delete(id);
        reject(new RpcError(RPC_INTERNAL_ERROR, message));
      };
      const onAbort = (): void => settleWithError(`${method} aborted`);
      const timer = timeoutMs > 0 ? setTimeout(() => settleWithError(`${method} timed out after ${timeoutMs} ms`), timeoutMs) : undefined;
      const entry: PendingRequest = {
        method,
        resolve: (value) => resolve(value as T),
        reject,
        cleanup: () => {
          if (timer) clearTimeout(timer);
          opts.signal?.removeEventListener('abort', onAbort);
        },
      };
      opts.signal?.addEventListener('abort', onAbort, { once: true });
      this.pending.set(id, entry);
      try {
        this.options.write(encodeMessage({ id, method, ...(params !== undefined ? { params } : {}) }));
      } catch (err) {
        entry.cleanup();
        this.pending.delete(id);
        reject(err instanceof Error ? err : new Error(String(err)));
      }
    });
  }

  notify(method: string, params?: unknown): void {
    if (this.closedError) return;
    this.options.write(encodeMessage({ method, ...(params !== undefined ? { params } : {}) }));
  }

  /** Feeds one received line (already split by LineDecoder). */
  handleLine(line: string): void {
    let parsed: unknown;
    try {
      parsed = JSON.parse(line);
    } catch {
      this.options.onInvalid?.('unparseable JSON line');
      return;
    }
    const message = classifyMessage(parsed);
    switch (message.kind) {
      case 'response': {
        const entry = this.pending.get(message.id);
        if (!entry) return;
        this.pending.delete(message.id);
        entry.cleanup();
        if (message.error) entry.reject(new RpcError(message.error.code, message.error.message, message.error.data));
        else entry.resolve(message.result);
        return;
      }
      case 'notification':
        this.options.onNotification?.(message.method, message.params);
        return;
      case 'request':
        void this.answer(message.id, message.method, message.params);
        return;
      case 'invalid':
        this.options.onInvalid?.(message.reason);
        return;
    }
  }

  /** Rejects every in-flight request (child exited) and refuses new ones. */
  close(error: Error): void {
    this.closedError = error;
    for (const [id, entry] of this.pending) {
      entry.cleanup();
      entry.reject(error);
      this.pending.delete(id);
    }
  }

  private async answer(id: JsonRpcId, method: string, params: unknown): Promise<void> {
    let reply: Record<string, unknown>;
    if (!this.options.onRequest) {
      reply = { id, error: { code: RPC_METHOD_NOT_FOUND, message: `unsupported method ${method}` } };
    } else {
      try {
        const result = await this.options.onRequest(method, params, id);
        reply = { id, result: result ?? {} };
      } catch (err) {
        const code = err instanceof RpcError ? err.code : RPC_INTERNAL_ERROR;
        const message = err instanceof Error ? err.message : 'request handler failed';
        reply = { id, error: { code, message } };
      }
    }
    if (this.closedError) return;
    try {
      this.options.write(encodeMessage(reply));
    } catch {
      // The child is gone; close() will be called by the owner.
    }
  }
}

// ─── Request builders ────────────────────────────────────────────────────────────────────

/**
 * Policy values sent on every thread and turn. Never `never`, never a read-only sandbox: the
 * Guard (via approval requests) is the gate, and the uid/egress/docker/DB boundaries are the
 * enforcement. Wire spellings follow the generated v2 schema
 * (codex-rs/app-server-protocol/schema/typescript/v2): `AskForApproval` is kebab-case
 * ("untrusted" = the Rust UnlessTrusted variant), `SandboxMode` (thread/start|resume `sandbox`)
 * is kebab-case ("danger-full-access"), while the tagged `SandboxPolicy` (turn/start
 * `sandboxPolicy`) uses camelCase tags ({ type: "dangerFullAccess" }). Re-check on every bump.
 */
export const CODEX_APPROVAL_POLICY = 'untrusted';
export const CODEX_SANDBOX_MODE = 'danger-full-access';
export const CODEX_SANDBOX_POLICY = { type: 'dangerFullAccess' } as const;
/** Reasoning summaries are what the console shows as "thinking". */
export const CODEX_REASONING_SUMMARY = 'auto';

export function buildInitializeParams(version: string): Record<string, unknown> {
  return { clientInfo: { name: 'oet_agent_console', title: 'OET Owner Agent Console', version } };
}

/**
 * The console's session instructions (operating manual, session block, worktree AGENTS.md,
 * handoff summary — built by src/sessions.ts) travel as `developerInstructions`, mirroring the
 * Claude system-prompt append (v2 ThreadStartParams / ThreadResumeParams.developerInstructions).
 */
export function buildThreadStartParams(opts: { cwd: string; model: string; developerInstructions?: string }): Record<string, unknown> {
  // No per-thread `config` overrides: openai/codex#45361 (turns hang after any override).
  return {
    model: opts.model,
    cwd: opts.cwd,
    approvalPolicy: CODEX_APPROVAL_POLICY,
    sandbox: CODEX_SANDBOX_MODE,
    ...(opts.developerInstructions ? { developerInstructions: opts.developerInstructions } : {}),
  };
}

/** v2 ThreadResumeParams accepts the same model/cwd/approvalPolicy/sandbox/developerInstructions overrides. */
export function buildThreadResumeParams(opts: { threadId: string; cwd: string; model: string; developerInstructions?: string }): Record<string, unknown> {
  return {
    threadId: opts.threadId,
    model: opts.model,
    cwd: opts.cwd,
    approvalPolicy: CODEX_APPROVAL_POLICY,
    sandbox: CODEX_SANDBOX_MODE,
    ...(opts.developerInstructions ? { developerInstructions: opts.developerInstructions } : {}),
  };
}

export function buildTurnStartParams(opts: {
  threadId: string;
  text: string;
  cwd: string;
  model: string;
  effort?: string;
}): Record<string, unknown> {
  return {
    threadId: opts.threadId,
    input: [{ type: 'text', text: opts.text }],
    cwd: opts.cwd,
    model: opts.model,
    ...(opts.effort ? { effort: opts.effort } : {}),
    summary: CODEX_REASONING_SUMMARY,
    approvalPolicy: CODEX_APPROVAL_POLICY,
    sandboxPolicy: CODEX_SANDBOX_POLICY,
  };
}

export function readThreadId(result: unknown): string | undefined {
  return nonEmpty(rec(rec(result)['thread'])['id']);
}

export function readTurnId(result: unknown): string | undefined {
  return nonEmpty(rec(rec(result)['turn'])['id']);
}

// ─── Models ──────────────────────────────────────────────────────────────────────────────

/** model/list page → ModelInfo[] (hidden models dropped; ids stay opaque). */
export function mapModelList(result: unknown): { models: ModelInfo[]; nextCursor: string | null } {
  const r = rec(result);
  const data: unknown[] = Array.isArray(r['data']) ? r['data'] : [];
  const models: ModelInfo[] = [];
  for (const raw of data) {
    const m = rec(raw);
    if (m['hidden'] === true) continue;
    const value = nonEmpty(m['model']) ?? nonEmpty(m['id']);
    if (!value) continue;
    const efforts: string[] = [];
    const supported: unknown[] = Array.isArray(m['supportedReasoningEfforts']) ? m['supportedReasoningEfforts'] : [];
    for (const entry of supported) {
      // v2 ReasoningEffortOption is { reasoningEffort, description }; plain strings tolerated.
      const effort = typeof entry === 'string' ? entry : nonEmpty(rec(entry)['reasoningEffort']);
      if (effort && !efforts.includes(effort)) efforts.push(effort);
    }
    const defaultEffort = nonEmpty(m['defaultReasoningEffort']);
    const description = nonEmpty(m['description']);
    models.push({
      value,
      displayName: nonEmpty(m['displayName']) ?? value,
      ...(description ? { description } : {}),
      supportsEffort: efforts.length > 0,
      efforts,
      ...(defaultEffort && efforts.includes(defaultEffort) ? { defaultEffort } : {}),
    });
  }
  const cursor = r['nextCursor'];
  return { models, nextCursor: typeof cursor === 'string' && cursor.length > 0 ? cursor : null };
}

// ─── Rate limits ─────────────────────────────────────────────────────────────────────────

function windowLabel(mins: number | undefined, fallback: string): string {
  if (mins === undefined || mins <= 0) return fallback;
  if (mins % 1440 === 0) return `${mins / 1440}-day window`;
  if (mins % 60 === 0) return `${mins / 60}-hour window`;
  return `${mins}-minute window`;
}

export function rateLimitStatus(usedPercent: number | undefined): RateLimit['status'] {
  if (usedPercent === undefined) return 'unknown';
  if (usedPercent >= 100) return 'limited';
  if (usedPercent >= 80) return 'warning';
  return 'ok';
}

/** v2 RateLimitSnapshot { primary, secondary: RateLimitWindow { usedPercent, windowDurationMins, resetsAt } | null } → RateLimit[]. */
export function mapRateLimitSnapshot(snapshot: unknown): RateLimit[] {
  const s = rec(snapshot);
  const limits: RateLimit[] = [];
  for (const [key, fallback] of [
    ['primary', 'Primary window'],
    ['secondary', 'Secondary window'],
  ] as const) {
    const w = s[key];
    if (!isRecord(w)) continue;
    const usedPercent = num(w['usedPercent']);
    const resetsAt = epochToIso(w['resetsAt']);
    limits.push({
      label: windowLabel(num(w['windowDurationMins']), fallback),
      status: rateLimitStatus(usedPercent),
      ...(usedPercent !== undefined ? { usedPercent: Math.round(usedPercent) } : {}),
      ...(resetsAt ? { resetsAt } : {}),
    });
  }
  return limits;
}

/** account/rateLimits/read result or account/rateLimits/updated params → RateLimit[]. */
export function mapRateLimitsPayload(payload: unknown): RateLimit[] {
  return mapRateLimitSnapshot(rec(payload)['rateLimits']);
}

// ─── Account / login ─────────────────────────────────────────────────────────────────────

/** account/read → EngineAuth. API-key auth is a hard error: the console only runs on ChatGPT sign-in. */
export function mapAccountRead(result: unknown): EngineAuth {
  const r = rec(result);
  const account = r['account'];
  if (!isRecord(account)) return { state: 'signed_out' };
  const type = str(account['type']);
  if (type === 'chatgpt') {
    const email = nonEmpty(account['email']);
    const plan = nonEmpty(account['planType']);
    return {
      state: 'signed_in',
      ...(email || plan ? { account: { ...(email ? { email } : {}), ...(plan ? { plan } : {}) } } : {}),
    };
  }
  if (type === 'apiKey') {
    return {
      state: 'error',
      detail: 'Codex is using API-key auth; the console only runs on ChatGPT sign-in. Log out and connect ChatGPT.',
    };
  }
  return { state: 'error', detail: `Codex reports an unsupported auth mode (${type ?? 'unknown'}).` };
}

const CODEX_VERIFICATION_HOSTS = ['auth.openai.com', 'chatgpt.com', 'openai.com'];

export function isAllowedVerificationUrl(url: string): boolean {
  try {
    const u = new URL(url);
    if (u.protocol !== 'https:') return false;
    return CODEX_VERIFICATION_HOSTS.some((h) => u.hostname === h || u.hostname.endsWith(`.${h}`));
  } catch {
    return false;
  }
}

export interface DeviceCodeStart {
  loginId: string;
  verificationUrl: string;
  userCode: string;
  expiresAt?: string;
}

/** account/login/start {type:'chatgptDeviceCode'} result → DeviceCodeStart (throws on anything else). */
export function parseDeviceCodeStart(result: unknown): DeviceCodeStart {
  const r = rec(result);
  const type = str(r['type']);
  if (type !== undefined && type !== 'chatgptDeviceCode') {
    throw new Error(`unexpected login response type ${type}`);
  }
  const loginId = nonEmpty(r['loginId']);
  const verificationUrl = nonEmpty(r['verificationUrl']);
  const userCode = nonEmpty(r['userCode']);
  if (!loginId || !verificationUrl || !userCode) throw new Error('device-code login response is missing fields');
  if (!isAllowedVerificationUrl(verificationUrl)) throw new Error('device-code verification URL is not an OpenAI host');
  // VERIFY-ON-PIN: no expiry field is documented; accept one if present.
  const expiresAt = epochToIso(r['expiresAt']) ?? (typeof r['expiresAt'] === 'string' ? r['expiresAt'] : undefined);
  return { loginId, verificationUrl, userCode, ...(expiresAt ? { expiresAt } : {}) };
}

export function parseLoginCompleted(params: unknown): { loginId?: string; success: boolean; error?: string } {
  const p = rec(params);
  const loginId = nonEmpty(p['loginId']);
  const error = nonEmpty(p['error']);
  return { ...(loginId ? { loginId } : {}), success: p['success'] === true, ...(error ? { error } : {}) };
}

/** Pulls the CLI version out of initialize's userAgent ("codex_cli_rs/0.157.1 (...)"). VERIFY-ON-PIN. */
export function parseUserAgentVersion(userAgent: unknown): string | null {
  const ua = str(userAgent);
  if (!ua) return null;
  const match = /(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)/.exec(ua);
  return match?.[1] ?? null;
}

// ─── Commands ────────────────────────────────────────────────────────────────────────────

/** Minimal POSIX-style tokenizer (quotes + backslashes) for display commands. */
export function tokenizeShell(command: string): string[] | undefined {
  const tokens: string[] = [];
  let current = '';
  let inToken = false;
  let quote: '"' | "'" | null = null;
  for (let i = 0; i < command.length; i++) {
    const ch = command.charAt(i);
    if (quote === "'") {
      if (ch === "'") quote = null;
      else current += ch;
      continue;
    }
    if (quote === '"') {
      if (ch === '"') quote = null;
      else if (ch === '\\' && i + 1 < command.length && '"\\$`'.includes(command.charAt(i + 1))) current += command.charAt(++i);
      else current += ch;
      continue;
    }
    if (ch === "'" || ch === '"') {
      quote = ch;
      inToken = true;
    } else if (ch === '\\' && i + 1 < command.length) {
      current += command.charAt(++i);
      inToken = true;
    } else if (/\s/.test(ch)) {
      if (inToken) tokens.push(current);
      current = '';
      inToken = false;
    } else {
      current += ch;
      inToken = true;
    }
  }
  if (quote) return undefined;
  if (inToken) tokens.push(current);
  return tokens;
}

function shellQuote(token: string): string {
  return /^[A-Za-z0-9_@%+=:,./-]+$/.test(token) ? token : `'${token.replace(/'/g, `'\\''`)}'`;
}

const SHELL_WRAPPERS = new Set(['bash', 'sh', 'zsh', '/bin/bash', '/bin/sh', '/usr/bin/bash', '/bin/zsh', '/usr/bin/zsh']);

/**
 * Codex runs model commands as `<shell> -lc <script>`. The wrapper is Codex's execution
 * mechanism, not the model's choice, so exactly one level is unwrapped for the Guard; a
 * script that itself wraps a shell stays wrapped (and is classified as unparseable).
 * v2 sends `command` as a display string (CommandExecutionRequestApprovalParams / ThreadItem); v1 sent argv.
 */
export function normalizeCommand(command: unknown): string | undefined {
  let argv: string[] | undefined;
  if (Array.isArray(command)) {
    argv = command.filter((t): t is string => typeof t === 'string');
  } else if (typeof command === 'string') {
    if (command.trim().length === 0) return undefined;
    argv = tokenizeShell(command);
    if (!argv) return command;
  } else {
    return undefined;
  }
  const [shell, flag, script] = argv;
  if (argv.length === 3 && shell && SHELL_WRAPPERS.has(shell) && (flag === '-lc' || flag === '-c') && script !== undefined) {
    return script;
  }
  if (typeof command === 'string') return command;
  return argv.map(shellQuote).join(' ');
}

// ─── Approvals ───────────────────────────────────────────────────────────────────────────

export const APPROVAL_METHODS = {
  command: 'item/commandExecution/requestApproval',
  fileChange: 'item/fileChange/requestApproval',
  /** Legacy (v1) names, answered defensively so a stray request never hangs a turn. */
  legacyExec: 'execCommandApproval',
  legacyPatch: 'applyPatchApproval',
} as const;

export function isApprovalMethod(method: string): boolean {
  return (Object.values(APPROVAL_METHODS) as string[]).includes(method);
}

/**
 * Maps a Guard decision onto the app-server reply. `acceptForSession` is only used when the
 * session layer explicitly attaches `scope: 'session'` to an allow (forward-compatible with a
 * scoped ToolDecision); by default every approval is one-shot so the Guard sees every call.
 */
export function approvalResponse(method: string, decision: ToolDecision | 'cancel'): Record<string, unknown> {
  const legacy = method === APPROVAL_METHODS.legacyExec || method === APPROVAL_METHODS.legacyPatch;
  if (decision === 'cancel') return { decision: legacy ? 'abort' : 'cancel' };
  if (decision.behavior === 'allow') {
    const scope = (decision as { scope?: unknown }).scope;
    if (scope === 'session') return { decision: legacy ? 'approved_for_session' : 'acceptForSession' };
    return { decision: legacy ? 'approved' : 'accept' };
  }
  return { decision: legacy ? 'denied' : 'decline' };
}

/** v2 PatchChangeKind is { type: "add" | "delete" | "update" }; bare strings (older builds) tolerated. */
export function mapChangeKind(kind: unknown): 'add' | 'modify' | 'delete' {
  const k = typeof kind === 'string' ? kind : str(rec(kind)['type']);
  if (k === 'add' || k === 'create') return 'add';
  if (k === 'delete' || k === 'remove') return 'delete';
  return 'modify';
}

interface FileChangeEntry {
  path: string;
  kind: 'add' | 'modify' | 'delete';
  diff?: string;
}

function fileChangesOf(value: unknown): FileChangeEntry[] {
  const out: FileChangeEntry[] = [];
  if (Array.isArray(value)) {
    for (const raw of value) {
      const c = rec(raw);
      const path = nonEmpty(c['path']);
      if (!path) continue;
      const diff = str(c['diff']);
      out.push({ path, kind: mapChangeKind(c['kind']), ...(diff ? { diff } : {}) });
    }
  } else if (isRecord(value)) {
    // Legacy (v1) map form { [path]: { add: {...} } | { delete: {...} } | { update: { unified_diff } } }.
    for (const [path, change] of Object.entries(value)) {
      const c = rec(change);
      const variant = str(c['type']) ?? str(c['kind']) ?? Object.keys(c)[0];
      const body = variant ? rec(c[variant]) : {};
      const diff = str(body['unified_diff']) ?? str(c['unified_diff']) ?? str(c['diff']);
      out.push({ path, kind: mapChangeKind(variant), ...(diff ? { diff } : {}) });
    }
  }
  return out;
}

// ─── Per-turn notification mapper ────────────────────────────────────────────────────────

export interface TokenTotals {
  inputTokens: number;
  cachedInputTokens: number;
  outputTokens: number;
}

function readTotals(value: unknown): TokenTotals | undefined {
  const v = rec(value);
  const input = num(v['inputTokens']);
  const output = num(v['outputTokens']);
  if (input === undefined && output === undefined) return undefined;
  return { inputTokens: input ?? 0, cachedInputTokens: num(v['cachedInputTokens']) ?? 0, outputTokens: output ?? 0 };
}

interface TrackedItem {
  type: string;
  item: Record<string, unknown>;
}

/**
 * Converts one turn's app-server notifications into EngineEvents. Stateful only within the
 * turn (item registry, de-duplication, output caps, token-usage baseline); no I/O.
 */
export class CodexTurnMapper {
  private readonly items = new Map<string, TrackedItem>();
  private readonly emittedToolCalls = new Set<string>();
  private readonly finishedToolCalls = new Set<string>();
  private readonly summaryStreamed = new Set<string>();
  private readonly outputBytes = new Map<string, number>();
  private baseline: TokenTotals | undefined;
  private latest: TokenTotals | undefined;
  /** Last non-retried `error` notification of this turn (used when turn/completed has none). */
  lastError: { code: string; message: string } | undefined;

  /** @param knownTotals thread token totals observed before this turn, when known. */
  constructor(
    private readonly model: string,
    knownTotals?: TokenTotals,
  ) {
    this.baseline = knownTotals;
  }

  /** Latest thread totals seen (carry into the next turn's mapper). */
  get totals(): TokenTotals | undefined {
    return this.latest ?? this.baseline;
  }

  handle(method: string, params: unknown): EngineEvent[] {
    const p = rec(params);
    switch (method) {
      case 'item/agentMessage/delta': {
        const itemId = nonEmpty(p['itemId']);
        const text = str(p['delta']) ?? str(p['deltaText']);
        return itemId && text ? [{ type: 'text_delta', data: { messageId: itemId, text } }] : [];
      }
      case 'item/reasoning/summaryTextDelta': {
        const itemId = nonEmpty(p['itemId']);
        const text = str(p['delta']);
        if (!text) return [];
        if (itemId) this.summaryStreamed.add(itemId);
        return [{ type: 'thinking_delta', data: { text } }];
      }
      case 'item/reasoning/summaryPartAdded': {
        const itemId = nonEmpty(p['itemId']);
        if (itemId && this.summaryStreamed.has(itemId)) return [{ type: 'thinking_delta', data: { text: '\n\n' } }];
        return [];
      }
      case 'item/commandExecution/outputDelta':
      case 'item/fileChange/outputDelta': {
        const itemId = nonEmpty(p['itemId']);
        const text = str(p['delta']);
        return itemId && text ? this.outputDelta(itemId, text) : [];
      }
      case 'item/started':
        return this.itemStarted(rec(p['item']));
      case 'item/completed':
        return this.itemCompleted(rec(p['item']));
      case 'thread/tokenUsage/updated':
        this.noteTokenUsage(rec(p['tokenUsage']));
        return [];
      case 'error': {
        // A non-retried error ends the turn; it is reported once, through the TurnResult.
        if (p['willRetry'] === true) return [];
        const error = rec(p['error']);
        this.lastError = { code: codexErrorCode(error['codexErrorInfo']), message: str(error['message']) ?? 'Codex reported an error' };
        return [];
      }
      default:
        return [];
    }
  }

  /**
   * Builds the Guard request for an approval and returns any tool_call event that has not
   * been emitted yet (approval requests can overtake item/started).
   */
  approvalToolCall(method: string, params: unknown): { events: EngineEvent[]; request: ToolCallRequest | undefined } {
    const p = rec(params);
    const itemId = nonEmpty(p['itemId']) ?? nonEmpty(p['callId']) ?? nonEmpty(p['call_id']);
    if (!itemId) return { events: [], request: undefined };
    const tracked = this.items.get(itemId)?.item;
    const reason = nonEmpty(p['reason']);
    let request: ToolCallRequest;
    if (method === APPROVAL_METHODS.command || method === APPROVAL_METHODS.legacyExec) {
      // v2 CommandExecutionRequestApprovalParams: kind "command" | "writeStdin"; a network
      // approval carries networkApprovalContext { host, protocol }. All of it reaches the Guard.
      // A writeStdin approval feeds input the Guard cannot see into an already-running process
      // (e.g. an interactive psql), so its command line is shown as `stdinTarget` only and the
      // request carries no command: the Guard then classifies it as unparseable (fail closed).
      const kind = nonEmpty(p['kind']);
      const shownCommand = normalizeCommand(p['command'] ?? tracked?.['command']);
      const command = kind === 'writeStdin' ? undefined : shownCommand;
      const cwd = nonEmpty(p['cwd']) ?? nonEmpty(tracked?.['cwd']);
      const approvalId = nonEmpty(p['approvalId']);
      const networkRaw: unknown = p['networkApprovalContext'];
      const network = isRecord(networkRaw) ? networkRaw : undefined;
      request = {
        toolCallId: itemId,
        name: 'shell',
        input: {
          ...(command !== undefined ? { command } : {}),
          ...(kind === 'writeStdin' && shownCommand !== undefined ? { stdinTarget: shownCommand } : {}),
          ...(cwd ? { cwd } : {}),
          ...(reason ? { reason } : {}),
          ...(kind && kind !== 'command' ? { kind } : {}),
          ...(approvalId ? { approvalId } : {}),
          ...(network ? { network } : {}),
          ...(p['commandActions'] !== undefined && p['commandActions'] !== null ? { commandActions: p['commandActions'] } : {}),
        },
        ...(command !== undefined ? { command } : {}),
        ...(cwd ? { cwd } : {}),
      };
    } else {
      const changes = fileChangesOf(tracked?.['changes'] ?? p['fileChanges'] ?? p['changes']);
      const grantRoot = nonEmpty(p['grantRoot']);
      request = {
        toolCallId: itemId,
        name: 'apply_patch',
        input: {
          changes: changes.map((c) => ({ path: c.path, kind: c.kind })),
          ...(reason ? { reason } : {}),
          ...(grantRoot ? { grantRoot } : {}),
        },
        ...(changes.length > 0 ? { writePaths: changes.map((c) => c.path) } : {}),
      };
    }
    const events: EngineEvent[] = [];
    if (!this.emittedToolCalls.has(itemId)) {
      this.emittedToolCalls.add(itemId);
      events.push({
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
    return { events, request };
  }

  /** Per-turn usage (delta of thread totals). Undefined when Codex reported nothing. */
  usageEvent(): EngineEvent | undefined {
    if (!this.latest) return undefined;
    const base = this.baseline ?? { inputTokens: 0, cachedInputTokens: 0, outputTokens: 0 };
    return {
      type: 'usage',
      data: {
        model: this.model,
        inputTokens: Math.max(0, this.latest.inputTokens - base.inputTokens),
        outputTokens: Math.max(0, this.latest.outputTokens - base.outputTokens),
        cacheReadTokens: Math.max(0, this.latest.cachedInputTokens - base.cachedInputTokens),
      },
    };
  }

  private noteTokenUsage(usage: Record<string, unknown>): void {
    const total = readTotals(usage['total']);
    if (!total) return;
    if (!this.baseline) {
      // First report of this turn and no prior knowledge: total minus this request's usage.
      const last = readTotals(usage['last']) ?? { inputTokens: 0, cachedInputTokens: 0, outputTokens: 0 };
      this.baseline = {
        inputTokens: Math.max(0, total.inputTokens - last.inputTokens),
        cachedInputTokens: Math.max(0, total.cachedInputTokens - last.cachedInputTokens),
        outputTokens: Math.max(0, total.outputTokens - last.outputTokens),
      };
    }
    this.latest = total;
  }

  private outputDelta(itemId: string, text: string): EngineEvent[] {
    const used = this.outputBytes.get(itemId) ?? 0;
    if (used >= TOOL_OUTPUT_CAP_BYTES) return [];
    const bytes = Buffer.byteLength(text, 'utf8');
    if (used + bytes <= TOOL_OUTPUT_CAP_BYTES) {
      this.outputBytes.set(itemId, used + bytes);
      return [{ type: 'tool_output_delta', data: { toolCallId: itemId, text } }];
    }
    this.outputBytes.set(itemId, TOOL_OUTPUT_CAP_BYTES);
    const head = Buffer.from(text, 'utf8').subarray(0, TOOL_OUTPUT_CAP_BYTES - used).toString('utf8');
    return [{ type: 'tool_output_delta', data: { toolCallId: itemId, text: `${head}\n…[output truncated]` } }];
  }

  private toolCallEvent(id: string, name: string, input: unknown, command?: string, cwd?: string): EngineEvent[] {
    if (this.emittedToolCalls.has(id)) return [];
    this.emittedToolCalls.add(id);
    return [
      {
        type: 'tool_call',
        data: { toolCallId: id, name, input, ...(command !== undefined ? { command } : {}), ...(cwd ? { cwd } : {}) },
      },
    ];
  }

  private toolResultEvent(id: string, ok: boolean, output: string, exitCode?: number): EngineEvent[] {
    if (this.finishedToolCalls.has(id)) return [];
    this.finishedToolCalls.add(id);
    return [
      { type: 'tool_result', data: { toolCallId: id, ok, output: capUtf8(output), ...(exitCode !== undefined ? { exitCode } : {}) } },
    ];
  }

  private itemStarted(item: Record<string, unknown>): EngineEvent[] {
    const id = nonEmpty(item['id']);
    const type = str(item['type']);
    if (!id || !type) return [];
    this.items.set(id, { type, item });
    switch (type) {
      case 'commandExecution': {
        const command = normalizeCommand(item['command']);
        const cwd = nonEmpty(item['cwd']);
        return this.toolCallEvent(id, 'shell', { command, ...(cwd ? { cwd } : {}) }, command, cwd);
      }
      case 'fileChange': {
        const changes = fileChangesOf(item['changes']);
        return this.toolCallEvent(id, 'apply_patch', { changes: changes.map((c) => ({ path: c.path, kind: c.kind })) });
      }
      case 'mcpToolCall':
        return this.toolCallEvent(id, `mcp:${str(item['server']) ?? '?'}/${str(item['tool']) ?? '?'}`, item['arguments'] ?? {});
      case 'webSearch':
        return this.toolCallEvent(id, 'web_search', { query: str(item['query']) ?? '' });
      case 'imageView':
        return this.toolCallEvent(id, 'view_image', { path: str(item['path']) ?? '' });
      default:
        return [];
    }
  }

  private itemCompleted(item: Record<string, unknown>): EngineEvent[] {
    const id = nonEmpty(item['id']);
    const type = str(item['type']);
    if (!id || !type) return [];
    const started = this.items.get(id)?.item ?? {};
    const merged = { ...started, ...item };
    this.items.set(id, { type, item: merged });
    const status = str(merged['status']);
    switch (type) {
      case 'agentMessage': {
        const text = str(merged['text']);
        return text !== undefined ? [{ type: 'text', data: { messageId: id, text } }] : [];
      }
      case 'reasoning': {
        if (this.summaryStreamed.has(id)) return [];
        const summary: string[] = Array.isArray(merged['summary']) ? merged['summary'].filter((s): s is string => typeof s === 'string') : [];
        const text = summary.join('\n\n').trim();
        return text ? [{ type: 'thinking_delta', data: { text } }] : [];
      }
      case 'commandExecution': {
        const events = this.itemStarted(merged);
        const exitCode = num(merged['exitCode']);
        const ok = status === 'completed' && (exitCode === undefined || exitCode === 0);
        const output = str(merged['aggregatedOutput']) ?? (status === 'declined' ? 'Command was declined.' : '');
        return [...events, ...this.toolResultEvent(id, ok, output, exitCode)];
      }
      case 'fileChange': {
        const events = this.itemStarted(merged);
        const ok = status === 'completed';
        const changes = fileChangesOf(merged['changes']);
        if (ok) {
          for (const c of changes) {
            events.push({
              type: 'file_change',
              data: { path: c.path, changeKind: c.kind, ...(c.diff ? { diff: capUtf8(c.diff) } : {}) },
            });
          }
        }
        const summary = ok
          ? changes.map((c) => `${c.kind} ${c.path}`).join('\n')
          : status === 'declined'
            ? 'Patch was declined.'
            : 'Patch failed.';
        return [...events, ...this.toolResultEvent(id, ok, summary)];
      }
      case 'mcpToolCall': {
        const events = this.itemStarted(merged);
        const error = merged['error'];
        const ok = status === 'completed' && (error === undefined || error === null);
        const output = ok ? JSON.stringify(merged['result'] ?? null) : JSON.stringify(error ?? 'failed');
        return [...events, ...this.toolResultEvent(id, ok, output)];
      }
      case 'webSearch':
      case 'imageView': {
        const events = this.itemStarted(merged);
        return [...events, ...this.toolResultEvent(id, status !== 'failed', '')];
      }
      default:
        return [];
    }
  }
}

/** codexErrorInfo is a string enum or a single-key object ({ httpConnectionFailed: {...} }). */
export function codexErrorCode(info: unknown): string {
  const raw = typeof info === 'string' ? info : isRecord(info) ? Object.keys(info)[0] : undefined;
  if (!raw) return 'codex_error';
  return `codex_${raw.replace(/([a-z0-9])([A-Z])/g, '$1_$2').toLowerCase()}`;
}

/** turn/completed params → normalised outcome. */
export function parseTurnCompleted(params: unknown): {
  turnId: string | undefined;
  status: 'ok' | 'interrupted' | 'error';
  error?: { code: string; message: string };
} {
  const turn = rec(rec(params)['turn']);
  const turnId = nonEmpty(turn['id']);
  const status = str(turn['status']);
  if (status === 'completed') return { turnId, status: 'ok' };
  if (status === 'interrupted') return { turnId, status: 'interrupted' };
  const error = rec(turn['error']);
  const message = nonEmpty(error['message']);
  return { turnId, status: 'error', ...(message ? { error: { code: codexErrorCode(error['codexErrorInfo']), message } } : {}) };
}

/** Thread id carried by a notification or server request (routing key). */
export function threadIdOf(params: unknown): string | undefined {
  const p = rec(params);
  return nonEmpty(p['threadId']) ?? nonEmpty(p['conversationId']) ?? nonEmpty(rec(p['thread'])['id']);
}

export function turnIdOf(params: unknown): string | undefined {
  const p = rec(params);
  return nonEmpty(p['turnId']) ?? nonEmpty(rec(p['turn'])['id']);
}
