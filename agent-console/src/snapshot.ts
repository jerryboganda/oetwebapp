import type { DockerClient } from './docker.js';
import { describeError } from './errors.js';
import type { Logger } from './log.js';

// Control-plane pre-snapshot before a destructive DB operation runs
// (plan: "Pre-snapshot (uid console)"). Executes, through the docker proxy
// with the control token:
//   docker exec oet-db-backup /usr/local/bin/postgres-backup.sh --snapshot <label> [--table t]…
// Full-database dumps are rate limited to one per 10 minutes; table-scoped
// snapshots are preferred and not rate limited. A failed snapshot means the
// destructive operation must not run (runbook §7.1).

export interface SnapshotRequest {
  label: string;
  tables?: string[];
}

export interface SnapshotResult {
  ok: boolean;
  label: string;
  file?: string;
  error?: string;
}

export interface SnapshotServiceOptions {
  docker: Pick<DockerClient, 'exec'>;
  container: string;
  script: string;
  fullDumpMinIntervalMs: number;
  timeoutMs: number;
  now?: () => number;
  logger?: Logger;
}

/** Plain or double-quoted identifier, optionally schema-qualified (quotes are passed through to pg_dump). */
const IDENT_PART = String.raw`(?:"[A-Za-z0-9_$]{1,63}"|[A-Za-z_][A-Za-z0-9_$]{0,62})`;
const TABLE_RE = new RegExp(`^${IDENT_PART}(?:\\.${IDENT_PART})?$`);

/** Safe label for the backup script: starts alphanumeric, [A-Za-z0-9-] only, ≤ 64 chars. */
export function sanitizeLabel(label: string): string {
  const cleaned = label.replace(/[^A-Za-z0-9-]+/g, '-').replace(/-+/g, '-').replace(/^-|-$/g, '');
  return (cleaned || 'snapshot').slice(0, 64).replace(/-$/, '');
}

/**
 * The backup script reports the artifact as `SNAPSHOT_FILE=<path>`; otherwise
 * the last line that looks like an absolute path is used.
 */
export function parseSnapshotOutput(stdout: string): string | null {
  const lines = stdout.split('\n').map((l) => l.trim()).filter(Boolean);
  for (let i = lines.length - 1; i >= 0; i -= 1) {
    const m = /^SNAPSHOT_FILE=(\/\S+)$/.exec(lines[i] as string);
    if (m) return m[1] as string;
  }
  for (let i = lines.length - 1; i >= 0; i -= 1) {
    const line = lines[i] as string;
    if (/^\/\S+$/.test(line)) return line;
  }
  return null;
}

export class SnapshotService {
  private readonly options: SnapshotServiceOptions;
  private readonly now: () => number;
  private lastFullAt: number | null = null;
  private chain: Promise<unknown> = Promise.resolve();

  constructor(options: SnapshotServiceOptions) {
    this.options = options;
    this.now = options.now ?? Date.now;
  }

  /** Serialised: one snapshot at a time. */
  take(request: SnapshotRequest): Promise<SnapshotResult> {
    const run = this.chain.then(() => this.run(request));
    this.chain = run.catch(() => undefined);
    return run;
  }

  private async run(request: SnapshotRequest): Promise<SnapshotResult> {
    const label = sanitizeLabel(request.label);
    const tables = (request.tables ?? []).filter((t) => TABLE_RE.test(t));
    if ((request.tables ?? []).length !== tables.length) {
      return { ok: false, label, error: 'invalid table name for a scoped snapshot' };
    }
    const full = tables.length === 0;
    if (full && this.lastFullAt !== null && this.now() - this.lastFullAt < this.options.fullDumpMinIntervalMs) {
      const waitMin = Math.ceil((this.options.fullDumpMinIntervalMs - (this.now() - this.lastFullAt)) / 60_000);
      return {
        ok: false,
        label,
        error: `full-database snapshots are limited to one per ${Math.round(this.options.fullDumpMinIntervalMs / 60_000)} min (next in ~${waitMin} min); scope the operation to one table`,
      };
    }
    const cmd = [this.options.script, '--snapshot', label, ...tables.flatMap((t) => ['--table', t])];
    try {
      const result = await this.options.docker.exec(this.options.container, cmd, this.options.timeoutMs);
      if (result.exitCode !== 0) {
        const detail = (result.stderr || result.stdout).trim().split('\n').slice(-3).join(' | ');
        return { ok: false, label, error: `snapshot script exited with ${result.exitCode ?? 'no status'}: ${detail.slice(0, 500)}` };
      }
      const file = parseSnapshotOutput(result.stdout);
      if (!file) return { ok: false, label, error: 'snapshot script did not report a file path' };
      if (full) this.lastFullAt = this.now();
      return { ok: true, label, file };
    } catch (error) {
      this.options.logger?.warn({ err: error, label }, 'pre-snapshot failed');
      return { ok: false, label, error: describeError(error) };
    }
  }
}
