// Claude writing sidecar — serves Anthropic Messages-compatible completions
// from the DEDICATED Claude Max subscription already signed into this
// container's `claude` CLI (see docker-compose.writing-ai.yml).
//
// Wire contract consumed by the .NET AnthropicProvider:
//   POST /v1/messages  →  Anthropic Messages response shape
//   GET  /usage        →  quota snapshot for the subscription selector
//   GET  /readyz       →  login (`claude auth status`) + queue state, for display only
//
// The CLI runs with every built-in tool removed (--tools ""), session persistence off
// (--no-session-persistence) and auto-memory off (CLAUDE_CODE_DISABLE_AUTO_MEMORY=1), so a graded
// prompt and reply are not written to $CLAUDE_CONFIG_DIR/projects/-tmp on the volume shared with
// the owner console. Effort is pinned by env (WRITING_CLAUDE_EFFORT). The prompt travels on stdin,
// never on argv. Any other bookkeeping the CLI keeps under CLAUDE_CONFIG_DIR (credential refresh,
// caches) is outside those switches. The Codex sidecar is different: see codex/server.mjs.

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

const MODEL = process.env.WRITING_CLAUDE_MODEL || 'claude-opus-5-5';
const EFFORT = (process.env.WRITING_CLAUDE_EFFORT || 'high').toLowerCase();
const TIMEOUT_MS = Number(process.env.WRITING_CLI_TIMEOUT_MS || 300000);
const mutex = new Mutex();
// This container is ONE Claude Max account (owner directive 2026-10-10): its own credential home
// and its own counters. The primary sidecar's home is shared with the agent console, so its
// counters see only the sidecar's own traffic — a lower bound of the account's real usage.
const accountUsage = new SubscriptionUsageTracker({
  fiveHourCap: Number(process.env.WRITING_CLAUDE_5H_TOKEN_CAP || 0),
  weeklyCap: Number(process.env.WRITING_CLAUDE_WEEKLY_TOKEN_CAP || 0),
});

/** `claude auth status` (see agent-console/src/auth/claude.ts): JSON on stdout, exit 0 signed in /
 * 1 signed out, e.g. {"loggedIn":true,"authMethod":"claude.ai","subscriptionType":"max",...}. Only an
 * explicit boolean loggedIn decides; anything else is unknown (null), never false. */
export function parseClaudeAuthStatus({ stdout }) {
  try {
    const status = JSON.parse(stdout.slice(stdout.indexOf('{'), stdout.lastIndexOf('}') + 1));
    if (typeof status?.loggedIn === 'boolean') {
      const plan = typeof status.subscriptionType === 'string' && status.subscriptionType ? status.subscriptionType : null;
      return { authOk: status.loggedIn, plan };
    }
  } catch { /* unreadable: unknown */ }
  return { authOk: null, plan: null };
}

const login = loginProbe('claude', ['auth', 'status'], parseClaudeAuthStatus);

// Validated live 2026-09-29 against claude-code 2.1.283: `-p` (print) requires
// the prompt on STDIN (a positional prompt is rejected), and the result event is
//   { type:"result", subtype:"success", is_error:false, result:"<text>",
//     modelUsage:{ <model>:{ costUSD, contextWindow, ... } } }
// The facade reads `result` for text and sums modelUsage for tokens/cost.

function buildPrompt(body) {
  const parts = [];
  const system = body.system;
  if (Array.isArray(system)) {
    for (const block of system) {
      if (block && typeof block.text === 'string') parts.push(block.text);
    }
  } else if (typeof system === 'string') {
    parts.push(system);
  }
  for (const msg of body.messages || []) {
    if (!msg) continue;
    if (typeof msg.content === 'string') parts.push(msg.content);
    else if (Array.isArray(msg.content)) {
      for (const c of msg.content) if (c && typeof c.text === 'string') parts.push(c.text);
    }
  }
  return parts.filter(Boolean).join('\n\n');
}

async function complete(body, { signal } = {}) {
  return mutex.run(async () => {
    const model = typeof body.model === 'string' && body.model ? body.model : MODEL;
    const prompt = buildPrompt(body);

    // Auto-guard (owner directive 2026-09-29): run at the configured effort
    // ("max"). Max thinking can consume the whole completion budget and emit an
    // empty answer — if that happens, retry the SAME call at "high" so the
    // subscription is not burned on an empty grade. The retry is reported via
    // the `x-effort-fallback` field in the response so the backend can see it.
    const attempt = async (effort) => {
      // `-p` with no positional arg reads the prompt from stdin. `--tools ""` removes every
      // built-in tool (`--allowedTools` only pre-approves tools, it never removes them), so the
      // model only reads the prompt and answers: no FS/shell access. `--no-session-persistence`
      // stops print mode writing prompt + reply to $CLAUDE_CONFIG_DIR/projects/-tmp/<id>.jsonl on
      // the volume shared with the owner console.
      const args = [
        '-p',
        '--output-format', 'json',
        '--model', model,
        '--effort', effort,
        '--no-session-persistence',
        '--tools', '',
        '--allowedTools', '',
      ];

      const result = await runCli('claude', args, {
        timeoutMs: TIMEOUT_MS,
        input: prompt,
        // Aborted when the backend gives up on the request: the CLI is killed, not left running.
        signal,
        // cwd is always /tmp, so an auto-memory dir would be ONE store shared by every learner
        // and by Writing and Speaking alike.
        env: { CLAUDE_CODE_DISABLE_AUTO_MEMORY: '1' },
      });

      const events = parseJsonLines(result.stdout);
      const final = events.find((e) => e && (e.type === 'result' || e.result)) || events[events.length - 1];
      const failed = final && (final.is_error === true || final.subtype === 'error') ? final : null;
      if (result.code !== 0 || failed) {
        // Error text only: stderr plus the CLI's error result (or its plain-text stdout when there
        // is no JSON). A successful result's model text and usage numbers are never scanned.
        const err = cliError(
          'Claude',
          result.code !== 0 ? `claude exited ${result.code}` : 'claude error',
          errorText(result.stderr, failed?.result, failed?.error, final ? null : result.stdout),
        );
        const failure = isAccountFailure(err);
        if (failure !== null) accountUsage.noteFailure(failure, parseQuotaResetHint(err.message));
        if (err instanceof AuthExpiredError) login.failed();
        throw err;
      }
      if (!final) throw new Error('claude returned no parseable result event');
      login.succeeded();

      const text = typeof final.result === 'string'
        ? final.result
        : (typeof final.text === 'string' ? final.text : '');
      const usage = final.usage || final.modelUsage || {};
      const inputTokens = Number(usage.input_tokens ?? usage.inputTokens ?? 0) || 0;
      const outputTokens = Number(usage.output_tokens ?? usage.outputTokens ?? 0) || 0;
      return { text, inputTokens, outputTokens };
    };

    // Single attempt at the configured effort. No max→high fallback — the owner
    // pinned effort=high everywhere, which is the proven-working setting.
    const out = await attempt(EFFORT);
    if (!out.text.trim()) throw new Error('claude returned empty completion');

    accountUsage.record(out.inputTokens, out.outputTokens);

    // Anthropic Messages shape.
    return {
      id: `msg_sidecar_${Date.now().toString(36)}`,
      type: 'message',
      role: 'assistant',
      model,
      content: [{ type: 'text', text: out.text }],
      stop_reason: 'end_turn',
      usage: { input_tokens: out.inputTokens, output_tokens: out.outputTokens },
      // The effort the grade actually ran at (always the configured value now).
      effort: EFFORT,
    };
  }, { signal });
}

async function usageSnapshot() {
  const snapshot = accountUsage.snapshot();
  const weekly = snapshot.windows.find((w) => w.label === 'weekly');
  const cap = weekly?.cap ?? null;
  return {
    engine: 'claude',
    model: MODEL,
    effort: EFFORT,
    source: 'estimated', // subscription CLIs don't expose raw allowance counters
    account: process.env.WRITING_ACCOUNT_LABEL || 'claude-primary',
    requestsThisWeek: weekly?.requests ?? 0,
    inputTokensThisWeek: weekly?.inputTokens ?? 0,
    outputTokensThisWeek: weekly?.outputTokens ?? 0,
    weeklyTokenCap: cap,
    utilisation: weekly?.usedPct ?? null, // 0..1 when a cap is configured, else null
    weekStartedAt: weekly?.windowStartedAt ?? null,
    resetsAt: weekly?.resetsAt ?? null,
    windows: snapshot.windows,
    lastQuotaErrorAt: snapshot.lastQuotaErrorAt,
    lastQuotaErrorKind: snapshot.lastQuotaErrorKind,
    lastQuotaErrorResetsAt: snapshot.lastQuotaErrorResetsAt,
  };
}

createSidecarServer({
  engineName: 'claude',
  completionPath: '/v1/messages',
  onCompletion: complete,
  onUsage: usageSnapshot,
  lane: mutex,
  login,
  port: Number(process.env.PORT || 8080),
});
