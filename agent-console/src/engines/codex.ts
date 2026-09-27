// Codex engine adapter: one shared `codex app-server` child (uid agent via as-agent, JSON-RPC
// over stdio JSONL) serving every Codex console session as a thread.
//
//  - initialize{clientInfo} → initialized; model/list → ModelInfo (reasoning efforts).
//  - thread/start|resume with the session worktree as cwd, sandbox "danger-full-access" and
//    approvalPolicy "untrusted" (never `never`, never a read-only sandbox), and the session's
//    instructions (appendSystemPrompt) as developerInstructions; turn/start with
//    {model, effort, summary:'auto'} and the same policy repeated per turn.
//  - item/commandExecution|fileChange/requestApproval → hooks.onToolCall (Guard) → accept /
//    decline. Every approval is one-shot unless the session layer scopes it (see
//    approvalResponse in codex-protocol.ts).
//  - account/rateLimits/read|updated → RateLimit; account/* → device-code auth (auth/codex.ts).
//  - The child is restarted after a crash with exponential backoff; in-flight turns fail with
//    engine_crashed and their threads are resumed on the next turn.
//  - Before every start the root-owned Codex policy files under $CODEX_HOME (written by
//    bin/entrypoint.sh) are verified, so a tampered config/rules tree is never loaded.

import { spawn } from 'node:child_process';
import { lstat, readdir } from 'node:fs/promises';
import path from 'node:path';
import { CodexAuthManager } from '../auth/codex.js';
import type { AppConfig } from '../config.js';
import type { EngineFactoryContext } from '../engine-registry.js';
import {
  approvalResponse,
  buildInitializeParams,
  buildThreadResumeParams,
  buildThreadStartParams,
  buildTurnStartParams,
  CodexTurnMapper,
  consoleEngineLogger,
  EngineError,
  type EngineLogger,
  isApprovalMethod,
  isRecord,
  LineDecoder,
  mapModelList,
  mapRateLimitsPayload,
  parseTurnCompleted,
  parseUserAgentVersion,
  readThreadId,
  readTurnId,
  RPC_METHOD_NOT_FOUND,
  RpcConnection,
  RpcError,
  safeErrorMessage,
  threadIdOf,
  toEngineLogger,
  type TokenTotals,
  turnIdOf,
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
  TurnResult,
} from './types.js';

// ─── Configuration ─────────────────────────────────────────────────────────────────────────

/** The parts of a child process the adapter uses (node's ChildProcess satisfies it). */
export interface CodexChild {
  readonly stdin: NodeJS.WritableStream;
  readonly stdout: NodeJS.ReadableStream;
  readonly stderr: NodeJS.ReadableStream;
  kill(signal?: NodeJS.Signals): boolean;
  once(event: 'exit', listener: (code: number | null, signal: NodeJS.Signals | null) => void): unknown;
  once(event: 'error', listener: (error: Error) => void): unknown;
}

export type CodexSpawn = (file: string, args: string[], options: { cwd: string; env: Record<string, string> }) => CodexChild;

export const nodeCodexSpawn: CodexSpawn = (file, args, options) =>
  spawn(file, args, { cwd: options.cwd, env: options.env, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });

export interface CodexAdapterOverrides {
  /** Env for the shared app-server; defaults to context.buildEnv() (src/env.ts, no session). */
  baseEnv?: () => Record<string, string>;
  /** Native codex binary (image: /usr/local/lib/oet-agent/codex). */
  codexBinPath?: string;
  /**
   * Arguments before the `app-server` subcommand. `-c forced_login_method` is repeated on the
   * command line because config.toml alone was not honoured by the app-server in 0.155
   * (openai/codex#46914). VERIFY-ON-PIN: root-level -c applies to the subcommand.
   */
  globalArgs?: string[];
  clientVersion?: string;
  requestTimeoutMs?: number;
  modelCacheMs?: number;
  accountCacheMs?: number;
  rateLimitCacheMs?: number;
  restartInitialMs?: number;
  restartMaxMs?: number;
  /** Longest a caller waits for a pending restart before getting engine_unavailable. */
  restartWaitMs?: number;
  interruptGraceMs?: number;
  /** Verify the root-owned policy files before each start (disable only outside the image). */
  verifyPolicyFiles?: boolean;
  policyFs?: PolicyFs;
  logger?: EngineLogger;
  spawn?: CodexSpawn;
  now?: () => number;
}

export const CODEX_DEFAULTS = {
  codexBinPath: '/usr/local/lib/oet-agent/codex',
  globalArgs: ['-c', 'forced_login_method="chatgpt"'],
  requestTimeoutMs: 60_000,
  modelCacheMs: 10 * 60_000,
  accountCacheMs: 30_000,
  rateLimitCacheMs: 60_000,
  restartInitialMs: 1_000,
  restartMaxMs: 60_000,
  restartWaitMs: 15_000,
  interruptGraceMs: 30_000,
} as const;

/** Codex-side env hygiene on top of src/env.ts (API keys must never reach the app-server). */
const STRIPPED_ENV = /^(?:OPENAI_.*|CODEX_API_KEY|ANTHROPIC_.*|OWNER_AGENT_.*)$/;

export function buildCodexEnv(base: Record<string, string>, codexHome: string, homeDir: string): Record<string, string> {
  const env: Record<string, string> = {};
  for (const [key, value] of Object.entries(base)) if (!STRIPPED_ENV.test(key)) env[key] = value;
  env['HOME'] ??= homeDir;
  env['CODEX_HOME'] = codexHome;
  env['RUST_LOG'] ??= 'warn';
  env['NO_COLOR'] = '1';
  return env;
}

// ─── Policy-file integrity (layout owned by bin/entrypoint.sh) ─────────────────────────────

export interface PolicyStat {
  uid: number;
  mode: number;
  isDirectory(): boolean;
  isFile(): boolean;
  isSymbolicLink(): boolean;
}

export interface PolicyFs {
  lstat(p: string): Promise<PolicyStat>;
  readdir(p: string): Promise<string[]>;
}

const nodePolicyFs: PolicyFs = { lstat: (p) => lstat(p), readdir: (p) => readdir(p) };

/**
 * $CODEX_HOME is root:agent 1770 (sticky), so the agent can create its own state files but
 * cannot replace config.toml, AGENTS(.override).md, rules/, skills/ or prompts/ (root-owned,
 * installed by bin/entrypoint.sh). Anything else means someone rearranged the tree; refusing to
 * start keeps a planted allow-rule, `notify` hook or instruction file from bypassing the
 * Guard. Returns the list of problems (empty = OK).
 */
export async function checkCodexPolicyFiles(codexHome: string, fs: PolicyFs = nodePolicyFs): Promise<string[]> {
  const problems: string[] = [];
  const expectRootOwned = async (p: string, kind: 'dir' | 'file', label: string): Promise<boolean> => {
    let st: PolicyStat;
    try {
      st = await fs.lstat(p);
    } catch {
      problems.push(`${label} is missing`);
      return false;
    }
    if (st.isSymbolicLink()) problems.push(`${label} is a symlink`);
    else if (kind === 'dir' ? !st.isDirectory() : !st.isFile()) problems.push(`${label} is not a ${kind === 'dir' ? 'directory' : 'file'}`);
    else if (st.uid !== 0) problems.push(`${label} is not owned by root`);
    else return true;
    return false;
  };

  if (await expectRootOwned(codexHome, 'dir', 'CODEX_HOME')) {
    const st = await fs.lstat(codexHome);
    if ((st.mode & 0o002) !== 0) problems.push('CODEX_HOME is world-writable');
    if ((st.mode & 0o020) !== 0 && (st.mode & 0o1000) === 0) problems.push('CODEX_HOME is group-writable without the sticky bit');
  }
  for (const name of ['config.toml', 'AGENTS.md', 'AGENTS.override.md']) {
    const p = path.posix.join(codexHome, name);
    if (await expectRootOwned(p, 'file', name)) {
      const st = await fs.lstat(p);
      if ((st.mode & 0o022) !== 0) problems.push(`${name} is writable by non-root`);
    }
  }
  for (const name of ['skills', 'prompts']) {
    const p = path.posix.join(codexHome, name);
    if (await expectRootOwned(p, 'dir', `${name}/`)) {
      const st = await fs.lstat(p);
      if ((st.mode & 0o022) !== 0) problems.push(`${name}/ is writable by non-root`);
    }
  }
  const rulesDir = path.posix.join(codexHome, 'rules');
  if (await expectRootOwned(rulesDir, 'dir', 'rules/')) {
    const st = await fs.lstat(rulesDir);
    if ((st.mode & 0o022) !== 0) problems.push('rules/ is writable by non-root');
    const entries = await fs.readdir(rulesDir);
    for (const entry of entries) if (entry !== 'oet.rules') problems.push(`unexpected rules/${entry}`);
    const oet = path.posix.join(rulesDir, 'oet.rules');
    if (await expectRootOwned(oet, 'file', 'rules/oet.rules')) {
      const rst = await fs.lstat(oet);
      if ((rst.mode & 0o022) !== 0) problems.push('rules/oet.rules is writable by non-root');
    }
  }
  return problems;
}

// ─── Shared app-server process ─────────────────────────────────────────────────────────────

interface AppServerOptions {
  file: string;
  args: string[];
  cwd: string;
  env: () => Record<string, string>;
  clientVersion: string;
  requestTimeoutMs: number;
  restartInitialMs: number;
  restartMaxMs: number;
  restartWaitMs: number;
  verify: (() => Promise<void>) | undefined;
  spawn: CodexSpawn;
  logger: EngineLogger;
  now: () => number;
}

interface AppServerHandlers {
  onNotification(method: string, params: unknown): void;
  onRequest(method: string, params: unknown): Promise<unknown>;
  onExit(error: EngineError): void;
}

const STDERR_TAIL_LINES = 20;
const STABLE_UPTIME_MS = 60_000;

export class CodexAppServer {
  private child: CodexChild | undefined;
  private rpc: RpcConnection | undefined;
  private starting: Promise<void> | undefined;
  private ready = false;
  private stopping = false;
  private shutDown = false;
  private generationCounter = 0;
  private startedAt = 0;
  private failures = 0;
  private nextStartAt = 0;
  private restartTimer: ReturnType<typeof setTimeout> | undefined;
  private readonly stderrTail: string[] = [];
  userAgent: string | undefined;

  constructor(
    private readonly options: AppServerOptions,
    private readonly handlers: AppServerHandlers,
  ) {}

  /** Increments on every (re)start; threads loaded in an older generation must be resumed. */
  get generation(): number {
    return this.generationCounter;
  }

  get isReady(): boolean {
    return this.ready;
  }

  /** Starts the child if needed (respecting crash backoff) and returns the live generation. */
  async ensureStarted(): Promise<number> {
    if (this.shutDown) throw new EngineError('engine_unavailable', 'The Codex engine is shut down.');
    if (this.ready && this.rpc) return this.generationCounter;
    if (!this.starting) {
      const wait = this.nextStartAt - this.options.now();
      if (wait > this.options.restartWaitMs) {
        throw new EngineError('engine_unavailable', `Codex is restarting after a crash; retry in ${Math.ceil(wait / 1000)} s.`);
      }
      this.starting = (async () => {
        if (wait > 0) await new Promise((resolve) => setTimeout(resolve, wait));
        await this.start();
      })().finally(() => {
        this.starting = undefined;
      });
    }
    await this.starting;
    return this.generationCounter;
  }

  async request<T = unknown>(method: string, params?: unknown, opts: { timeoutMs?: number; signal?: AbortSignal } = {}): Promise<T> {
    await this.ensureStarted();
    const rpc = this.rpc;
    if (!rpc) throw new EngineError('engine_unavailable', 'Codex app-server is not running.');
    return rpc.request<T>(method, params, opts);
  }

  async stop(): Promise<void> {
    this.shutDown = true;
    if (this.restartTimer) clearTimeout(this.restartTimer);
    const child = this.child;
    if (!child) return;
    this.stopping = true;
    await new Promise<void>((resolve) => {
      const hardKill = setTimeout(() => {
        child.kill('SIGKILL');
        resolve();
      }, 5_000);
      hardKill.unref?.();
      child.once('exit', () => {
        clearTimeout(hardKill);
        resolve();
      });
      child.kill('SIGTERM');
    });
  }

  private async start(): Promise<void> {
    if (this.options.verify) await this.options.verify();
    const generation = ++this.generationCounter;
    const child = this.options.spawn(this.options.file, this.options.args, { cwd: this.options.cwd, env: this.options.env() });
    const decoder = new LineDecoder(undefined, (bytes) => this.options.logger.warn('codex app-server line too long; dropped', { bytes }));
    const rpc = new RpcConnection({
      write: (line) => {
        child.stdin.write(line);
      },
      onNotification: (method, params) => this.handlers.onNotification(method, params),
      onRequest: (method, params) => this.handlers.onRequest(method, params),
      onInvalid: (reason) => this.options.logger.warn('codex app-server sent an invalid message', { reason }),
      requestTimeoutMs: this.options.requestTimeoutMs,
    });
    this.child = child;
    this.rpc = rpc;
    this.ready = false;
    child.stdout.on('data', (chunk: Buffer | string) => {
      for (const line of decoder.push(chunk)) rpc.handleLine(line);
    });
    child.stderr.on('data', (chunk: Buffer | string) => this.noteStderr(String(chunk)));
    child.stdin.on('error', () => undefined);
    child.once('error', (error) => this.onChildExit(generation, child, `could not be started (${safeErrorMessage(error)})`));
    child.once('exit', (code, signal) => this.onChildExit(generation, child, `exited (${code ?? signal ?? 'unknown'})`));
    try {
      const init = await rpc.request('initialize', buildInitializeParams(this.options.clientVersion), { timeoutMs: 30_000 });
      // The documented handshake sends `initialized` with an empty params object.
      rpc.notify('initialized', {});
      this.userAgent = isRecord(init) && typeof init['userAgent'] === 'string' ? init['userAgent'] : undefined;
      this.ready = true;
      this.startedAt = this.options.now();
      this.options.logger.info('codex app-server ready', { generation, userAgent: this.userAgent });
    } catch (err) {
      try {
        child.kill('SIGKILL');
      } catch {
        // already gone
      }
      this.onChildExit(generation, child, `failed to initialize (${safeErrorMessage(err)})`);
      throw new EngineError('engine_unavailable', `Codex app-server did not start: ${safeErrorMessage(err)}`);
    }
  }

  private noteStderr(text: string): void {
    for (const line of text.split('\n')) {
      const trimmed = line.trim();
      if (!trimmed) continue;
      this.stderrTail.push(trimmed.slice(0, 300));
      if (this.stderrTail.length > STDERR_TAIL_LINES) this.stderrTail.shift();
    }
  }

  private onChildExit(generation: number, child: CodexChild, reason: string): void {
    if (generation !== this.generationCounter || this.child !== child) return;
    const uptime = this.ready ? this.options.now() - this.startedAt : 0;
    const rpc = this.rpc;
    this.child = undefined;
    this.rpc = undefined;
    this.ready = false;
    const lastLine = this.stderrTail.at(-1);
    const error = new EngineError('engine_crashed', `Codex app-server ${reason}.${lastLine ? ` Last log line: ${lastLine}` : ''}`);
    rpc?.close(error);
    this.handlers.onExit(error);
    if (this.stopping || this.shutDown) {
      this.stopping = false;
      return;
    }
    this.failures = uptime >= STABLE_UPTIME_MS ? 1 : this.failures + 1;
    const delay = Math.min(this.options.restartMaxMs, this.options.restartInitialMs * 2 ** (this.failures - 1));
    this.nextStartAt = this.options.now() + delay;
    this.options.logger.warn('codex app-server stopped; restarting with backoff', {
      reason,
      delayMs: delay,
      failures: this.failures,
      stderrTail: [...this.stderrTail],
    });
    if (this.restartTimer) clearTimeout(this.restartTimer);
    this.restartTimer = setTimeout(() => {
      this.ensureStarted().catch((err: unknown) =>
        this.options.logger.warn('codex app-server restart failed', { message: safeErrorMessage(err) }),
      );
    }, delay);
    this.restartTimer.unref?.();
  }
}

// ─── Sessions ──────────────────────────────────────────────────────────────────────────────

interface TurnOutcome {
  status: 'ok' | 'interrupted' | 'error';
  error?: { code: string; message: string };
}

class CodexTurn {
  turnId: string | undefined;
  interruptRequested = false;
  readonly mapper: CodexTurnMapper;
  readonly done: Promise<TurnOutcome>;
  private settle: (outcome: TurnOutcome) => void = () => undefined;
  private settled = false;
  private graceTimer: ReturnType<typeof setTimeout> | undefined;

  constructor(
    readonly hooks: EngineHooks,
    readonly signal: AbortSignal,
    readonly model: string,
    totals: TokenTotals | undefined,
  ) {
    this.mapper = new CodexTurnMapper(model, totals);
    this.done = new Promise((resolve) => {
      this.settle = resolve;
    });
  }

  get isSettled(): boolean {
    return this.settled;
  }

  finish(outcome: TurnOutcome): void {
    if (this.settled) return;
    this.settled = true;
    this.settle(outcome);
  }

  armGrace(ms: number): void {
    if (this.graceTimer || this.settled) return;
    this.graceTimer = setTimeout(() => this.finish({ status: 'interrupted' }), ms);
    this.graceTimer.unref?.();
  }

  dispose(): void {
    if (this.graceTimer) clearTimeout(this.graceTimer);
  }
}

interface CodexSessionHost {
  readonly server: CodexAppServer;
  readonly logger: EngineLogger;
  readonly interruptGraceMs: number;
  registerThread(threadId: string, session: CodexSession): void;
  unregisterThread(threadId: string, session: CodexSession): void;
  detach(session: CodexSession): void;
  /** Thread token totals per console session (survive the session layer's idle close/reopen). */
  tokenTotals(sessionId: string): TokenTotals | undefined;
  setTokenTotals(sessionId: string, totals: TokenTotals): void;
}

const RECENT_TURN_MEMORY = 8;

export class CodexSession implements EngineSession {
  readonly engine = 'codex' as const;
  private threadId: string | undefined;
  private loadedGeneration = -1;
  private turn: CodexTurn | undefined;
  private readonly finishedTurnIds: string[] = [];

  constructor(
    private readonly host: CodexSessionHost,
    private readonly opts: SessionEngineOptions,
  ) {
    this.threadId = opts.resumeId;
  }

  get sessionId(): string {
    return this.opts.sessionId;
  }

  get hasActiveTurn(): boolean {
    return this.turn !== undefined;
  }

  async runTurn(text: string, opts: { model: string; effort?: string; mode: Mode }, hooks: EngineHooks, signal: AbortSignal): Promise<TurnResult> {
    if (this.turn) throw new EngineError('turn_in_progress', 'A turn is already running in this session.');
    const resume = (): { resumeId?: string } => (this.threadId ? { resumeId: this.threadId } : {});
    if (signal.aborted) return { status: 'interrupted', ...resume() };
    const turn = new CodexTurn(hooks, signal, opts.model, this.host.tokenTotals(this.opts.sessionId));
    this.turn = turn;
    const onAbort = (): void => void this.interrupt();
    signal.addEventListener('abort', onAbort, { once: true });
    try {
      const generation = await this.host.server.ensureStarted();
      await this.ensureThread(opts.model, generation);
      const threadId = this.threadId;
      if (!threadId) throw new EngineError('engine_error', 'Codex did not return a thread id.');
      if (turn.interruptRequested) return { status: 'interrupted', ...resume() };
      const started = await this.host.server.request(
        'turn/start',
        buildTurnStartParams({ threadId, text, cwd: this.opts.cwd, model: opts.model, ...(opts.effort ? { effort: opts.effort } : {}) }),
      );
      turn.turnId ??= readTurnId(started);
      if (turn.interruptRequested) await this.sendInterrupt(turn);
      const outcome = await turn.done;
      const usage = turn.mapper.usageEvent();
      if (usage) this.emit(turn, usage);
      const totals = turn.mapper.totals;
      if (totals) this.host.setTokenTotals(this.opts.sessionId, totals);
      return { status: outcome.status, ...resume(), ...(outcome.error ? { error: outcome.error } : {}) };
    } catch (err) {
      if (turn.interruptRequested) return { status: 'interrupted', ...resume() };
      const code = err instanceof EngineError ? err.code : 'engine_error';
      this.host.logger.error('codex turn failed', { sessionId: this.opts.sessionId, code, message: safeErrorMessage(err) });
      return { status: 'error', ...resume(), error: { code, message: safeErrorMessage(err) } };
    } finally {
      signal.removeEventListener('abort', onAbort);
      turn.dispose();
      if (turn.turnId) this.rememberFinished(turn.turnId);
      this.turn = undefined;
    }
  }

  async interrupt(): Promise<void> {
    const turn = this.turn;
    if (!turn || turn.isSettled) return;
    turn.interruptRequested = true;
    turn.armGrace(this.host.interruptGraceMs);
    await this.sendInterrupt(turn);
  }

  async close(): Promise<void> {
    if (this.turn) {
      this.turn.interruptRequested = true;
      await this.sendInterrupt(this.turn);
      this.turn.finish({ status: 'interrupted' });
    }
    const threadId = this.threadId;
    if (threadId && this.loadedGeneration === this.host.server.generation && this.host.server.isReady) {
      // Best effort: let the app-server drop the loaded thread (v2 thread/unsubscribe { threadId }).
      await this.host.server.request('thread/unsubscribe', { threadId }, { timeoutMs: 5_000 }).catch(() => undefined);
    }
    if (threadId) this.host.unregisterThread(threadId, this);
    this.host.detach(this);
    this.loadedGeneration = -1;
  }

  /** Routed notification for this thread. */
  handleNotification(method: string, params: unknown): void {
    const turn = this.turn;
    if (!turn) return;
    const turnId = turnIdOf(params);
    if (turnId && this.finishedTurnIds.includes(turnId)) return;
    if (turnId && turn.turnId && turnId !== turn.turnId) return;
    if (method === 'turn/started') {
      turn.turnId ??= turnId;
      return;
    }
    if (method === 'turn/completed') {
      const outcome = parseTurnCompleted(params);
      if (outcome.status === 'interrupted' || turn.interruptRequested) turn.finish({ status: 'interrupted' });
      else if (outcome.status === 'ok') turn.finish({ status: 'ok' });
      else {
        turn.finish({
          status: 'error',
          error: outcome.error ?? turn.mapper.lastError ?? { code: 'codex_turn_failed', message: 'Codex ended the turn with an error.' },
        });
      }
      return;
    }
    for (const event of turn.mapper.handle(method, params)) this.emit(turn, event);
  }

  /** Routed approval request for this thread → Guard → accept/decline. */
  async handleApproval(method: string, params: unknown): Promise<unknown> {
    const turn = this.turn;
    if (!turn || turn.isSettled) return approvalResponse(method, { behavior: 'deny', message: 'No console turn is active.' });
    const turnId = turnIdOf(params);
    if (turnId && turn.turnId && turnId !== turn.turnId) {
      return approvalResponse(method, { behavior: 'deny', message: 'Approval belongs to a finished turn.' });
    }
    const { events, request } = turn.mapper.approvalToolCall(method, params);
    for (const event of events) this.emit(turn, event);
    if (!request) return approvalResponse(method, { behavior: 'deny', message: 'Unrecognised approval request.' });
    try {
      const decision = await turn.hooks.onToolCall(request, turn.signal);
      return approvalResponse(method, decision);
    } catch (err) {
      if (turn.interruptRequested || turn.signal.aborted) return approvalResponse(method, 'cancel');
      this.host.logger.error('guard failed; declining codex approval', { sessionId: this.opts.sessionId, message: safeErrorMessage(err) });
      return approvalResponse(method, { behavior: 'deny', message: 'The console guard could not decide on this call.' });
    }
  }

  /** The shared app-server died: fail the turn; the thread is resumed on the next turn. */
  onServerExit(error: EngineError): void {
    this.loadedGeneration = -1;
    const turn = this.turn;
    if (!turn) return;
    if (turn.interruptRequested) turn.finish({ status: 'interrupted' });
    else turn.finish({ status: 'error', error: { code: error.code, message: error.message } });
  }

  /** Account-wide rate limits are pushed into the running turn's stream. */
  emitRateLimits(limits: RateLimit[]): void {
    if (this.turn) this.emit(this.turn, { type: 'rate_limit', data: { engine: 'codex', limits } });
  }

  private async ensureThread(model: string, generation: number): Promise<void> {
    if (this.threadId && this.loadedGeneration === generation) return;
    const previous = this.threadId;
    let result: unknown;
    try {
      const developerInstructions = this.opts.appendSystemPrompt;
      result = previous
        ? await this.host.server.request('thread/resume', buildThreadResumeParams({ threadId: previous, cwd: this.opts.cwd, model, developerInstructions }))
        : await this.host.server.request('thread/start', buildThreadStartParams({ cwd: this.opts.cwd, model, developerInstructions }));
    } catch (err) {
      if (err instanceof RpcError) {
        throw new EngineError(previous ? 'engine_resume_failed' : 'engine_error', `Codex could not ${previous ? 'resume' : 'start'} the thread: ${safeErrorMessage(err)}`);
      }
      throw err;
    }
    const threadId = readThreadId(result) ?? previous;
    if (!threadId) throw new EngineError('engine_error', 'Codex did not return a thread id.');
    if (previous && previous !== threadId) this.host.unregisterThread(previous, this);
    this.threadId = threadId;
    this.loadedGeneration = generation;
    this.host.registerThread(threadId, this);
  }

  private async sendInterrupt(turn: CodexTurn): Promise<void> {
    const threadId = this.threadId;
    if (!threadId || !turn.turnId || turn.isSettled) return;
    try {
      await this.host.server.request('turn/interrupt', { threadId, turnId: turn.turnId }, { timeoutMs: 10_000 });
    } catch (err) {
      this.host.logger.warn('codex turn/interrupt failed', { sessionId: this.opts.sessionId, message: safeErrorMessage(err) });
    }
  }

  private rememberFinished(turnId: string): void {
    this.finishedTurnIds.push(turnId);
    if (this.finishedTurnIds.length > RECENT_TURN_MEMORY) this.finishedTurnIds.shift();
  }

  private emit(turn: CodexTurn, event: EngineEvent): void {
    try {
      turn.hooks.emit(event);
    } catch (err) {
      this.host.logger.error('codex event sink failed', { sessionId: this.opts.sessionId, message: safeErrorMessage(err) });
    }
  }
}

// ─── Adapter ───────────────────────────────────────────────────────────────────────────────

/** EngineAdapter plus the hook the session layer can use to attribute session-less proxy approvals. */
export interface CodexEngineAdapter extends EngineAdapter {
  /** Console sessions with a Codex turn in flight (all share one app-server env). */
  activeTurnSessionIds(): string[];
}

interface CodexAdapterSettings extends CodexAdapterOverrides {
  baseEnv: () => Record<string, string>;
  asAgentPath: string;
  homeDir: string;
  codexHome: string;
  logger: EngineLogger;
}

class CodexAdapter implements CodexEngineAdapter, CodexSessionHost {
  readonly engine = 'codex' as const;
  readonly server: CodexAppServer;
  readonly logger: EngineLogger;
  readonly interruptGraceMs: number;
  private readonly auth: CodexAuthManager;
  private readonly now: () => number;
  private readonly threads = new Map<string, CodexSession>();
  private readonly sessions = new Set<CodexSession>();
  private readonly totalsBySession = new Map<string, TokenTotals>();
  private modelCache: { at: number; models: ModelInfo[] } | undefined;
  private accountCache: { at: number; auth: EngineAuth } | undefined;
  private rateLimits: RateLimit[] | null = null;
  private rateLimitsAt = 0;

  constructor(private readonly settings: CodexAdapterSettings) {
    this.now = settings.now ?? Date.now;
    this.logger = settings.logger;
    this.interruptGraceMs = settings.interruptGraceMs ?? CODEX_DEFAULTS.interruptGraceMs;
    const verify = settings.verifyPolicyFiles === false ? undefined : () => this.verifyPolicy();
    this.server = new CodexAppServer(
      {
        file: settings.asAgentPath,
        args: [settings.codexBinPath ?? CODEX_DEFAULTS.codexBinPath, ...(settings.globalArgs ?? CODEX_DEFAULTS.globalArgs), 'app-server'],
        cwd: settings.homeDir,
        env: () => buildCodexEnv(settings.baseEnv(), settings.codexHome, settings.homeDir),
        clientVersion: settings.clientVersion ?? '0.0.0',
        requestTimeoutMs: settings.requestTimeoutMs ?? CODEX_DEFAULTS.requestTimeoutMs,
        restartInitialMs: settings.restartInitialMs ?? CODEX_DEFAULTS.restartInitialMs,
        restartMaxMs: settings.restartMaxMs ?? CODEX_DEFAULTS.restartMaxMs,
        restartWaitMs: settings.restartWaitMs ?? CODEX_DEFAULTS.restartWaitMs,
        verify,
        spawn: settings.spawn ?? nodeCodexSpawn,
        logger: settings.logger,
        now: this.now,
      },
      {
        onNotification: (method, params) => this.onNotification(method, params),
        onRequest: (method, params) => this.onRequest(method, params),
        onExit: (error) => this.onServerExit(error),
      },
    );
    this.auth = new CodexAuthManager(
      { request: (method, params, opts) => this.server.request(method, params, opts ?? {}) },
      { now: this.now, onAuthChanged: () => this.invalidateAccount() },
    );
  }

  // ── EngineAdapter ──

  async status(): Promise<EngineStatus> {
    try {
      await this.server.ensureStarted();
    } catch (err) {
      return { engine: 'codex', version: null, auth: { state: 'error', detail: safeErrorMessage(err) }, models: [], rateLimits: this.rateLimits };
    }
    const version = parseUserAgentVersion(this.server.userAgent);
    let auth: EngineAuth;
    try {
      auth = await this.readAccount();
    } catch (err) {
      auth = { state: 'error', detail: `Could not read the Codex account: ${safeErrorMessage(err)}` };
    }
    if (auth.state !== 'signed_in') return { engine: 'codex', version, auth, models: [], rateLimits: this.rateLimits };
    let models: ModelInfo[] = [];
    try {
      models = await this.models();
    } catch (err) {
      this.logger.warn('codex model/list failed', { message: safeErrorMessage(err) });
      auth = { ...auth, detail: `Model list unavailable: ${safeErrorMessage(err)}` };
    }
    await this.refreshRateLimits();
    return { engine: 'codex', version, auth, models, rateLimits: this.rateLimits };
  }

  async openSession(opts: SessionEngineOptions): Promise<EngineSession> {
    const auth = await this.readAccount().catch((err: unknown): EngineAuth => ({ state: 'error', detail: safeErrorMessage(err) }));
    if (auth.state !== 'signed_in') {
      throw new EngineError(
        auth.state === 'error' ? 'engine_auth_error' : 'engine_not_signed_in',
        auth.detail ?? 'Codex is not signed in with ChatGPT. Connect ChatGPT first.',
      );
    }
    const session = new CodexSession(this, opts);
    this.sessions.add(session);
    return session;
  }

  connect(): Promise<ConnectFlow> {
    return this.auth.connect();
  }

  getFlow(flowId: string): ConnectFlow | undefined {
    return this.auth.getFlow(flowId);
  }

  submitCode(flowId: string): Promise<ConnectFlow> {
    return this.auth.submitCode(flowId);
  }

  cancel(flowId: string): Promise<ConnectFlow> {
    return this.auth.cancel(flowId);
  }

  async logout(): Promise<EngineAuth> {
    await this.auth.logout();
    this.invalidateAccount();
    return this.readAccount().catch((err: unknown): EngineAuth => ({ state: 'error', detail: safeErrorMessage(err) }));
  }

  async shutdown(): Promise<void> {
    this.auth.shutdown();
    await Promise.allSettled([...this.sessions].map((s) => s.close()));
    await this.server.stop();
  }

  activeTurnSessionIds(): string[] {
    return [...this.sessions].filter((s) => s.hasActiveTurn).map((s) => s.sessionId);
  }

  // ── CodexSessionHost ──

  registerThread(threadId: string, session: CodexSession): void {
    this.threads.set(threadId, session);
    this.sessions.add(session);
  }

  unregisterThread(threadId: string, session: CodexSession): void {
    if (this.threads.get(threadId) === session) this.threads.delete(threadId);
  }

  detach(session: CodexSession): void {
    this.sessions.delete(session);
  }

  tokenTotals(sessionId: string): TokenTotals | undefined {
    return this.totalsBySession.get(sessionId);
  }

  setTokenTotals(sessionId: string, totals: TokenTotals): void {
    this.totalsBySession.set(sessionId, totals);
  }

  // ── routing ──

  private onNotification(method: string, params: unknown): void {
    if (method === 'account/rateLimits/updated') {
      const limits = mapRateLimitsPayload(params);
      if (limits.length > 0) {
        this.rateLimits = limits;
        this.rateLimitsAt = this.now();
        for (const session of this.sessions) session.emitRateLimits(limits);
      }
      return;
    }
    if (method.startsWith('account/')) {
      this.auth.handleNotification(method, params);
      this.invalidateAccount();
      return;
    }
    const threadId = threadIdOf(params);
    if (threadId) this.threads.get(threadId)?.handleNotification(method, params);
  }

  private async onRequest(method: string, params: unknown): Promise<unknown> {
    if (isApprovalMethod(method)) {
      const threadId = threadIdOf(params);
      const session = threadId ? this.threads.get(threadId) : undefined;
      if (!session) return approvalResponse(method, { behavior: 'deny', message: 'No console session owns this thread.' });
      return session.handleApproval(method, params);
    }
    this.logger.warn('codex server request not supported; rejected', { method });
    throw new RpcError(RPC_METHOD_NOT_FOUND, `The OET console does not support ${method}.`);
  }

  private onServerExit(error: EngineError): void {
    for (const session of this.sessions) session.onServerExit(error);
    this.auth.onServerRestart();
    this.invalidateAccount();
    this.modelCache = undefined;
  }

  // ── internals ──

  private invalidateAccount(): void {
    this.accountCache = undefined;
  }

  private async readAccount(): Promise<EngineAuth> {
    const ttl = this.settings.accountCacheMs ?? CODEX_DEFAULTS.accountCacheMs;
    if (this.accountCache && this.now() - this.accountCache.at < ttl) return this.accountCache.auth;
    const auth = await this.auth.readAccount();
    this.accountCache = { at: this.now(), auth };
    return auth;
  }

  private async models(): Promise<ModelInfo[]> {
    const ttl = this.settings.modelCacheMs ?? CODEX_DEFAULTS.modelCacheMs;
    if (this.modelCache && this.now() - this.modelCache.at < ttl) return this.modelCache.models;
    const models: ModelInfo[] = [];
    let cursor: string | null = null;
    for (let page = 0; page < 10; page++) {
      const result = await this.server.request('model/list', { limit: 100, includeHidden: false, ...(cursor ? { cursor } : {}) });
      const mapped = mapModelList(result);
      for (const model of mapped.models) if (!models.some((m) => m.value === model.value)) models.push(model);
      cursor = mapped.nextCursor;
      if (!cursor) break;
    }
    this.modelCache = { at: this.now(), models };
    return models;
  }

  private async refreshRateLimits(): Promise<void> {
    const ttl = this.settings.rateLimitCacheMs ?? CODEX_DEFAULTS.rateLimitCacheMs;
    if (this.rateLimits && this.now() - this.rateLimitsAt < ttl) return;
    try {
      const limits = mapRateLimitsPayload(await this.server.request('account/rateLimits/read', {}, { timeoutMs: 15_000 }));
      if (limits.length > 0) {
        this.rateLimits = limits;
        this.rateLimitsAt = this.now();
      }
    } catch (err) {
      this.logger.info('codex account/rateLimits/read failed', { message: safeErrorMessage(err) });
    }
  }

  private async verifyPolicy(): Promise<void> {
    const problems = await checkCodexPolicyFiles(this.settings.codexHome, this.settings.policyFs);
    if (problems.length > 0) {
      this.logger.error('codex policy files failed verification; refusing to start', { problems });
      throw new EngineError(
        'codex_policy_tampered',
        `Codex policy files under CODEX_HOME are not in the expected root-owned state (${problems.join('; ')}). Restart the container to reinstall them.`,
      );
    }
  }
}

/**
 * Factory used by src/engine-registry.ts. The app-server runs as uid agent with the
 * session-less allow-listed env from src/env.ts (all Codex threads share that process).
 */
export function createCodexAdapter(
  config: AppConfig,
  context?: EngineFactoryContext,
  overrides: CodexAdapterOverrides = {},
): CodexEngineAdapter {
  return new CodexAdapter({
    ...overrides,
    baseEnv: overrides.baseEnv ?? (() => context?.buildEnv() ?? {}),
    asAgentPath: config.asAgentPath,
    homeDir: config.agentHome,
    codexHome: config.codexHome,
    clientVersion: overrides.clientVersion ?? config.version,
    logger: overrides.logger ?? (context ? toEngineLogger(context.logger, 'engine.codex') : consoleEngineLogger('engine.codex')),
  });
}
