#!/usr/bin/env node
// Stand-in for the `claude` and `codex` subscription CLIs, so the REAL writing-ai-sidecars servers
// (writing-ai-sidecars/claude/server.mjs and codex/server.mjs) can run in the load stack unchanged.
// That is the point: the sidecar's own lane (WRITING_LANE_CONCURRENCY, WRITING_QUEUE_MAX,
// WRITING_QUEUE_WAIT_MS) and its 503 lane_busy behaviour are the REAL Writing-grading bottleneck, and
// only the vendor call is replaced by a sleep.
//
//   cli-stub.mjs claude auth status        -> {"loggedIn":true,...}            (login probe)
//   cli-stub.mjs claude -p ...             -> one JSON result event on stdout  (prompt on stdin)
//   cli-stub.mjs codex login status        -> "Logged in using ChatGPT"
//   cli-stub.mjs codex exec ... -          -> item.completed + turn.completed JSON lines
//
// Environment: SIM_LLM_LATENCY_MS (default 8000), SIM_LLM_JITTER_MS (default 2000), SIM_LLM_RESPONSE
// (reply text; default a small JSON object), SIM_LLM_RESPONSE_FILE (read the reply from a file: use it
// to replay a real captured grade), SIM_LLM_FAIL_RATE (0..1, exit non-zero with a quota-looking error).
// The reply is NOT a faithful Writing grade; it is enough to keep the pipeline moving, so grade-parse
// outcomes are not what a load run measures (the lane and the queue are).

import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

const num = (value, fallback) => {
  const n = Number(value);
  return value !== undefined && value !== '' && Number.isFinite(n) ? n : fallback;
};

export function replyText(env = {}) {
  if (env.SIM_LLM_RESPONSE_FILE) return readFileSync(env.SIM_LLM_RESPONSE_FILE, 'utf8');
  return env.SIM_LLM_RESPONSE ?? '{"ok":true,"simulated":true}';
}

export function delayMs(env = {}, random = Math.random) {
  const base = num(env.SIM_LLM_LATENCY_MS, 8000);
  const jitter = num(env.SIM_LLM_JITTER_MS, 2000);
  return Math.max(0, Math.round(base + (random() * 2 - 1) * jitter));
}

/**
 * Decide what the stub prints. Pure: returns { stdout, stderr, code, wait } where `wait` says whether
 * the caller should sleep delayMs() first (only real completions are slow; probes are instant).
 */
export function respond(engine, argv, stdin, env = {}, random = Math.random) {
  if (engine === 'claude') {
    if (argv[0] === 'auth' && argv[1] === 'status') {
      return { stdout: '{"loggedIn":true,"authMethod":"claude.ai","subscriptionType":"max"}\n', stderr: '', code: 0, wait: false };
    }
    if (argv.includes('-p')) {
      if (random() < num(env.SIM_LLM_FAIL_RATE, 0)) {
        return { stdout: '', stderr: 'You have reached your usage limit (simulated)', code: 1, wait: true };
      }
      const text = replyText(env);
      const event = { type: 'result', subtype: 'success', is_error: false, result: text, usage: { input_tokens: Math.ceil(stdin.length / 4), output_tokens: Math.ceil(text.length / 4) } };
      return { stdout: `${JSON.stringify(event)}\n`, stderr: '', code: 0, wait: true };
    }
  }
  if (engine === 'codex') {
    if (argv[0] === 'login' && argv[1] === 'status') return { stdout: 'Logged in using ChatGPT\n', stderr: '', code: 0, wait: false };
    if (argv[0] === 'exec') {
      if (random() < num(env.SIM_LLM_FAIL_RATE, 0)) {
        return { stdout: '', stderr: 'You have reached your usage limit (simulated)', code: 1, wait: true };
      }
      const text = replyText(env);
      const lines = [
        { type: 'item.completed', item: { type: 'agent_message', text } },
        { type: 'turn.completed', usage: { input_tokens: Math.ceil(stdin.length / 4), output_tokens: Math.ceil(text.length / 4) } },
      ];
      return { stdout: `${lines.map((l) => JSON.stringify(l)).join('\n')}\n`, stderr: '', code: 0, wait: true };
    }
  }
  return { stdout: '', stderr: `cli-stub: unsupported invocation ${engine} ${argv.join(' ')}\n`, code: 2, wait: false };
}

async function readStdin() {
  if (process.stdin.isTTY) return '';
  const chunks = [];
  for await (const chunk of process.stdin) chunks.push(chunk);
  return Buffer.concat(chunks).toString('utf8');
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const [engine, ...argv] = process.argv.slice(2);
  const wantsStdin = (engine === 'claude' && argv.includes('-p')) || (engine === 'codex' && argv[0] === 'exec');
  const stdin = wantsStdin ? await readStdin() : '';
  const result = respond(engine, argv, stdin, process.env);
  if (result.wait) await new Promise((resolve) => setTimeout(resolve, delayMs(process.env)));
  process.stdout.write(result.stdout);
  process.stderr.write(result.stderr);
  process.exitCode = result.code;
}
