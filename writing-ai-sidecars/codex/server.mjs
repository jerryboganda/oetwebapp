// Codex writing sidecar — serves OpenAI chat-completions-compatible responses
// from the Codex/ChatGPT subscription signed into this container's `codex` CLI.
//
// Wire contract consumed by the .NET RegistryBackedProvider (OpenAiCompatible):
//   POST /v1/chat/completions  →  OpenAI chat.completion shape
//   GET  /usage                →  quota snapshot for the subscription selector
//   GET  /readyz               →  login (`codex login status`) + queue state, for display only
//
// Uses `codex exec` (non-interactive headless mode) with a JSON event stream.
// Tools are sandboxed off; the prompt travels on stdin. CLI session/rollout
// persistence is unverified.

import { createSidecarServer } from '../shared/http.mjs';
import {
  AuthExpiredError,
  Mutex,
  SubscriptionUsageTracker,
  cliError,
  errorText,
  isAccountFailure,
  loginProbe,
  parseJsonLines,
  parseQuotaResetHint,
  runCli,
} from '../shared/engine.mjs';

// Owner 2026-10-02: GPT-6.1 Sol High. The backend always sends the model; this is only the default.
const MODEL = process.env.WRITING_CODEX_MODEL || 'gpt-6.1-sol';
const EFFORT = (process.env.WRITING_CODEX_EFFORT || 'high').toLowerCase();
const TIMEOUT_MS = Number(process.env.WRITING_CLI_TIMEOUT_MS || 300000);
const mutex = new Mutex();
// This container is ONE ChatGPT Business account (owner directive 2026-10-10): its own credential
// volume and its own counters. The backend rotates between the accounts from these numbers.
const accountUsage = new SubscriptionUsageTracker({
  fiveHourCap: Number(process.env.WRITING_CODEX_5H_TOKEN_CAP || 0),
  weeklyCap: Number(process.env.WRITING_CODEX_WEEKLY_TOKEN_CAP || 0),
});

/** `codex login status` prints "Logged in using ChatGPT" (exit 0) or "Not logged in" (exit 1), a
 * local auth-file read. Anything else is unknown (null), never false. */
export function parseCodexLoginStatus({ code, stdout, stderr }) {
  const text = `${stdout}\n${stderr}`;
  if (/\bnot logged in\b/i.test(text)) return { authOk: false, plan: null };
  if (code === 0 && /\blogged in\b/i.test(text)) return { authOk: true, plan: null };
  return { authOk: null, plan: null };
}

const login = loginProbe('codex', ['login', 'status'], parseCodexLoginStatus);

function buildPrompt(body) {
  const parts = [];
  for (const msg of body.messages || []) {
    if (!msg) continue;
    if (typeof msg.content === 'string') parts.push(msg.content);
    else if (Array.isArray(msg.content)) {
      for (const c of msg.content) {
        if (c && typeof c.text === 'string') parts.push(c.text);
        else if (c && c.type === 'text' && typeof c.text === 'string') parts.push(c.text);
      }
    }
  }
  return parts.filter(Boolean).join('\n\n');
}

async function complete(body, { signal } = {}) {
  return mutex.run(async () => {
    const model = typeof body.model === 'string' && body.model ? body.model : MODEL;
    const prompt = buildPrompt(body);
    // Validated live 2026-09-29 against codex-cli 0.157.1. `--ignore-user-config`
    // escapes the agent-console's /etc/codex/requirements.toml (which forces
    // approval_policy=UnlessTrusted and would hang headless `exec`); we then set a
    // self-contained config: never ask for approval, read-only sandbox, high
    // reasoning. `--json` emits item.completed(agent_message.text) + a final
    // turn.completed{usage:{input_tokens, output_tokens, ...}}.
    const args = [
      'exec',
      '--skip-git-repo-check',
      '--ignore-user-config',
      '--json',
      '--model', model,
      '-c', `model_reasoning_effort="${EFFORT}"`,
      '-c', 'approval_policy="never"',
      '-c', 'sandbox_mode="read-only"',
      '-',
    ];

    const result = await runCli('codex', args, { timeoutMs: TIMEOUT_MS, cwd: '/tmp', input: prompt, signal });
    const events = parseJsonLines(result.stdout);
    // Error text only, and only for a failed run: stderr plus `error` / `turn.failed` events. The
    // agent's text and the usage numbers are never scanned: a clean grade can quote "429" or
    // "You have reached ...", and a false quota error hard-opens the shared Codex circuit.
    const errors = events
      .filter((e) => e?.type === 'error' || e?.type === 'turn.failed')
      .map((e) => e.message ?? e.error?.message ?? e.error ?? e);
    if (result.code !== 0 || errors.length) {
      const err = cliError(
        'Codex',
        result.code !== 0 ? `codex exited ${result.code}` : 'codex error',
        errorText(result.stderr, ...errors),
      );
      const failure = isAccountFailure(err);
      if (failure !== null) accountUsage.noteFailure(failure, parseQuotaResetHint(err.message));
      if (err instanceof AuthExpiredError) login.failed();
      throw err;
    }
    login.succeeded();

    let text = '';
    let usage = null;
    for (const e of events) {
      if (!e) continue;
      // codex --json emits { type: "item.completed", item: { type:"agent_message", text } }
      // and a final { type: "turn.completed", usage: {...} }.
      if (e.type === 'item.completed' && e.item && typeof e.item.text === 'string' && e.item.type === 'agent_message') {
        text += (text ? '\n' : '') + e.item.text;
      } else if (e.type === 'turn.completed' && e.usage) {
        usage = e.usage;
      }
    }
    if (!text.trim()) {
      // Fallback: last non-json stdout text (codex without --json would print the answer).
      const tail = result.stdout.trim().split('\n').filter((l) => l.trim() && !l.trim().startsWith('{')).pop();
      text = tail || '';
    }
    if (!text.trim()) throw new Error('codex returned empty completion');

    const inputTokens = Number(usage?.input_tokens ?? usage?.inputTokens ?? 0) || 0;
    const outputTokens = Number(usage?.output_tokens ?? usage?.completion_tokens ?? 0) || 0;

    accountUsage.record(inputTokens, outputTokens);

    // OpenAI chat.completion shape.
    return {
      id: `chatcmpl-sidecar-${Date.now().toString(36)}`,
      object: 'chat.completion',
      created: Math.floor(Date.now() / 1000),
      model,
      choices: [
        {
          index: 0,
          message: { role: 'assistant', content: text },
          finish_reason: 'stop',
        },
      ],
      usage: { prompt_tokens: inputTokens, completion_tokens: outputTokens, total_tokens: inputTokens + outputTokens },
    };
  }, { signal });
}

async function usageSnapshot() {
  const snapshot = accountUsage.snapshot();
  const weekly = snapshot.windows.find((w) => w.label === 'weekly');
  const cap = weekly?.cap ?? null;
  return {
    engine: 'codex',
    model: MODEL,
    effort: EFFORT,
    source: 'estimated', // subscription CLIs expose no official allowance counter
    account: process.env.WRITING_ACCOUNT_LABEL || 'codex-primary',
    requestsThisWeek: weekly?.requests ?? 0,
    inputTokensThisWeek: weekly?.inputTokens ?? 0,
    outputTokensThisWeek: weekly?.outputTokens ?? 0,
    weeklyTokenCap: cap,
    utilisation: weekly?.usedPct ?? null,
    weekStartedAt: weekly?.windowStartedAt ?? null,
    windows: snapshot.windows,
    lastQuotaErrorAt: snapshot.lastQuotaErrorAt,
    lastQuotaErrorKind: snapshot.lastQuotaErrorKind,
    lastQuotaErrorResetsAt: snapshot.lastQuotaErrorResetsAt,
  };
}

createSidecarServer({
  engineName: 'codex',
  completionPath: '/v1/chat/completions',
  onCompletion: complete,
  onUsage: usageSnapshot,
  lane: mutex,
  login,
  port: Number(process.env.PORT || 8080),
});
