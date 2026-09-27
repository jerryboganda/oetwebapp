import { mkdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { ulid } from 'ulid';
import type { AppConfig } from './config.js';
import type { ShipPhase, ShipState } from './contract.js';
import { buildControlEnv } from './env.js';
import { conflict, describeError } from './errors.js';
import { runChecked, type RunOptions, type Runner } from './exec.js';
import type { Logger } from './log.js';
import type { Redactor } from './redact.js';
import type { SessionRow, Store } from './store.js';
import { asObject, optString } from './validate.js';

// Ship executor (plan "src/ship.ts", control identity, Ship PAT):
//   scanning   worktree clean, commits ahead of main, blocked extensions,
//              bundle the branch (as the agent) into a root-owned mirror,
//              gitleaks over the new commits and over the PR text
//   pushing    push agent/* (never forced, never main)
//   pr_open    create (or reuse) the PR
//   visibility add a PUBLIC_WINDOW_HOLDERS lease; flip public if private
//   merging    squash-merge the PR at the scanned head SHA
//   deploying  find + watch "Build & Deploy (web + API)" for the merge SHA
//   health     the three live health URLs
//   restoring_visibility  drop the lease; private again when no holders and
//              no queued/in-progress runs (a watchdog forces private after 90 min)
// The control identity never runs git inside the agent-writable repository.

export const BLOCKED_SHIP_EXTENSIONS: readonly string[] = ['.sql', '.dump', '.csv', '.jsonl'];
const HOLDER_PREFIX = 'agent-console:';
const WINDOW_KEY = 'public_window';

/** Paths that must never be shipped from the console (plan: block .sql .dump .csv .jsonl .env*). */
export function blockedShipFiles(paths: readonly string[]): string[] {
  return paths.filter((p) => {
    const base = path.posix.basename(p.replace(/\\/g, '/')).toLowerCase();
    if (base.startsWith('.env')) return true;
    return BLOCKED_SHIP_EXTENSIONS.some((ext) => base.endsWith(ext));
  });
}

export function isShippableBranch(branch: string): boolean {
  return /^agent\/[a-z0-9][a-z0-9._-]{0,150}$/.test(branch) && !branch.includes('..') && !branch.endsWith('.lock') && !branch.endsWith('.');
}

/** PUBLIC_WINDOW_HOLDERS: a JSON array of holder ids (comma/space lists are tolerated on read). */
export function parseHolders(value: string | null | undefined): string[] {
  if (!value || !value.trim()) return [];
  try {
    const parsed = JSON.parse(value) as unknown;
    if (Array.isArray(parsed)) {
      return [...new Set(parsed.filter((h): h is string => typeof h === 'string').map((h) => h.trim()).filter(Boolean))];
    }
  } catch {
    // fall through to the tolerant list format
  }
  return [...new Set(value.split(/[\s,]+/).map((h) => h.trim()).filter(Boolean))];
}

export function serializeHolders(holders: readonly string[]): string {
  return JSON.stringify([...new Set(holders)].sort());
}

export function withHolder(holders: readonly string[], holder: string): string[] {
  return holders.includes(holder) ? [...holders] : [...holders, holder];
}

export function withoutHolder(holders: readonly string[], holder: string): string[] {
  return holders.filter((h) => h !== holder);
}

/** Number of findings in a gitleaks JSON report, or null when unreadable. */
export function countGitleaksFindings(report: string): number | null {
  try {
    const parsed = JSON.parse(report) as unknown;
    return Array.isArray(parsed) ? parsed.length : null;
  } catch {
    return null;
  }
}

export class GhApiError extends Error {
  readonly status: number | null;
  constructor(status: number | null, message: string) {
    super(message);
    this.name = 'GhApiError';
    this.status = status;
  }
}

interface PublicWindow {
  openedAt: number;
  deadline: number;
  holders: string[];
  sessionIds: string[];
}

export interface ShipSessions {
  requireSession(id: string): SessionRow;
  isRunning(id: string): boolean;
  setPullRequest(id: string, pr: { number: number; url: string; state: string }): void;
}

export interface ShipCredentials {
  shipEnv(extra?: Record<string, string>): Promise<Record<string, string> | null>;
}

export interface ShipDeps {
  config: Pick<AppConfig, 'ship' | 'controlHome' | 'egressProxyUrl' | 'noProxy'>;
  store: Store;
  run: Runner;
  credentials: ShipCredentials;
  sessions: ShipSessions;
  /** Env for git commands run as the agent (bundle, status). */
  agentEnv: () => Record<string, string>;
  emit: (sessionId: string, data: { phase: ShipPhase; message: string; level: 'info' | 'warn' | 'error' }) => void;
  redactor: Redactor;
  logger: Logger;
  now?: () => number;
  sleep?: (ms: number) => Promise<void>;
  /** Poll/timeout tuning (tests). */
  timings?: Partial<ShipTimings>;
}

export interface ShipTimings {
  runLookupTimeoutMs: number;
  runLookupIntervalMs: number;
  runWatchTimeoutMs: number;
  runWatchIntervalMs: number;
  healthAttempts: number;
  healthIntervalMs: number;
  watchdogIntervalMs: number;
}

const DEFAULT_TIMINGS: ShipTimings = {
  runLookupTimeoutMs: 15 * 60_000,
  runLookupIntervalMs: 20_000,
  runWatchTimeoutMs: 80 * 60_000,
  runWatchIntervalMs: 30_000,
  healthAttempts: 6,
  healthIntervalMs: 20_000,
  watchdogIntervalMs: 60_000,
};

class ShipFailure extends Error {}

interface ShipInput {
  prTitle?: string;
  prBody?: string;
}

export class ShipExecutor {
  private queue: Promise<void> = Promise.resolve();
  private readonly activeBySession = new Map<string, ShipState>();
  private watchdog: NodeJS.Timeout | null = null;
  private readonly now: () => number;
  private readonly sleep: (ms: number) => Promise<void>;
  private readonly timings: ShipTimings;

  constructor(private readonly deps: ShipDeps) {
    this.now = deps.now ?? Date.now;
    this.sleep =
      deps.sleep ??
      ((ms: number) =>
        new Promise<void>((resolve) => {
          setTimeout(resolve, ms).unref();
        }));
    this.timings = { ...DEFAULT_TIMINGS, ...deps.timings };
  }

  private get repo(): string {
    return this.deps.config.ship.repo;
  }

  get(sessionId: string): ShipState | null {
    return this.activeBySession.get(sessionId) ?? this.deps.store.getLatestShip(sessionId);
  }

  /**
   * "Apply update" (CONTRACT.md §5): dispatch the console's own deploy workflow
   * (`agent-console.yml`, input apply=true) on the base branch with the Ship PAT
   * (needs Actions: write). Never throws; the caller has already set draining.
   */
  async dispatchConsoleUpdate(): Promise<{ dispatched: boolean; detail?: string }> {
    const env = await this.deps.credentials.shipEnv();
    if (!env) return { dispatched: false, detail: 'Set the Ship token in Settings to dispatch the update, or run agent-console.yml with apply=true.' };
    const workflow = this.deps.config.ship.consoleWorkflowFile;
    try {
      await this.gh(env, 'POST', `repos/${this.repo}/actions/workflows/${workflow}/dispatches`, {
        ref: this.deps.config.ship.baseBranch,
        inputs: { apply: 'true' },
      });
      this.deps.logger.info({ workflow }, 'apply-update: dispatched the console workflow with apply=true');
      return { dispatched: true };
    } catch (error) {
      const detail = error instanceof Error ? this.deps.redactor.redact(error.message).slice(0, 300) : 'dispatch failed';
      this.deps.logger.warn({ detail }, 'apply-update: workflow dispatch failed');
      return { dispatched: false, detail };
    }
  }

  async start(sessionId: string, body: unknown): Promise<ShipState> {
    const obj = asObject(body);
    const input: ShipInput = {};
    const prTitle = optString(obj, 'prTitle', 256);
    const prBody = optString(obj, 'prBody', 60_000, { allowEmpty: true });
    if (prTitle !== undefined) input.prTitle = prTitle;
    if (prBody !== undefined) input.prBody = prBody;

    const session = this.deps.sessions.requireSession(sessionId);
    if (session.archived) throw conflict('session_archived', 'The session is archived.');
    if (this.deps.sessions.isRunning(sessionId)) throw conflict('turn_in_progress', 'Wait for the running turn to finish before shipping.');
    if (this.activeBySession.has(sessionId)) throw conflict('ship_in_progress', 'A ship is already in progress for this session.');
    if (!isShippableBranch(session.branch)) throw conflict('branch_not_shippable', 'Only agent/* branches can be shipped.');
    if (!(await this.deps.credentials.shipEnv())) throw conflict('ship_token_missing', 'Set the Ship token in Settings first.');
    // Re-check after the await: two concurrent requests must not both start a ship.
    if (this.activeBySession.has(sessionId)) throw conflict('ship_in_progress', 'A ship is already in progress for this session.');
    if (this.deps.sessions.isRunning(sessionId)) throw conflict('turn_in_progress', 'Wait for the running turn to finish before shipping.');

    const nowIso = new Date(this.now()).toISOString();
    const state: ShipState = { shipId: ulid(), sessionId, phase: 'queued', startedAt: nowIso, updatedAt: nowIso };
    this.activeBySession.set(sessionId, state);
    this.save(state);
    this.deps.emit(sessionId, { phase: 'queued', message: 'Ship queued.', level: 'info' });
    this.enqueue(() => this.execute(state, session, input));
    return { ...state };
  }

  /** Boot: continue or close out ships that were in flight when the process stopped. */
  resume(): void {
    for (const state of this.deps.store.listUnfinishedShips()) {
      this.activeBySession.set(state.sessionId, state);
      this.enqueue(() => this.resumeOne(state));
    }
  }

  startWatchdog(): void {
    if (this.watchdog) return;
    this.watchdog = setInterval(() => {
      void this.watchdogTick().catch((error: unknown) => {
        this.deps.logger.error({ err: describeError(error) }, 'visibility watchdog failed');
      });
    }, this.timings.watchdogIntervalMs);
    this.watchdog.unref();
  }

  stop(): void {
    if (this.watchdog) clearInterval(this.watchdog);
    this.watchdog = null;
  }

  /** Forces the repository private once a public window opened by a ship exceeds its deadline. */
  async watchdogTick(): Promise<boolean> {
    const window = this.deps.store.getKv<PublicWindow>(WINDOW_KEY);
    if (!window || this.now() < window.deadline) return false;
    const env = await this.deps.credentials.shipEnv();
    if (!env) {
      this.deps.logger.error('visibility watchdog: ship token missing; cannot restore private');
      return false;
    }
    const repo = await this.gh<{ private: boolean }>(env, 'GET', `repos/${this.repo}`);
    if (!repo.private) await this.gh(env, 'PATCH', `repos/${this.repo}`, { visibility: 'private' });
    const holders = await this.readHolders(env).catch(() => [] as string[]);
    const others = holders.filter((h) => !h.startsWith(HOLDER_PREFIX));
    await this.writeHolders(env, others).catch(() => undefined);
    this.deps.store.deleteKv(WINDOW_KEY);
    for (const sessionId of window.sessionIds) {
      this.deps.emit(sessionId, {
        phase: 'restoring_visibility',
        message: `Visibility watchdog forced the repository private after the public window exceeded ${Math.round(this.deps.config.ship.publicWindowMaxMs / 60_000)} min${others.length ? ` (other holders: ${others.join(', ')})` : ''}.`,
        level: 'warn',
      });
    }
    this.deps.logger.warn({ others }, 'visibility watchdog forced the repository private');
    return true;
  }

  // ------------------------------------------------------------- pipeline

  private enqueue(job: () => Promise<void>): void {
    this.queue = this.queue.then(job).catch((error: unknown) => {
      this.deps.logger.error({ err: describeError(error) }, 'ship job crashed');
    });
  }

  private save(state: ShipState): void {
    state.updatedAt = new Date(this.now()).toISOString();
    this.deps.store.saveShip(state);
  }

  private phase(state: ShipState, phase: ShipPhase, message: string, level: 'info' | 'warn' | 'error' = 'info'): void {
    state.phase = phase;
    this.save(state);
    this.deps.emit(state.sessionId, { phase, message: this.deps.redactor.redact(message), level });
  }

  private note(state: ShipState, message: string, level: 'info' | 'warn' | 'error' = 'info'): void {
    this.deps.emit(state.sessionId, { phase: state.phase, message: this.deps.redactor.redact(message), level });
  }

  private finish(state: ShipState, outcome: 'done' | 'failed', message: string): void {
    if (outcome === 'failed') state.error = this.deps.redactor.redact(message).slice(0, 1000);
    this.phase(state, outcome, message, outcome === 'failed' ? 'error' : 'info');
    this.activeBySession.delete(state.sessionId);
  }

  private async execute(state: ShipState, session: SessionRow, input: ShipInput): Promise<void> {
    const env = await this.deps.credentials.shipEnv();
    if (!env) {
      this.finish(state, 'failed', 'Ship token is not set.');
      return;
    }
    const holder = `${HOLDER_PREFIX}${state.shipId}`;
    let holderAdded = false;
    try {
      const title = (input.prTitle ?? session.title).trim().slice(0, 256) || `Agent console: ${session.branch}`;
      const bodyText =
        input.prBody ??
        [
          'Shipped from the Owner Agent Console.',
          '',
          `- Session: ${session.id}`,
          `- Engine: ${session.engine} (${session.model})`,
          `- Branch: \`${session.branch}\``,
        ].join('\n');
      const headSha = await this.scan(state, session, title, bodyText, env);
      await this.push(state, session, env);
      const pr = await this.openPullRequest(state, session, title, bodyText, env);
      holderAdded = true;
      await this.openWindow(state, holder, env);
      const mergeSha = await this.merge(state, session, pr.number, title, headSha, env);
      await this.deployAndCheck(state, mergeSha, env);
      await this.restoreVisibility(state, holder, env);
      this.finish(state, 'done', `Shipped: PR #${pr.number} merged as ${mergeSha.slice(0, 12)}, deploy green, health green.`);
    } catch (error) {
      const message = error instanceof ShipFailure || error instanceof GhApiError ? error.message : describeError(error);
      this.deps.logger.warn({ shipId: state.shipId, err: this.deps.redactor.redact(message) }, 'ship failed');
      state.error = this.deps.redactor.redact(message).slice(0, 1000);
      if (holderAdded) {
        await this.restoreVisibility(state, holder, env).catch((restoreError: unknown) => {
          this.note(state, `Could not restore visibility: ${describeError(restoreError)}; the watchdog will force private.`, 'error');
        });
      }
      this.finish(state, 'failed', message);
    } finally {
      await rm(this.bundlePath(state), { force: true }).catch(() => undefined);
    }
  }

  private async resumeOne(state: ShipState): Promise<void> {
    const env = await this.deps.credentials.shipEnv();
    const holder = `${HOLDER_PREFIX}${state.shipId}`;
    if (!env) {
      this.finish(state, 'failed', 'Interrupted by a console restart and the ship token is missing.');
      return;
    }
    try {
      if (['queued', 'scanning', 'pushing', 'pr_open'].includes(state.phase)) {
        this.finish(state, 'failed', 'Interrupted by a console restart before merging; start Ship again.');
        return;
      }
      if (state.phase === 'restoring_visibility') {
        await this.restoreVisibility(state, holder, env);
        if (state.error) this.finish(state, 'failed', state.error);
        else this.finish(state, 'done', 'Visibility restored after a console restart.');
        return;
      }
      let mergeSha = state.mergeSha;
      if (!mergeSha && state.prNumber) {
        const pr = await this.gh<{ merged: boolean; merge_commit_sha: string | null }>(env, 'GET', `repos/${this.repo}/pulls/${state.prNumber}`);
        if (pr.merged && pr.merge_commit_sha) mergeSha = pr.merge_commit_sha;
      }
      if (!mergeSha) throw new ShipFailure('Interrupted by a console restart before the merge completed.');
      state.mergeSha = mergeSha;
      await this.deployAndCheck(state, mergeSha, env);
      await this.restoreVisibility(state, holder, env);
      this.finish(state, 'done', `Resumed after restart: ${mergeSha.slice(0, 12)} deployed and healthy.`);
    } catch (error) {
      await this.restoreVisibility(state, holder, env).catch(() => undefined);
      this.finish(state, 'failed', describeError(error));
    }
  }

  private bundlePath(state: ShipState): string {
    return path.posix.join(this.deps.config.ship.workDir, `${state.shipId}.bundle`);
  }

  private async agentGit(worktree: string, args: string[], options: Partial<RunOptions> = {}) {
    return runChecked(this.deps.run, 'git', ['-C', worktree, ...args], {
      asAgent: true,
      env: this.deps.agentEnv(),
      timeoutMs: 5 * 60_000,
      ...options,
    });
  }

  private async mirrorGit(env: Record<string, string>, args: string[], okCodes: number[] = [0]) {
    return runChecked(
      this.deps.run,
      'git',
      ['-C', this.deps.config.ship.mirrorDir, ...args],
      { env, timeoutMs: 10 * 60_000, maxOutputBytes: 16 * 1024 * 1024 },
      okCodes,
    );
  }

  private async ensureMirror(env: Record<string, string>): Promise<void> {
    const { mirrorDir, baseBranch } = this.deps.config.ship;
    const exists = await stat(path.posix.join(mirrorDir, 'HEAD')).then(() => true, () => false);
    if (!exists) {
      await mkdir(path.posix.dirname(mirrorDir), { recursive: true, mode: 0o700 });
      await runChecked(
        this.deps.run,
        'git',
        ['clone', '--bare', '--filter=blob:none', '--no-tags', '--single-branch', '--branch', baseBranch, `https://github.com/${this.repo}.git`, mirrorDir],
        { env, timeoutMs: 20 * 60_000 },
      );
    }
    await this.mirrorGit(env, ['fetch', '--no-tags', 'origin', `+refs/heads/${baseBranch}:refs/remotes/origin/${baseBranch}`]);
  }

  private async scan(state: ShipState, session: SessionRow, title: string, body: string, env: Record<string, string>): Promise<string> {
    const { baseBranch, workDir, gitleaksPath } = this.deps.config.ship;
    const branch = session.branch;
    const wt = session.worktree;
    this.phase(state, 'scanning', 'Checking the branch, blocked files and secrets.');

    const current = (await this.agentGit(wt, ['symbolic-ref', '--short', 'HEAD'])).stdout.trim();
    if (current !== branch) throw new ShipFailure(`The worktree is on "${current}", not the session branch ${branch}.`);
    const dirty = (await this.agentGit(wt, ['status', '--porcelain', '--untracked-files=normal'])).stdout.trim();
    if (dirty) throw new ShipFailure('The worktree has uncommitted changes; ask the agent to commit (or discard) them first.');
    await this.agentGit(wt, ['fetch', '--no-tags', 'origin', `+refs/heads/${baseBranch}:refs/remotes/origin/${baseBranch}`]);
    const ahead = Number((await this.agentGit(wt, ['rev-list', '--count', `origin/${baseBranch}..HEAD`])).stdout.trim());
    if (!Number.isFinite(ahead) || ahead === 0) throw new ShipFailure('Nothing to ship: the branch has no commits ahead of main.');
    const headSha = (await this.agentGit(wt, ['rev-parse', 'HEAD'])).stdout.trim();

    // Bundle as the agent → stdout → root-owned file; no agent-writable path is ever read as root.
    const bundle = await this.agentGit(wt, ['bundle', 'create', '-', branch, `^origin/${baseBranch}`], {
      maxOutputBytes: 512 * 1024 * 1024,
      timeoutMs: 10 * 60_000,
    });
    if (bundle.stdoutTruncated) throw new ShipFailure('The branch bundle is larger than 512 MiB.');
    await mkdir(workDir, { recursive: true, mode: 0o700 });
    const bundleFile = this.bundlePath(state);
    await writeFile(bundleFile, bundle.stdoutBuffer, { mode: 0o600 });

    await this.ensureMirror(env);
    await this.mirrorGit(env, ['bundle', 'verify', '--quiet', bundleFile]);
    await this.mirrorGit(env, ['fetch', '--no-tags', bundleFile, `+refs/heads/${branch}:refs/heads/${branch}`]);
    const mirrored = (await this.mirrorGit(env, ['rev-parse', `refs/heads/${branch}`])).stdout.trim();
    if (mirrored !== headSha) throw new ShipFailure('The bundled branch does not match the worktree HEAD.');

    const changed = (await this.mirrorGit(env, ['diff', '--name-only', '--diff-filter=ACMR', '-z', `refs/remotes/origin/${baseBranch}...refs/heads/${branch}`])).stdout
      .split('\0')
      .filter(Boolean);
    const blocked = blockedShipFiles(changed);
    if (blocked.length > 0) {
      throw new ShipFailure(`Blocked file types cannot be shipped from the console: ${blocked.slice(0, 10).join(', ')}${blocked.length > 10 ? ' …' : ''}`);
    }

    const commitReport = path.posix.join(workDir, `${state.shipId}.gitleaks-commits.json`);
    const commitScan = await this.deps.run(
      gitleaksPath,
      ['git', '--no-banner', '--redact', '--exit-code', '1', '--report-format', 'json', '--report-path', commitReport, '--log-opts', `refs/remotes/origin/${baseBranch}..refs/heads/${branch}`, this.deps.config.ship.mirrorDir],
      { env, timeoutMs: 10 * 60_000 },
    );
    await this.checkGitleaks(commitScan.code, commitReport, 'commits', commitScan.stderr);

    const textReport = path.posix.join(workDir, `${state.shipId}.gitleaks-pr.json`);
    const textScan = await this.deps.run(
      gitleaksPath,
      ['stdin', '--no-banner', '--redact', '--exit-code', '1', '--report-format', 'json', '--report-path', textReport],
      { env, input: `${title}\n\n${body}\n`, timeoutMs: 2 * 60_000 },
    );
    await this.checkGitleaks(textScan.code, textReport, 'PR title/body', textScan.stderr);
    this.note(state, `Scan clean: ${ahead} commit(s), ${changed.length} file(s).`);
    return headSha;
  }

  private async checkGitleaks(code: number | null, reportFile: string, what: string, stderr: string): Promise<void> {
    const report = await readFile(reportFile, 'utf8').catch(() => null);
    await rm(reportFile, { force: true }).catch(() => undefined);
    const findings = report === null ? null : countGitleaksFindings(report);
    if (code === 0) return;
    if (findings !== null && findings > 0) throw new ShipFailure(`gitleaks found ${findings} potential secret(s) in the ${what}; remove them and retry.`);
    throw new ShipFailure(`gitleaks could not scan the ${what}: ${this.deps.redactor.redact(stderr.trim().split('\n').slice(-2).join(' ')).slice(0, 300)}`);
  }

  private async push(state: ShipState, session: SessionRow, env: Record<string, string>): Promise<void> {
    this.phase(state, 'pushing', `Pushing ${session.branch}.`);
    if (!isShippableBranch(session.branch)) throw new ShipFailure('Only agent/* branches can be pushed.');
    await this.mirrorGit(env, ['push', '--no-verify', '--porcelain', 'origin', `refs/heads/${session.branch}:refs/heads/${session.branch}`]);
  }

  private async openPullRequest(state: ShipState, session: SessionRow, title: string, body: string, env: Record<string, string>): Promise<{ number: number; url: string }> {
    this.phase(state, 'pr_open', 'Opening the pull request.');
    const owner = this.repo.split('/')[0] as string;
    let pr: { number: number; html_url: string };
    try {
      pr = await this.gh<{ number: number; html_url: string }>(env, 'POST', `repos/${this.repo}/pulls`, {
        title,
        head: session.branch,
        base: this.deps.config.ship.baseBranch,
        body,
        maintainer_can_modify: false,
      });
    } catch (error) {
      if (!(error instanceof GhApiError) || error.status !== 422) throw error;
      const existing = await this.gh<{ number: number; html_url: string }[]>(
        env,
        'GET',
        `repos/${this.repo}/pulls?state=open&head=${encodeURIComponent(`${owner}:${session.branch}`)}`,
      );
      const first = existing[0];
      if (!first) throw error;
      pr = first;
    }
    state.prNumber = pr.number;
    state.prUrl = pr.html_url;
    this.save(state);
    this.deps.sessions.setPullRequest(session.id, { number: pr.number, url: pr.html_url, state: 'open' });
    this.note(state, `PR #${pr.number} open: ${pr.html_url}`);
    return { number: pr.number, url: pr.html_url };
  }

  private async openWindow(state: ShipState, holder: string, env: Record<string, string>): Promise<void> {
    this.phase(state, 'visibility', 'Taking the public-window lease.');
    const holders = await this.readHolders(env);
    await this.writeHolders(env, withHolder(holders, holder));
    const repo = await this.gh<{ private: boolean }>(env, 'GET', `repos/${this.repo}`);
    const window = this.deps.store.getKv<PublicWindow>(WINDOW_KEY);
    if (repo.private) {
      await this.gh(env, 'PATCH', `repos/${this.repo}`, { visibility: 'public' });
      const openedAt = this.now();
      this.deps.store.setKv(WINDOW_KEY, {
        openedAt,
        deadline: openedAt + this.deps.config.ship.publicWindowMaxMs,
        holders: [holder],
        sessionIds: [state.sessionId],
      } satisfies PublicWindow);
      this.note(state, 'Repository flipped public for the deploy window (hosted runners).', 'warn');
    } else if (window) {
      this.deps.store.setKv(WINDOW_KEY, {
        ...window,
        holders: withHolder(window.holders, holder),
        sessionIds: [...new Set([...window.sessionIds, state.sessionId])],
      } satisfies PublicWindow);
    }
  }

  private async merge(state: ShipState, session: SessionRow, prNumber: number, title: string, headSha: string, env: Record<string, string>): Promise<string> {
    this.phase(state, 'merging', `Squash-merging PR #${prNumber}.`);
    const result = await this.gh<{ merged: boolean; sha: string; message?: string }>(env, 'PUT', `repos/${this.repo}/pulls/${prNumber}/merge`, {
      merge_method: 'squash',
      sha: headSha,
      commit_title: `${title} (#${prNumber})`,
    });
    if (!result.merged || !result.sha) throw new ShipFailure(`GitHub did not merge PR #${prNumber}: ${result.message ?? 'unknown reason'}`);
    state.mergeSha = result.sha;
    this.save(state);
    this.deps.sessions.setPullRequest(session.id, { number: prNumber, url: state.prUrl ?? '', state: 'merged' });
    this.note(state, `Merged as ${result.sha.slice(0, 12)}.`);
    return result.sha;
  }

  private async deployAndCheck(state: ShipState, mergeSha: string, env: Record<string, string>): Promise<void> {
    const { deployWorkflowFile, deployWorkflowName, healthUrls } = this.deps.config.ship;
    this.phase(state, 'deploying', `Waiting for "${deployWorkflowName}" on ${mergeSha.slice(0, 12)}.`);
    type Run = { id: number; html_url: string; status: string; conclusion: string | null; head_sha: string };
    let run: Run | undefined;
    const lookupDeadline = this.now() + this.timings.runLookupTimeoutMs;
    while (!run) {
      const runs = await this.gh<{ workflow_runs: Run[] }>(
        env,
        'GET',
        `repos/${this.repo}/actions/workflows/${encodeURIComponent(deployWorkflowFile)}/runs?head_sha=${mergeSha}&per_page=10`,
      );
      run = runs.workflow_runs.find((r) => r.head_sha === mergeSha);
      if (run) break;
      if (this.now() >= lookupDeadline) throw new ShipFailure(`No "${deployWorkflowName}" run appeared for ${mergeSha.slice(0, 12)}.`);
      await this.sleep(this.timings.runLookupIntervalMs);
    }
    state.runUrl = run.html_url;
    this.save(state);
    this.note(state, `Deploy run: ${run.html_url}`);
    const watchDeadline = this.now() + this.timings.runWatchTimeoutMs;
    let lastStatus = run.status;
    while (run.status !== 'completed') {
      if (this.now() >= watchDeadline) throw new ShipFailure(`"${deployWorkflowName}" did not finish in time (${run.html_url}).`);
      await this.sleep(this.timings.runWatchIntervalMs);
      run = await this.gh<Run>(env, 'GET', `repos/${this.repo}/actions/runs/${run.id}`);
      if (run.status !== lastStatus) {
        lastStatus = run.status;
        this.note(state, `Deploy run is ${run.status}.`);
      }
    }
    if (run.conclusion !== 'success') throw new ShipFailure(`"${deployWorkflowName}" concluded ${run.conclusion ?? 'without a conclusion'}: ${run.html_url}`);

    this.phase(state, 'health', 'Checking live health.');
    const probeEnv = buildControlEnv(this.deps.config);
    for (const url of healthUrls) {
      let healthy = false;
      for (let attempt = 1; attempt <= this.timings.healthAttempts && !healthy; attempt += 1) {
        const probe = await this.deps.run('curl', ['-sS', '-o', '/dev/null', '-w', '%{http_code}', '--max-time', '20', url], {
          env: probeEnv,
          timeoutMs: 30_000,
        });
        const code = Number(probe.stdout.trim());
        healthy = probe.code === 0 && code >= 200 && code < 300;
        if (!healthy && attempt < this.timings.healthAttempts) await this.sleep(this.timings.healthIntervalMs);
      }
      if (!healthy) throw new ShipFailure(`Health check failed: ${url}`);
      this.note(state, `Healthy: ${url}`);
    }
  }

  private async restoreVisibility(state: ShipState, holder: string, env: Record<string, string>): Promise<void> {
    const failed = state.error !== undefined;
    state.phase = 'restoring_visibility';
    this.save(state);
    this.deps.emit(state.sessionId, { phase: 'restoring_visibility', message: 'Releasing the public-window lease.', level: failed ? 'warn' : 'info' });
    const holders = withoutHolder(await this.readHolders(env), holder);
    await this.writeHolders(env, holders);
    const window = this.deps.store.getKv<PublicWindow>(WINDOW_KEY);
    if (window) this.deps.store.setKv(WINDOW_KEY, { ...window, holders: withoutHolder(window.holders, holder) } satisfies PublicWindow);
    if (holders.length > 0) {
      this.note(state, `Repository stays public for other holders: ${holders.join(', ')}.`);
      return;
    }
    const busy = await this.activeRunCount(env);
    if (busy > 0) {
      this.note(state, `Repository stays public: ${busy} workflow run(s) still queued or in progress; the watchdog restores private at the latest ${Math.round(this.deps.config.ship.publicWindowMaxMs / 60_000)} min after the window opened.`, 'warn');
      if (!window) {
        const openedAt = this.now();
        this.deps.store.setKv(WINDOW_KEY, { openedAt, deadline: openedAt + this.deps.config.ship.publicWindowMaxMs, holders: [], sessionIds: [state.sessionId] } satisfies PublicWindow);
      }
      return;
    }
    const repo = await this.gh<{ private: boolean }>(env, 'GET', `repos/${this.repo}`);
    if (!repo.private) await this.gh(env, 'PATCH', `repos/${this.repo}`, { visibility: 'private' });
    this.deps.store.deleteKv(WINDOW_KEY);
    this.note(state, 'Repository is private again.');
  }

  private async activeRunCount(env: Record<string, string>): Promise<number> {
    let total = 0;
    for (const status of ['queued', 'in_progress']) {
      const res = await this.gh<{ total_count: number }>(env, 'GET', `repos/${this.repo}/actions/runs?status=${status}&per_page=1`);
      total += res.total_count ?? 0;
    }
    return total;
  }

  private async readHolders(env: Record<string, string>): Promise<string[]> {
    try {
      const variable = await this.gh<{ value: string }>(env, 'GET', `repos/${this.repo}/actions/variables/${this.deps.config.ship.holdersVariable}`);
      return parseHolders(variable.value);
    } catch (error) {
      if (error instanceof GhApiError && error.status === 404) return [];
      throw error;
    }
  }

  private async writeHolders(env: Record<string, string>, holders: string[]): Promise<void> {
    const name = this.deps.config.ship.holdersVariable;
    const value = serializeHolders(holders);
    try {
      await this.gh(env, 'PATCH', `repos/${this.repo}/actions/variables/${name}`, { name, value });
    } catch (error) {
      if (!(error instanceof GhApiError) || error.status !== 404) throw error;
      await this.gh(env, 'POST', `repos/${this.repo}/actions/variables`, { name, value });
    }
  }

  /** GitHub REST through `gh api` (honours HTTPS_PROXY; token only in env). */
  private async gh<T = unknown>(env: Record<string, string>, method: string, endpoint: string, body?: unknown): Promise<T> {
    const args = ['api', '-X', method, endpoint, '-H', 'Accept: application/vnd.github+json', '-H', 'X-GitHub-Api-Version: 2022-11-28'];
    const options: RunOptions = { env, timeoutMs: 60_000, maxOutputBytes: 8 * 1024 * 1024 };
    if (body !== undefined) {
      args.push('--input', '-');
      options.input = JSON.stringify(body);
    }
    const result = await this.deps.run('gh', args, options);
    if (result.code !== 0) {
      const status = /HTTP (\d{3})/.exec(result.stderr)?.[1];
      const detail = this.deps.redactor.redact(result.stderr.trim().split('\n')[0] ?? '').slice(0, 300);
      throw new GhApiError(status ? Number(status) : null, `GitHub ${method} ${endpoint.split('?')[0]} failed${status ? ` (HTTP ${status})` : ''}: ${detail}`);
    }
    const text = result.stdout.trim();
    return (text ? JSON.parse(text) : null) as T;
  }
}
