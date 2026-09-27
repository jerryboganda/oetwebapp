// Claude Code sign-in for the owner console.
//
// Runs the SDK-bundled, unmodified `claude auth login` (claude.ai subscription method — never
// `--console`, never an API key or a pasted token) inside the container as uid agent, in a
// wide PTY. The owner only ever sees the Anthropic sign-in URL and types back the paste code.
// The binary owns $CLAUDE_CONFIG_DIR/.credentials.json: this module never reads, copies, logs
// or returns it (vendor terms: code.claude.com/docs/en/legal-and-compliance).
//
// Also hosts the small helpers shared by both engines' connect flows (flow registry, ULID
// flow ids, AuthFlowError). Output parsers are pure and exported for tests.

import { createRequire } from 'node:module';
import { ulid } from 'ulid';
import type { ConnectFlow, Engine } from '../engines/types.js';
import { HttpError } from '../errors.js';
import { createRunner, type Runner } from '../exec.js';

// ─── Shared: errors, ids, flow registry ───────────────────────────────────────────────────

export type AuthFlowErrorCode = 'flow_not_found' | 'flow_not_awaiting_code' | 'invalid_code' | 'logout_failed';

const AUTH_FLOW_STATUS: Record<AuthFlowErrorCode, number> = {
  flow_not_found: 404,
  flow_not_awaiting_code: 409,
  invalid_code: 400,
  logout_failed: 500,
};

/** HttpError (CONTRACT §3 envelope) for connect-flow failures; `message` is safe to show the owner. */
export class AuthFlowError extends HttpError {
  constructor(code: AuthFlowErrorCode, message: string) {
    super(AUTH_FLOW_STATUS[code], code, message);
    this.name = 'AuthFlowError';
  }
}

/** Flow ids are ULIDs like every other id in the contract. */
export function newFlowId(now: number = Date.now()): string {
  return ulid(now);
}

const TERMINAL_STATES: ReadonlySet<ConnectFlow['state']> = new Set(['completed', 'failed', 'cancelled', 'expired']);

export function isTerminalFlow(flow: Pick<ConnectFlow, 'state'>): boolean {
  return TERMINAL_STATES.has(flow.state);
}

type FlowPatch = Partial<Pick<ConnectFlow, 'state' | 'verificationUrl' | 'userCode' | 'expiresAt' | 'detail'>>;

/** In-memory connect flows for one engine. Terminal flows are immutable and kept for polling. */
export class FlowRegistry {
  private readonly flows = new Map<string, { flow: ConnectFlow; endedAt?: number }>();

  constructor(
    private readonly engine: Engine,
    private readonly now: () => number = Date.now,
    private readonly retentionMs = 60 * 60_000,
  ) {}

  create(kind: ConnectFlow['kind']): ConnectFlow {
    this.prune();
    const flow: ConnectFlow = { flowId: newFlowId(this.now()), engine: this.engine, kind, state: 'pending' };
    this.flows.set(flow.flowId, { flow });
    return { ...flow };
  }

  get(flowId: string): ConnectFlow | undefined {
    this.prune();
    const entry = this.flows.get(flowId);
    return entry ? { ...entry.flow } : undefined;
  }

  /** Applies a patch unless the flow is already terminal; returns the current snapshot. */
  update(flowId: string, patch: FlowPatch): ConnectFlow | undefined {
    const entry = this.flows.get(flowId);
    if (!entry) return undefined;
    if (!isTerminalFlow(entry.flow)) {
      entry.flow = { ...entry.flow, ...patch };
      if (isTerminalFlow(entry.flow)) entry.endedAt = this.now();
    }
    return { ...entry.flow };
  }

  /** Most recent non-terminal flow, if any. */
  active(): ConnectFlow | undefined {
    let found: ConnectFlow | undefined;
    for (const { flow } of this.flows.values()) if (!isTerminalFlow(flow)) found = flow;
    return found ? { ...found } : undefined;
  }

  private prune(): void {
    const cutoff = this.now() - this.retentionMs;
    for (const [id, entry] of this.flows) if (entry.endedAt !== undefined && entry.endedAt < cutoff) this.flows.delete(id);
  }
}

// ─── PTY seam ─────────────────────────────────────────────────────────────────────────────

export interface PtyProcess {
  onData(listener: (data: string) => void): unknown;
  onExit(listener: (event: { exitCode: number; signal?: number }) => void): unknown;
  write(data: string): void;
  kill(signal?: string): void;
}

export type PtySpawner = (
  file: string,
  args: string[],
  options: { name: string; cols: number; rows: number; cwd: string; env: Record<string, string> },
) => PtyProcess;

/** node-pty, loaded lazily so unit tests (and the Codex path) never load the native module. */
export const nodePtySpawner: PtySpawner = (file, args, options) => {
  const requireCjs = createRequire(import.meta.url);
  const pty = requireCjs('node-pty') as { spawn(file: string, args: string[], options: object): PtyProcess };
  return pty.spawn(file, args, options);
};

// ─── Pure parsers (fixture-tested) ────────────────────────────────────────────────────────

// eslint-disable-next-line no-control-regex
const OSC8_LINK = /\u001b\]8;[^;\u0007\u001b]*;([^\u0007\u001b]*)(?:\u0007|\u001b\\)/g;
// eslint-disable-next-line no-control-regex
const ANSI = /\u001b\[[0-?]*[ -/]*[@-~]|\u001b\][^\u0007\u001b]*(?:\u0007|\u001b\\)|\u001b[@-Z\\-_]|\u009b[0-?]*[ -/]*[@-~]/g;

/** Removes CSI/OSC/ESC sequences (colours, cursor moves, hyperlinks) from terminal output. */
export function stripAnsi(text: string): string {
  return text.replace(ANSI, '');
}

const CLAUDE_LOGIN_HOSTS = ['claude.ai', 'claude.com', 'anthropic.com'];

/** Only Anthropic-owned https URLs are ever relayed to the owner. */
export function isAllowedClaudeLoginUrl(url: string): boolean {
  try {
    const u = new URL(url);
    if (u.protocol !== 'https:' || u.username || u.password) return false;
    return CLAUDE_LOGIN_HOSTS.some((h) => u.hostname === h || u.hostname.endsWith(`.${h}`));
  } catch {
    return false;
  }
}

function cleanUrl(candidate: string): string {
  return candidate.replace(/[)\].,;:'"`>]+$/, '');
}

/**
 * Finds the OAuth authorize URL printed by `claude auth login`. Prefers OSC 8 hyperlink
 * targets (never wrapped), then plain text; prefers the manual paste-code variant
 * (`code=true`). VERIFY-ON-PIN: URL shape `https://claude.ai/oauth/authorize?code=true&...`.
 */
export function extractLoginUrl(output: string): string | undefined {
  const candidates: string[] = [];
  for (const match of output.matchAll(OSC8_LINK)) if (match[1]) candidates.push(cleanUrl(match[1]));
  const plain = stripAnsi(output);
  // eslint-disable-next-line no-control-regex
  for (const match of plain.matchAll(/https:\/\/[^\s"'<>\u0000-\u001f]+/g)) candidates.push(cleanUrl(match[0]));
  const oauth = candidates.filter((u) => isAllowedClaudeLoginUrl(u) && /\/oauth\/authorize\b/.test(u));
  return oauth.find((u) => /[?&]code=true\b/.test(u)) ?? oauth[0];
}

/** True when the CLI shows a login-method menu (older builds / no forceLoginMethod). */
export function isLoginMethodMenu(output: string): boolean {
  return /select login method|claude account with subscription/i.test(stripAnsi(output));
}

export function isPressEnterPrompt(output: string): boolean {
  return /press enter to continue/i.test(stripAnsi(output));
}

const TOKENISH = /[A-Za-z0-9_#~.-]{24,}/g;

/** Makes one CLI output line safe to show: no ANSI, no long token-like strings, bounded length. */
export function sanitizeDetail(line: string, max = 160): string {
  const clean = stripAnsi(line).replace(TOKENISH, '[redacted]').replace(/\s+/g, ' ').trim();
  return clean.length > max ? `${clean.slice(0, max - 1)}…` : clean;
}

/**
 * Classifies CLI output produced after the paste code was typed. VERIFY-ON-PIN: message
 * wording of the pinned CLI ("Login successful." / "OAuth error: ...").
 */
export function detectLoginOutcome(outputAfterSubmit: string): { outcome: 'success' | 'failure'; detail?: string } | undefined {
  const text = stripAnsi(outputAfterSubmit);
  if (/login successful|logged in successfully|successfully logged in|you are now logged in/i.test(text)) {
    return { outcome: 'success' };
  }
  const failure = /^.*(?:oauth error|invalid (?:code|authorization code|grant)|invalid_grant|login failed|authentication failed|failed to (?:exchange|log ?in|authenticate)).*$/im.exec(
    text,
  );
  if (failure) return { outcome: 'failure', detail: sanitizeDetail(failure[0]) };
  return undefined;
}

/** Paste codes look like `<code>#<state>`; anything else is rejected before it reaches the PTY. */
export function normalizeAuthCode(code: string): string {
  const trimmed = code.trim();
  if (!/^[A-Za-z0-9._~#-]{8,1024}$/.test(trimmed)) {
    throw new AuthFlowError('invalid_code', 'That does not look like an Anthropic sign-in code. Copy the whole code and try again.');
  }
  return trimmed;
}

export interface ClaudeAuthStatus {
  loggedIn: boolean;
  authMethod?: string;
  apiProvider?: string;
  email?: string;
  orgName?: string;
  subscriptionType?: string;
}

/**
 * Parses `claude auth status` (JSON by default; exit 0 signed in, 1 signed out). Example
 * (CLI 2.1.259): {"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty",
 * "email":"…","orgId":"…","orgName":"…","subscriptionType":"max"}.
 */
export function parseClaudeAuthStatus(stdout: string, exitCode: number | null): ClaudeAuthStatus {
  const start = stdout.indexOf('{');
  const end = stdout.lastIndexOf('}');
  if (start !== -1 && end > start) {
    try {
      const raw: unknown = JSON.parse(stdout.slice(start, end + 1));
      if (typeof raw === 'object' && raw !== null) {
        const r = raw as Record<string, unknown>;
        const pick = (key: string): string | undefined => (typeof r[key] === 'string' && r[key] !== '' ? (r[key] as string) : undefined);
        const authMethod = pick('authMethod');
        const apiProvider = pick('apiProvider');
        const email = pick('email');
        const orgName = pick('orgName');
        const subscriptionType = pick('subscriptionType');
        return {
          loggedIn: r['loggedIn'] === true,
          ...(authMethod ? { authMethod } : {}),
          ...(apiProvider ? { apiProvider } : {}),
          ...(email ? { email } : {}),
          ...(orgName ? { orgName } : {}),
          ...(subscriptionType ? { subscriptionType } : {}),
        };
      }
    } catch {
      // fall through
    }
  }
  if (exitCode === 1) return { loggedIn: false };
  throw new Error('claude auth status returned unreadable output');
}

export function parseCliVersion(output: string): string | null {
  return /(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)/.exec(output)?.[1] ?? null;
}

// ─── Login manager ────────────────────────────────────────────────────────────────────────

export interface ClaudeCliConfig {
  /** /usr/local/bin/as-agent (setpriv wrapper, CONTRACT §2). */
  asAgentPath: string;
  /** The SDK-bundled native `claude` binary. */
  claudeBinPath: string;
  /** Agent home, used as the PTY cwd. */
  homeDir: string;
  /** Allow-listed agent env (src/env.ts); called per spawn so rotations apply. */
  env(): Record<string, string>;
}

export interface ClaudeAuthOptions {
  spawnPty?: PtySpawner;
  /** src/exec.ts runner; defaults to createRunner(cli.asAgentPath). */
  runner?: Runner;
  now?: () => number;
  /** PTY width; wide enough that the OAuth URL is never wrapped (plan: ≥ 400). */
  ptyCols?: number;
  /** How long connect() waits for the sign-in URL. */
  urlTimeoutMs?: number;
  /** Lifetime of a flow awaiting its code. */
  flowTtlMs?: number;
  /** How long submitCode() waits for the CLI's verdict before returning `pending`. */
  outcomeWaitMs?: number;
  commandTimeoutMs?: number;
  /** Called after a sign-in completes or a logout succeeds (adapters drop caches). */
  onAuthChanged?(): void;
}

interface LoginSession {
  flowId: string;
  pty: PtyProcess;
  output: string;
  submitOffset: number | undefined;
  menuAnswered: boolean;
  enterAnswered: boolean;
  verifying: boolean;
  recheck: boolean;
  exited: boolean;
  timers: ReturnType<typeof setTimeout>[];
  waiters: Set<() => void>;
}

const MAX_OUTPUT_CHARS = 256 * 1024;

export class ClaudeAuthManager {
  private readonly flows: FlowRegistry;
  private readonly spawnPty: PtySpawner;
  private readonly run: Runner;
  private readonly now: () => number;
  private session: LoginSession | undefined;

  constructor(
    private readonly cli: ClaudeCliConfig,
    private readonly options: ClaudeAuthOptions = {},
  ) {
    this.now = options.now ?? Date.now;
    this.flows = new FlowRegistry('claude', this.now);
    this.spawnPty = options.spawnPty ?? nodePtySpawner;
    this.run = options.runner ?? createRunner(cli.asAgentPath);
  }

  /** True while a sign-in is in progress (status shows `signing_in`). */
  hasActiveFlow(): boolean {
    return this.flows.active() !== undefined;
  }

  getFlow(flowId: string): ConnectFlow | undefined {
    return this.flows.get(flowId);
  }

  /** Starts `claude auth login`; resolves once the sign-in URL is known (or the flow failed). */
  async connect(): Promise<ConnectFlow> {
    if (this.session) this.endSession(this.session, { state: 'cancelled', detail: 'Superseded by a new sign-in.' });
    const flow = this.flows.create('paste_code');
    let pty: PtyProcess;
    try {
      pty = this.spawnPty(this.cli.asAgentPath, [this.cli.claudeBinPath, 'auth', 'login'], {
        name: 'xterm-256color',
        cols: this.options.ptyCols ?? 1000,
        rows: 50,
        cwd: this.cli.homeDir,
        env: { ...this.cli.env(), TERM: 'xterm-256color' },
      });
    } catch {
      return this.flows.update(flow.flowId, { state: 'failed', detail: 'Could not start claude auth login.' }) ?? flow;
    }
    const session: LoginSession = {
      flowId: flow.flowId,
      pty,
      output: '',
      submitOffset: undefined,
      menuAnswered: false,
      enterAnswered: false,
      verifying: false,
      recheck: false,
      exited: false,
      timers: [],
      waiters: new Set(),
    };
    this.session = session;
    pty.onData((data) => this.onData(session, data));
    pty.onExit((event) => this.onExit(session, event.exitCode));
    const ttl = this.options.flowTtlMs ?? 15 * 60_000;
    session.timers.push(this.timer(() => this.endSession(session, { state: 'expired', detail: 'The sign-in link expired. Start again.' }), ttl));
    await this.waitFor(session, () => this.current(session)?.state !== 'pending', this.options.urlTimeoutMs ?? 30_000);
    if (this.current(session)?.state === 'pending') {
      this.endSession(session, { state: 'failed', detail: 'claude auth login did not print a sign-in URL.' });
    }
    return this.flows.get(flow.flowId) ?? flow;
  }

  /** Types the owner's paste code into the CLI and waits briefly for its verdict. */
  async submitCode(flowId: string, code: string): Promise<ConnectFlow> {
    const flow = this.flows.get(flowId);
    if (!flow) throw new AuthFlowError('flow_not_found', 'Unknown sign-in flow.');
    const session = this.session;
    if (!session || session.flowId !== flowId || flow.state !== 'awaiting_code') {
      throw new AuthFlowError('flow_not_awaiting_code', 'This sign-in is not waiting for a code.');
    }
    const normalized = normalizeAuthCode(code);
    session.submitOffset = session.output.length;
    this.flows.update(flowId, { state: 'pending', detail: 'Verifying the code…' });
    session.pty.write(normalized);
    // Enter as a separate keystroke so the TUI does not treat it as part of a paste.
    session.timers.push(this.timer(() => session.pty.write('\r'), 100));
    await this.waitFor(session, () => isTerminalFlow(this.flows.get(flowId) ?? { state: 'failed' }), this.options.outcomeWaitMs ?? 20_000);
    return this.flows.get(flowId) ?? flow;
  }

  async cancel(flowId: string): Promise<ConnectFlow> {
    const flow = this.flows.get(flowId);
    if (!flow) throw new AuthFlowError('flow_not_found', 'Unknown sign-in flow.');
    if (this.session?.flowId === flowId) this.endSession(this.session, { state: 'cancelled' });
    else if (!isTerminalFlow(flow)) this.flows.update(flowId, { state: 'cancelled' });
    return this.flows.get(flowId) ?? flow;
  }

  /** `claude auth status` as uid agent. */
  async readStatus(): Promise<ClaudeAuthStatus> {
    const result = await this.claude(['auth', 'status']);
    if (result.timedOut) throw new Error('claude auth status timed out');
    return parseClaudeAuthStatus(result.stdout, result.code);
  }

  async version(): Promise<string | null> {
    const result = await this.claude(['--version']);
    return result.code === 0 ? parseCliVersion(result.stdout) : null;
  }

  /** `claude auth logout` inside the container (vendor-side sessions are revoked by the owner). */
  async logout(): Promise<void> {
    if (this.session) this.endSession(this.session, { state: 'cancelled', detail: 'Signed out.' });
    let result: Awaited<ReturnType<Runner>>;
    try {
      result = await this.claude(['auth', 'logout']);
    } catch {
      throw new AuthFlowError('logout_failed', 'Could not run claude auth logout.');
    }
    if (result.code !== 0) {
      throw new AuthFlowError('logout_failed', `claude auth logout failed${result.stderr ? `: ${sanitizeDetail(result.stderr)}` : '.'}`);
    }
    this.options.onAuthChanged?.();
  }

  shutdown(): void {
    if (this.session) this.endSession(this.session, { state: 'cancelled', detail: 'Console shutting down.' });
  }

  /** Runs the bundled binary as uid agent (src/exec.ts runner, no shell, capped output). */
  private claude(args: string[]): ReturnType<Runner> {
    return this.run(this.cli.claudeBinPath, args, {
      asAgent: true,
      env: this.cli.env(),
      cwd: this.cli.homeDir,
      timeoutMs: this.options.commandTimeoutMs ?? 20_000,
      maxOutputBytes: 256 * 1024,
    });
  }

  // ── internals ──

  private current(session: LoginSession): ConnectFlow | undefined {
    return this.flows.get(session.flowId);
  }

  private onData(session: LoginSession, data: string): void {
    session.output += data;
    if (session.output.length > MAX_OUTPUT_CHARS) {
      const drop = session.output.length - MAX_OUTPUT_CHARS;
      session.output = session.output.slice(drop);
      if (session.submitOffset !== undefined) session.submitOffset = Math.max(0, session.submitOffset - drop);
    }
    const flow = this.current(session);
    if (!flow || isTerminalFlow(flow)) return;

    if (flow.state === 'pending' && session.submitOffset === undefined) {
      const url = extractLoginUrl(session.output);
      if (url) {
        const ttl = this.options.flowTtlMs ?? 15 * 60_000;
        this.flows.update(session.flowId, {
          state: 'awaiting_code',
          verificationUrl: url,
          expiresAt: new Date(this.now() + ttl).toISOString(),
        });
      } else if (!session.menuAnswered && isLoginMethodMenu(session.output)) {
        // First entry is "Claude account with subscription"; managed settings also pin
        // forceLoginMethod=claudeai so the menu normally never appears.
        session.menuAnswered = true;
        session.pty.write('\r');
      }
    }

    if (session.submitOffset !== undefined) {
      const after = session.output.slice(session.submitOffset);
      const verdict = detectLoginOutcome(after);
      if (verdict?.outcome === 'success') {
        if (!session.enterAnswered && isPressEnterPrompt(after)) {
          session.enterAnswered = true;
          session.pty.write('\r');
        }
        void this.verify(session);
      } else if (verdict?.outcome === 'failure') {
        this.endSession(session, { state: 'failed', detail: verdict.detail ?? 'Anthropic rejected the code. Start again.' });
      }
    }
    this.notify(session);
  }

  private onExit(session: LoginSession, exitCode: number): void {
    session.exited = true;
    const flow = this.current(session);
    if (flow && !isTerminalFlow(flow)) {
      if (session.submitOffset !== undefined) void this.verify(session);
      else this.endSession(session, { state: 'failed', detail: `claude auth login exited (code ${exitCode}) before sign-in finished.` });
    }
    this.notify(session);
  }

  /**
   * The CLI's own status is the source of truth for success. Triggered by the success text
   * and again by process exit; a trigger that arrives mid-check re-runs it afterwards.
   */
  private async verify(session: LoginSession): Promise<void> {
    if (session.verifying) {
      session.recheck = true;
      return;
    }
    session.verifying = true;
    try {
      const status = await this.readStatus();
      if (status.loggedIn && (status.authMethod === undefined || status.authMethod === 'claude.ai')) {
        this.endSession(session, { state: 'completed', detail: status.subscriptionType ? `Signed in (${status.subscriptionType}).` : 'Signed in.' });
        this.options.onAuthChanged?.();
      } else if (status.loggedIn) {
        this.endSession(session, {
          state: 'failed',
          detail: `Signed in, but not with a Claude subscription (authMethod=${status.authMethod ?? 'unknown'}). The engine will refuse to run.`,
        });
      } else if (session.exited) {
        this.endSession(session, { state: 'failed', detail: 'Sign-in did not complete. Start again.' });
      }
    } catch {
      if (session.exited) this.endSession(session, { state: 'failed', detail: 'Could not confirm the sign-in with claude auth status.' });
    } finally {
      session.verifying = false;
      const flow = this.current(session);
      if (session.recheck && flow && !isTerminalFlow(flow)) {
        session.recheck = false;
        void this.verify(session);
      }
      this.notify(session);
    }
  }

  private endSession(session: LoginSession, patch: FlowPatch & { state: ConnectFlow['state'] }): void {
    this.flows.update(session.flowId, patch);
    for (const t of session.timers) clearTimeout(t);
    session.timers = [];
    if (!session.exited) {
      try {
        session.pty.kill();
      } catch {
        // already gone
      }
      const hardKill = setTimeout(() => {
        if (!session.exited) {
          try {
            session.pty.kill('SIGKILL');
          } catch {
            // already gone
          }
        }
      }, 2_000);
      hardKill.unref?.();
    }
    if (this.session === session) this.session = undefined;
    this.notify(session);
  }

  private notify(session: LoginSession): void {
    for (const waiter of [...session.waiters]) waiter();
  }

  private waitFor(session: LoginSession, done: () => boolean, timeoutMs: number): Promise<void> {
    if (done()) return Promise.resolve();
    return new Promise((resolve) => {
      const finish = (): void => {
        clearTimeout(timer);
        session.waiters.delete(check);
        resolve();
      };
      const check = (): void => {
        if (done()) finish();
      };
      const timer = setTimeout(finish, timeoutMs);
      session.waiters.add(check);
    });
  }

  private timer(fn: () => void, ms: number): ReturnType<typeof setTimeout> {
    const t = setTimeout(fn, ms);
    t.unref?.();
    return t;
  }
}
