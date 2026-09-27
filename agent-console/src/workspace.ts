import { statfs } from 'node:fs/promises';
import path from 'node:path';
import type { SessionDiff, SessionDiffFile } from './contract.js';
import { buildAgentEnv, type AgentEnvConfig } from './env.js';
import { runChecked, type Runner } from './exec.js';
import type { Logger } from './log.js';

// Git workspace (plan Phase 1 "src/workspace.ts"). Every git command runs as
// the agent uid through the as-agent wrapper — the control identity never
// executes git inside an agent-writable repository (hooks and repo config are
// agent-controlled). Credentials come from the agent's own gh credential
// helper (src/github.ts).

export interface WorkspaceConfig extends AgentEnvConfig {
  repoDir: string;
  worktreeRoot: string;
  workspaceRoot: string;
  repoCloneUrl: string;
  diffCapBytes: number;
  minFreeDiskBytes: number;
  ship: { baseBranch: string };
}

export interface Worktree {
  path: string;
  branch: string;
}

export interface DiskStatus {
  freeBytes: number;
  ok: boolean;
}

/** The subset SessionManager / ShipExecutor depend on (fakes in tests). */
export interface WorkspaceApi {
  ensureRepo(): Promise<void>;
  createWorktree(sessionId: string, title: string, now?: Date): Promise<Worktree>;
  restoreWorktree(worktree: Worktree): Promise<void>;
  removeWorktree(worktreePath: string): Promise<void>;
  diff(worktree: Worktree): Promise<SessionDiff>;
  checkDisk(): Promise<DiskStatus>;
  readAgentsMd(worktreePath: string): Promise<string>;
}

const GIT_TIMEOUT = 5 * 60_000;

/** agent/<yyyymmdd>-<slug>-<id suffix> */
export function branchName(sessionId: string, title: string, now: Date = new Date()): string {
  const date = now.toISOString().slice(0, 10).replace(/-/g, '');
  const slug =
    title
      .normalize('NFKD')
      .replace(/[̀-ͯ]/g, '')
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 40)
      .replace(/-+$/g, '') || 'session';
  return `agent/${date}-${slug}-${sessionId.slice(-6).toLowerCase()}`;
}

/** Parses `git diff --numstat -z` output. */
export function parseNumstatZ(output: string): Map<string, { additions: number; deletions: number }> {
  const result = new Map<string, { additions: number; deletions: number }>();
  const parts = output.split('\0');
  let i = 0;
  while (i < parts.length) {
    const head = parts[i] ?? '';
    i += 1;
    if (!head) continue;
    const m = /^(-|\d+)\t(-|\d+)\t(.*)$/.exec(head);
    if (!m) continue;
    const additions = m[1] === '-' ? 0 : Number(m[1]);
    const deletions = m[2] === '-' ? 0 : Number(m[2]);
    let file = m[3] ?? '';
    if (file === '') {
      // rename/copy: "\0old\0new"
      i += 1; // old path
      file = parts[i] ?? '';
      i += 1;
    }
    if (file) result.set(file, { additions, deletions });
  }
  return result;
}

/** Parses `git diff --name-status -z` output. */
export function parseNameStatusZ(output: string): { path: string; status: SessionDiffFile['status'] }[] {
  const out: { path: string; status: SessionDiffFile['status'] }[] = [];
  const parts = output.split('\0');
  let i = 0;
  while (i < parts.length) {
    const code = parts[i] ?? '';
    i += 1;
    if (!code) continue;
    const kind = code[0];
    if (kind === 'R' || kind === 'C') {
      i += 1; // old path
      const newPath = parts[i] ?? '';
      i += 1;
      out.push({ path: newPath, status: kind === 'R' ? 'renamed' : 'added' });
      continue;
    }
    const file = parts[i] ?? '';
    i += 1;
    const status: SessionDiffFile['status'] = kind === 'A' ? 'added' : kind === 'D' ? 'deleted' : 'modified';
    out.push({ path: file, status });
  }
  return out;
}

/** Cuts a patch at the last newline before `capBytes`. */
export function capPatch(patch: string, capBytes: number): { patch: string; truncated: boolean } {
  const bytes = Buffer.byteLength(patch, 'utf8');
  if (bytes <= capBytes) return { patch, truncated: false };
  let cut = Buffer.from(patch, 'utf8').subarray(0, capBytes).toString('utf8');
  // Drop a possibly split multi-byte char and the partial last line.
  cut = cut.replace(/�$/, '');
  const nl = cut.lastIndexOf('\n');
  return { patch: nl > 0 ? cut.slice(0, nl + 1) : cut, truncated: true };
}

export class Workspace implements WorkspaceApi {
  private repoReady: Promise<void> | null = null;

  constructor(
    private readonly config: WorkspaceConfig,
    private readonly run: Runner,
    private readonly logger: Logger,
  ) {}

  private env(): Record<string, string> {
    return buildAgentEnv(this.config);
  }

  private git(args: string[], options: { cwd?: string; okCodes?: number[]; maxOutputBytes?: number } = {}) {
    return runChecked(
      this.run,
      'git',
      args,
      {
        asAgent: true,
        env: this.env(),
        cwd: options.cwd ?? this.config.workspaceRoot,
        timeoutMs: GIT_TIMEOUT,
        ...(options.maxOutputBytes ? { maxOutputBytes: options.maxOutputBytes } : {}),
      },
      options.okCodes ?? [0],
    );
  }

  /** Clones (blobless) on first use, otherwise fetches. Concurrent callers share one attempt. */
  ensureRepo(): Promise<void> {
    if (!this.repoReady) {
      this.repoReady = this.prepareRepo().catch((error: unknown) => {
        this.repoReady = null;
        throw error;
      });
    }
    return this.repoReady;
  }

  private async prepareRepo(): Promise<void> {
    await runChecked(this.run, 'mkdir', ['-p', this.config.worktreeRoot], { asAgent: true, env: this.env() });
    const probe = await this.run('git', ['-C', this.config.repoDir, 'rev-parse', '--git-dir'], {
      asAgent: true,
      env: this.env(),
      timeoutMs: 30_000,
    });
    if (probe.code !== 0) {
      // Full clone (not blobless): the repo is private most of the time and the console may
      // have no GitHub token, so every object must already be local (history, blame, diffs).
      this.logger.info({ repoDir: this.config.repoDir }, 'cloning workspace repository');
      await this.git(['clone', '--no-tags', this.config.repoCloneUrl, this.config.repoDir]);
    }
    await this.fetchBase(true);
    await this.git(['-C', this.config.repoDir, 'worktree', 'prune']);
  }

  /**
   * Refreshes origin/<base>. GitHub is unreachable while the repo is private and no agent
   * token is set (owner choice: the owner commits/pushes from the dev machine), so a failed
   * fetch falls back to the last fetched base instead of blocking every new session.
   */
  private async fetchBase(prune: boolean): Promise<void> {
    const base = this.config.ship.baseBranch;
    try {
      await this.git(['-C', this.config.repoDir, 'fetch', ...(prune ? ['--prune'] : []), '--no-tags', 'origin', `+refs/heads/${base}:refs/remotes/origin/${base}`]);
    } catch (error) {
      const known = await this.run('git', ['-C', this.config.repoDir, 'rev-parse', '--verify', '--quiet', `refs/remotes/origin/${base}`], {
        asAgent: true,
        env: this.env(),
        timeoutMs: 30_000,
      });
      if (known.code !== 0) throw error;
      this.logger.warn(
        { base, error: error instanceof Error ? error.message.slice(0, 300) : String(error) },
        'workspace fetch failed (private repo / no GitHub token?); using the last fetched base',
      );
    }
  }

  async createWorktree(sessionId: string, title: string, now: Date = new Date()): Promise<Worktree> {
    await this.ensureRepo();
    // Fresh base for every session when GitHub is reachable.
    await this.fetchBase(false);
    const branch = branchName(sessionId, title, now);
    const worktreePath = path.posix.join(this.config.worktreeRoot, sessionId);
    await this.git(['-C', this.config.repoDir, 'worktree', 'add', '-b', branch, worktreePath, `origin/${this.config.ship.baseBranch}`]);
    return { path: worktreePath, branch };
  }

  async restoreWorktree(worktree: Worktree): Promise<void> {
    await this.ensureRepo();
    const probe = await this.run('git', ['-C', worktree.path, 'rev-parse', '--is-inside-work-tree'], {
      asAgent: true,
      env: this.env(),
      timeoutMs: 30_000,
    });
    if (probe.code === 0) return;
    await this.git(['-C', this.config.repoDir, 'worktree', 'prune']);
    await this.git(['-C', this.config.repoDir, 'worktree', 'add', worktree.path, worktree.branch]);
  }

  /** Removes the worktree directory; the branch ref stays (cheap, and shipped work is on GitHub). */
  async removeWorktree(worktreePath: string): Promise<void> {
    const result = await this.run('git', ['-C', this.config.repoDir, 'worktree', 'remove', '--force', worktreePath], {
      asAgent: true,
      env: this.env(),
      timeoutMs: GIT_TIMEOUT,
    });
    if (result.code !== 0) {
      this.logger.warn({ worktreePath, stderr: result.stderr.slice(0, 300) }, 'git worktree remove failed; pruning');
      await this.run('rm', ['-rf', '--', worktreePath], { asAgent: true, env: this.env(), timeoutMs: GIT_TIMEOUT });
    }
    await this.run('git', ['-C', this.config.repoDir, 'worktree', 'prune'], { asAgent: true, env: this.env(), timeoutMs: GIT_TIMEOUT });
  }

  /**
   * SessionDiff: merge-base(origin/main, HEAD) → working tree (committed +
   * uncommitted) plus untracked files, patch capped at diffCapBytes.
   */
  async diff(worktree: Worktree): Promise<SessionDiff> {
    const cwd = worktree.path;
    const base = `origin/${this.config.ship.baseBranch}`;
    const head = (await this.git(['rev-parse', 'HEAD'], { cwd })).stdout.trim();
    const mergeBase = (await this.git(['merge-base', base, 'HEAD'], { cwd })).stdout.trim();
    const cap = this.config.diffCapBytes;

    const numstat = parseNumstatZ((await this.git(['diff', '--numstat', '-z', '-M', mergeBase], { cwd })).stdout);
    const statuses = parseNameStatusZ((await this.git(['diff', '--name-status', '-z', '-M', mergeBase], { cwd })).stdout);
    // --no-ext-diff / --no-textconv: the repo config and .gitattributes are
    // agent-writable, and the owner must review the real bytes, not a filtered view.
    const patchRun = await this.git(['diff', '-M', '--no-color', '--no-ext-diff', '--no-textconv', mergeBase], { cwd, maxOutputBytes: cap + 1 });
    let patch = patchRun.stdout;
    let truncated = patchRun.stdoutTruncated;

    const files: SessionDiffFile[] = statuses.map((s) => ({
      path: s.path,
      status: s.status,
      additions: numstat.get(s.path)?.additions ?? 0,
      deletions: numstat.get(s.path)?.deletions ?? 0,
    }));

    const untracked = (await this.git(['ls-files', '--others', '--exclude-standard', '-z'], { cwd })).stdout
      .split('\0')
      .filter(Boolean);
    const MAX_UNTRACKED_PATCHES = 200;
    for (const [index, file] of untracked.entries()) {
      let additions = 0;
      if (index < MAX_UNTRACKED_PATCHES && Buffer.byteLength(patch, 'utf8') < cap) {
        const room = cap - Buffer.byteLength(patch, 'utf8');
        const one = await this.git(['diff', '--no-index', '--no-color', '--no-ext-diff', '--no-textconv', '--', '/dev/null', file], {
          cwd,
          okCodes: [0, 1],
          maxOutputBytes: room + 1,
        });
        patch += one.stdout;
        if (one.stdoutTruncated) truncated = true;
        additions = one.stdout.split('\n').filter((l) => l.startsWith('+') && !l.startsWith('+++')).length;
      } else {
        truncated = true;
      }
      files.push({ path: file, status: 'untracked', additions, deletions: 0 });
    }

    const capped = capPatch(patch, cap);
    return {
      baseRef: base,
      head,
      branch: worktree.branch,
      files,
      patch: capped.patch,
      truncated: truncated || capped.truncated,
    };
  }

  async checkDisk(): Promise<DiskStatus> {
    try {
      const stats = await statfs(this.config.workspaceRoot);
      const freeBytes = Number(stats.bavail) * Number(stats.bsize);
      return { freeBytes, ok: freeBytes >= this.config.minFreeDiskBytes };
    } catch (error) {
      this.logger.warn({ err: error }, 'disk usage check failed');
      return { freeBytes: -1, ok: true };
    }
  }

  /**
   * Worktree AGENTS.md for the engine's system prompt. Read as the agent uid
   * (never as the control identity) so a symlink cannot expose control secrets.
   */
  async readAgentsMd(worktreePath: string): Promise<string> {
    const result = await this.run('cat', ['--', path.posix.join(worktreePath, 'AGENTS.md')], {
      asAgent: true,
      env: this.env(),
      timeoutMs: 15_000,
      maxOutputBytes: 64 * 1024,
    });
    return result.code === 0 ? result.stdout : '';
  }
}
