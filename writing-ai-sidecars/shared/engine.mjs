// Shared engine helpers for the writing AI sidecars.
//
// Both sidecars are THIN wrappers around an already-authenticated CLI
// (Claude Code or Codex) running headless inside the container. They spawn
// one short-lived process per request and translate the CLI's JSON output
// into the wire shape the .NET backend expects.
//
// Security rules (mirrors agent-console), per sidecar:
//  - Claude (claude/server.mjs): every built-in tool is removed (--tools ""),
//    session persistence is off (--no-session-persistence), auto-memory is off
//    (CLAUDE_CODE_DISABLE_AUTO_MEMORY=1, passed through runCli's `env`) and the
//    prompt travels on STDIN, so a graded prompt and reply are not written to
//    the CLI's session store. Other CLI bookkeeping under CLAUDE_CONFIG_DIR
//    (credential refresh, caches) is outside those switches.
//  - Codex (codex/server.mjs): read-only sandbox and approval policy `never`;
//    the prompt travels on stdin. It does NOT have the Claude switches above, and
//    whether `codex exec` keeps session/rollout files on disk is unverified, so
//    do not assume candidate content stays off disk there. Speaking has no Codex
//    leg; it carries Writing letters only.
//  - A per-engine mutex serializes requests (subscription CLIs are not
//    concurrency-safe on a shared credential set).
//  - Quota / rate-limit text is normalised into a machine-readable
//    `quota_exceeded` signal so the backend can fail over without parsing
//    prose.

import { spawn } from 'node:child_process';

/** Run `fn` holding the process-wide mutex. Subscription CLIs are not safe to
 * run concurrently against one credential set (OAuth refresh lock), so each
 * sidecar serves one request at a time. */
export class Mutex {
  #tail = Promise.resolve();
  run(fn) {
    const next = this.#tail.then(fn, fn);
    // Keep the chain alive regardless of individual rejections.
    this.#tail = next.then(() => undefined, () => undefined);
    return next;
  }
}

/** Normalised error thrown when the CLI reports quota / rate-limit exhaustion.
 * The HTTP layer maps this to a 429 with `error.code = "quota_exceeded"` so the
 * backend's failover predicate can key on the status code, not the prose. */
export class QuotaExceededError extends Error {
  constructor(message) {
    super(message);
    this.name = 'QuotaExceededError';
    this.quotaExceeded = true;
  }
}

const QUOTA_PATTERNS = [
  /you (have|ve) (hit|reached|exhausted)/i,
  /rate.?limit/i,
  /usage (limit|cap|quota)/i,
  /quota (exceeded|exhausted|reached)/i,
  /too many requests/i,
  /weekly (limit|allowance)/i,
  /limit reached/i,
  /try again (at|after|in)/i,
  /resets? (at|in|on)/i,
  /429/,
];

/** True when CLI output (stdout+stderr tail) indicates quota/rate exhaustion. */
export function looksLikeQuotaExceeded(text) {
  if (!text) return false;
  return QUOTA_PATTERNS.some((re) => re.test(text));
}

/** Spawn `cmd args`, capture stdout/stderr, enforce a hard timeout. When
 * `input` is provided it is written to the child's stdin and the stream is
 * closed (required by `claude -p`, which reads the prompt from stdin).
 * Resolves { code, stdout, stderr } on exit; rejects on spawn error / timeout.
 * Never streams to the client — the caller parses the captured buffers. */
export function runCli(cmd, args, { timeoutMs = 240000, env = {}, cwd = '/tmp', input } = {}) {
  return new Promise((resolve, reject) => {
    let child;
    try {
      child = spawn(cmd, args, {
        env: { ...process.env, ...env },
        cwd,
        stdio: [input == null ? 'ignore' : 'pipe', 'pipe', 'pipe'],
      });
    } catch (err) {
      reject(err);
      return;
    }
    let stdout = '';
    let stderr = '';
    let settled = false;
    const timer = setTimeout(() => {
      if (settled) return;
      settled = true;
      try { child.kill('SIGKILL'); } catch { /* ignore */ }
      reject(new Error(`${cmd} timed out after ${timeoutMs}ms`));
    }, timeoutMs);

    child.stdout.on('data', (d) => { stdout += d.toString(); });
    child.stderr.on('data', (d) => { stderr += d.toString(); });
    child.on('error', (err) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      reject(err);
    });
    child.on('close', (code) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      resolve({ code, stdout, stderr });
    });
    if (input != null) {
      child.stdin.on('error', () => { /* EPIPE if the child exits early — close handles it */ });
      child.stdin.write(input, () => child.stdin.end());
    }
  });
}

/** Parse the last non-empty JSON object line from a stream-json buffer. */
export function parseLastJsonLine(text) {
  const lines = String(text).split('\n').map((l) => l.trim()).filter(Boolean);
  for (let i = lines.length - 1; i >= 0; i -= 1) {
    try { return JSON.parse(lines[i]); } catch { /* not json, keep scanning */ }
  }
  return null;
}

/** Parse every JSON line (for usage / event extraction). */
export function parseJsonLines(text) {
  const out = [];
  for (const line of String(text).split('\n')) {
    const t = line.trim();
    if (!t) continue;
    try { out.push(JSON.parse(t)); } catch { /* skip */ }
  }
  return out;
}
