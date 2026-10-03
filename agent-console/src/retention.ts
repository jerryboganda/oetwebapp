import path from 'node:path';
import type { AppConfig } from './config.js';
import type { Engine } from './engines/types.js';
import { buildAgentEnv, type AgentEnvConfig } from './env.js';
import type { Runner } from './exec.js';
import type { Logger } from './log.js';

// Engine-native transcripts (needed by the engines to resume) live in the agent
// home, outside the control plane's session store (docs/ops/OWNER-AGENT-CONSOLE.md
// §11): Claude Code under $CLAUDE_CONFIG_DIR/projects/<cwd-slug>/<session_id>.jsonl,
// Codex under $CODEX_HOME/sessions/YYYY/MM/DD/rollout-<ts>-<thread_id>.jsonl.
// They are owned by the agent uid, so every deletion runs through as-agent (a
// planted symlink can never redirect a root-privileged delete).

export type RetentionConfig = AgentEnvConfig & Pick<AppConfig, 'claudeConfigDir' | 'codexHome' | 'opencodeBinPath' | 'worktreeRoot'>;

/** Engine session / thread ids are ULID/UUID-like; anything else never reaches `find -name`. */
const ENGINE_ID_PATTERN = /^[A-Za-z0-9_-]{8,128}$/;

export function engineTranscriptDirs(config: Pick<AppConfig, 'claudeConfigDir' | 'codexHome'>): string[] {
  return [path.posix.join(config.claudeConfigDir, 'projects'), path.posix.join(config.codexHome, 'sessions')];
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function countLines(stdout: string): number {
  return stdout.split('\n').filter((line) => line.trim().length > 0).length;
}

/**
 * 90-day window for engine-native transcripts: deletes `*.jsonl` files not
 * modified for `days` days (an engine rewrites its transcript on every resumed
 * turn, so a session still inside the window keeps its files). Returns the
 * number of files removed; never throws.
 */
export async function pruneEngineTranscripts(run: Runner, config: RetentionConfig, days: number, logger?: Logger): Promise<number> {
  if (!Number.isInteger(days) || days < 1) return 0;
  try {
    const result = await run(
      'find',
      [...engineTranscriptDirs(config), '-xdev', '-type', 'f', '-name', '*.jsonl', '-mtime', `+${days}`, '-print', '-delete'],
      { asAgent: true, env: buildAgentEnv(config), timeoutMs: 120_000, maxOutputBytes: 4 * 1024 * 1024 },
    );
    // find exits 1 when a directory does not exist yet (engine never used): not an error.
    return countLines(result.stdout);
  } catch (error) {
    logger?.warn({ err: error instanceof Error ? error.message : String(error) }, 'retention: engine transcript prune failed');
    return 0;
  }
}

/** Prunes OpenCode sessions via its native CLI without touching auth.json. */
export async function pruneOpenCodeSessions(run: Runner, config: RetentionConfig, days: number, logger?: Logger): Promise<number> {
  if (!Number.isInteger(days) || days < 1) return 0;
  let removed = 0;
  const env = buildAgentEnv(config);
  try {
    const dirs = await run(
      'find',
      [config.worktreeRoot, '-xdev', '-mindepth', '1', '-maxdepth', '1', '-type', 'd', '-print'],
      { asAgent: true, env, timeoutMs: 120_000, maxOutputBytes: 4 * 1024 * 1024 },
    );
    const cutoff = Date.now() - days * 86_400_000;
    for (const cwd of dirs.stdout.split(/\r?\n/).filter(Boolean)) {
      try {
        const listed = await run(config.opencodeBinPath, ['--pure', 'session', 'list', '--format', 'json'], {
          asAgent: true,
          cwd,
          env,
          timeoutMs: 30_000,
          maxOutputBytes: 8 * 1024 * 1024,
        });
        if (listed.code !== 0 || listed.timedOut) continue;
        const sessions = listed.stdout.trim() ? JSON.parse(listed.stdout) as unknown : [];
        if (!Array.isArray(sessions)) continue;
        for (const session of sessions) {
          if (!isRecord(session) || typeof session['id'] !== 'string' || !ENGINE_ID_PATTERN.test(session['id'])) continue;
          if (typeof session['updated'] !== 'number' || session['updated'] >= cutoff) continue;
          if (typeof session['directory'] === 'string' && path.posix.resolve(session['directory']) !== path.posix.resolve(cwd)) continue;
          const deleted = await run(config.opencodeBinPath, ['--pure', 'session', 'delete', session['id']], {
            asAgent: true,
            cwd,
            env,
            timeoutMs: 30_000,
            maxOutputBytes: 4096,
          });
          if (!deleted.timedOut && deleted.code === 0) removed += 1;
        }
      } catch {
        logger?.warn('retention: OpenCode session prune failed for one worktree');
      }
    }
  } catch {
    logger?.warn('retention: OpenCode worktree scan failed');
  }
  return removed;
}

/** Erasure: deletes the engine-native transcript data for the given session id. */
export async function removeEngineTranscripts(
  run: Runner,
  config: RetentionConfig,
  ids: readonly string[],
  logger?: Logger,
  engine?: Engine,
  cwd?: string,
): Promise<number> {
  let removed = 0;
  for (const id of new Set(ids)) {
    if (!ENGINE_ID_PATTERN.test(id)) {
      logger?.warn('erase: skipped an engine session id with unexpected characters');
      continue;
    }
    try {
      if (engine === 'opencode') {
        if (!cwd) {
          logger?.warn('erase: OpenCode session has no worktree directory');
          continue;
        }
        const result = await run(config.opencodeBinPath, ['--pure', 'session', 'delete', id], {
          asAgent: true,
          cwd,
          env: buildAgentEnv(config),
          timeoutMs: 30_000,
          maxOutputBytes: 4096,
        });
        if (!result.timedOut && result.code === 0) removed += 1;
        continue;
      }
      const result = await run(
        'find',
        [...engineTranscriptDirs(config), '-xdev', '-type', 'f', '-name', `*${id}*.jsonl`, '-print', '-delete'],
        { asAgent: true, env: buildAgentEnv(config), timeoutMs: 120_000, maxOutputBytes: 1024 * 1024 },
      );
      removed += countLines(result.stdout);
    } catch (error) {
      logger?.warn({ err: error instanceof Error ? error.message : String(error) }, 'erase: engine transcript removal failed');
    }
  }
  return removed;
}
