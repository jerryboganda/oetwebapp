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
//  - A per-engine lane runs one request at a time (subscription CLIs are not
//    concurrency-safe on a shared credential set) behind a bounded queue. A
//    request whose client went away is dropped before it runs, or its CLI is
//    killed while it runs, so an abandoned grade neither burns allowance nor
//    blocks the lane. Every refusal is per request: nothing here ever switches
//    the sidecar off.
//  - Quota / rate-limit and dead-login text is normalised into machine-readable
//    `quota_exceeded` / `auth_expired` signals so the backend can fail over
//    without parsing prose. Only ERROR text is read (stderr, error events, error
//    results): a successful grade may quote "429" or "You have reached ...".

import { spawn } from 'node:child_process';

/** CLI quota / rate-limit exhaustion -> HTTP 429 quota_exceeded (backend: QuotaExhausted). */
export class QuotaExceededError extends Error {
  constructor(message) {
    super(message);
    this.name = 'QuotaExceededError';
    this.quotaExceeded = true;
  }
}

/** A dead CLI login (signed out, token expired, revoked or not refreshable) -> HTTP 401
 * auth_expired / authentication_error (backend: Auth). Per request only. */
export class AuthExpiredError extends Error {
  constructor(message) {
    super(message);
    this.name = 'AuthExpiredError';
  }
}

/** The lane cannot take this request (queue full, or it waited too long) -> HTTP 503 lane_busy +
 * Retry-After (backend: Overloaded, retryable). Per request only. */
export class LaneBusyError extends Error {
  constructor(message) {
    super(message);
    this.name = 'LaneBusyError';
  }
}

// Matched against ERROR text only, never model output or usage numbers. Word-bounded and no bare
// status codes: a grade quoting "429" once failed as a quota error and hard-opened the circuit.
const QUOTA_PATTERNS = [
  /\byou(?:['’]ve| have) (?:hit|reached|exhausted)\b/i,
  /\brate[ _-]?limit/i, // also "rate_limit_error"
  /\busage (?:limit|cap|quota)\b/i,
  /\bquota (?:exceeded|exhausted|reached)\b/i,
  /\btoo many requests\b/i,
  /\bweekly (?:limit|allowance)\b/i,
  /\blimit reached\b/i,
  /\btry again (?:at|after|in)\b/i,
  /\bresets? (?:at|in|on)\b/i,
];

const AUTH_PATTERNS = [
  /\bplease run \/login\b/i, // Claude Code: "Invalid API key · Please run /login"
  /\bauthentication_error\b/i, // Anthropic error type inside Claude Code's "API Error: 401 {...}"
  /\boauth token has expired\b/i,
  /\b401 unauthorized\b/i, // Codex: "unexpected status 401 Unauthorized: ..."
  /\brefresh token\b/i, // Codex: "... your refresh token was already used ..."
  /\bnot logged in\b/i,
  /\b(?:log|sign) in again\b/i,
];

/** True when CLI error text indicates quota/rate exhaustion. */
export function looksLikeQuotaExceeded(text) {
  return !!text && QUOTA_PATTERNS.some((re) => re.test(text));
}

/** True when CLI error text indicates a dead login. */
export function looksLikeAuthExpired(text) {
  return !!text && AUTH_PATTERNS.some((re) => re.test(text));
}

/** Joins error fragments (strings, or JSON values such as an error event) into one text. */
export function errorText(...parts) {
  return parts
    .filter((part) => part != null && part !== '')
    .map((part) => (typeof part === 'string' ? part : JSON.stringify(part)))
    .join('\n');
}

/** The typed error for a FAILED CLI run, judged on its error text only (quota before auth, as the
 * backend's parser does); anything else is a plain Error -> 502 engine_error. */
export function cliError(engine, fallback, text) {
  const tail = text.slice(-400);
  if (looksLikeQuotaExceeded(text)) return new QuotaExceededError(`${engine} subscription quota/rate limit: ${tail}`);
  if (looksLikeAuthExpired(text)) return new AuthExpiredError(`${engine} login expired or invalid: ${tail}`);
  return new Error(`${fallback}: ${tail}`);
}

const positiveInt = (value, fallback) => (Number.isInteger(Number(value)) && Number(value) > 0 ? Number(value) : fallback);

/** The lane every completion runs on: at most `concurrency` jobs at once (default 1, the serial
 * mutex), at most `maxQueue` waiting behind them in arrival order. A job that cannot queue, or that
 * waited `queueWaitMs`, is refused with LaneBusyError; a queued job whose signal aborts is dropped
 * before it runs. Defaults: WRITING_LANE_CONCURRENCY (1), WRITING_QUEUE_MAX (40) and
 * WRITING_QUEUE_WAIT_MS (420000, the backend's 7-minute attempt budget). */
export class Mutex {
  #running = 0;
  #waiting = new Set(); // start callbacks, in arrival order

  constructor({
    concurrency = positiveInt(process.env.WRITING_LANE_CONCURRENCY, 1),
    maxQueue = positiveInt(process.env.WRITING_QUEUE_MAX, 40),
    queueWaitMs = positiveInt(process.env.WRITING_QUEUE_WAIT_MS, 420000),
  } = {}) {
    this.concurrency = concurrency;
    this.maxQueue = maxQueue;
    this.queueWaitMs = queueWaitMs;
  }

  /** Jobs waiting for the lane (the running ones excluded). */
  get queueDepth() {
    return this.#waiting.size;
  }

  /** True when a new job would be refused at once. */
  get full() {
    return this.#waiting.size >= this.maxQueue;
  }

  run(fn, { signal } = {}) {
    if (signal?.aborted) return Promise.reject(signal.reason);
    if (this.#running < this.concurrency) return this.#start(fn);
    if (this.full) return Promise.reject(new LaneBusyError(`lane queue full (${this.maxQueue} waiting)`));
    return new Promise((resolve, reject) => {
      const leave = (err) => {
        this.#waiting.delete(start);
        clearTimeout(timer);
        signal?.removeEventListener('abort', onAbort);
        if (err) reject(err);
      };
      const start = () => {
        leave();
        resolve(this.#start(fn));
      };
      const onAbort = () => leave(signal.reason);
      const timer = setTimeout(() => leave(new LaneBusyError(`waited ${this.queueWaitMs}ms for the lane`)), this.queueWaitMs);
      signal?.addEventListener('abort', onAbort, { once: true });
      this.#waiting.add(start);
    });
  }

  async #start(fn) {
    this.#running += 1;
    try {
      return await fn();
    } finally {
      this.#running -= 1;
      const [next] = this.#waiting;
      next?.();
    }
  }
}

/** Spawn `cmd args`, capture stdout/stderr, enforce a hard timeout. When
 * `input` is provided it is written to the child's stdin and the stream is
 * closed (required by `claude -p`, which reads the prompt from stdin). An
 * aborted `signal` (the client went away) SIGKILLs the child and rejects with
 * an AbortError, so an abandoned run stops spending allowance.
 * Resolves { code, stdout, stderr } on exit; rejects on spawn error / timeout / abort.
 * Never streams to the client — the caller parses the captured buffers. */
export function runCli(cmd, args, { timeoutMs = 240000, env = {}, cwd = '/tmp', input, signal } = {}) {
  return new Promise((resolve, reject) => {
    let child;
    try {
      child = spawn(cmd, args, {
        env: { ...process.env, ...env },
        cwd,
        stdio: [input == null ? 'ignore' : 'pipe', 'pipe', 'pipe'],
        // Node kills the child with killSignal on abort and emits 'error' (AbortError) below.
        signal,
        killSignal: 'SIGKILL',
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

const PROBE_TIMEOUT_MS = 5000;
const PROBE_TTL_MS = 60000;

/** Login state for GET /readyz (display only) from a CLI status command (`claude auth status`,
 * `codex login status`). It runs OUTSIDE the lane, so it never waits behind a grade, with a 5 s
 * timeout, cached 60 s. `parse(result)` returns { authOk, plan }: authOk is true/false only when the
 * output says so, else null (unknown); a spawn error or timeout is unknown too, never false. An auth
 * failure on a real request reports authOk=false until the next successful request or for 60 s, so a
 * re-login shows up without a restart. It never refuses a request. */
export function loginProbe(cmd, args, parse, run = runCli) {
  let probe = null; // { at, result: Promise<{ authOk, plan }> }
  let failedAt = null;
  return {
    async status() {
      if (!probe || Date.now() - probe.at >= PROBE_TTL_MS) {
        probe = {
          at: Date.now(),
          result: run(cmd, args, { timeoutMs: PROBE_TIMEOUT_MS }).then(parse).catch(() => ({ authOk: null, plan: null })),
        };
      }
      const login = await probe.result;
      return failedAt !== null && Date.now() - failedAt < PROBE_TTL_MS ? { ...login, authOk: false } : login;
    },
    failed() {
      failedAt = Date.now();
    },
    succeeded() {
      failedAt = null;
    },
  };
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
