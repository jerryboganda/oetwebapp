// Claude writing sidecar — serves Anthropic Messages-compatible completions
// from the DEDICATED Claude Max subscription already signed into this
// container's `claude` CLI (see docker-compose.writing-ai.yml).
//
// Wire contract consumed by the .NET AnthropicProvider:
//   POST /v1/messages  →  Anthropic Messages response shape
//   GET  /usage        →  quota snapshot for the subscription selector
//
// The CLI runs with tools disabled and effort pinned by env (WRITING_CLAUDE_EFFORT).
// No candidate content is written to disk: the prompt travels on argv only.

import { createSidecarServer } from '../shared/http.mjs';
import { Mutex, QuotaExceededError, looksLikeQuotaExceeded, parseJsonLines, runCli } from '../shared/engine.mjs';

const MODEL = process.env.WRITING_CLAUDE_MODEL || 'claude-opus-5-5';
const EFFORT = (process.env.WRITING_CLAUDE_EFFORT || 'high').toLowerCase();
const TIMEOUT_MS = Number(process.env.WRITING_CLI_TIMEOUT_MS || 300000);
const mutex = new Mutex();

// Validated live 2026-09-29 against claude-code 2.1.283: `-p` (print) requires
// the prompt on STDIN (a positional prompt is rejected), and the result event is
//   { type:"result", subtype:"success", is_error:false, result:"<text>",
//     modelUsage:{ <model>:{ costUSD, contextWindow, ... } } }
// The facade reads `result` for text and sums modelUsage for tokens/cost.

// Weekly allowance tracking. The subscription CLI does not expose a raw
// "tokens remaining" counter, so we track request counts + token usage
// reported in the result events and combine with a conservative operator-set
// weekly token budget (WRITING_CLAUDE_WEEKLY_TOKEN_CAP) to derive utilisation.
const state = {
  weekStartedAt: weekStart(new Date()),
  requestsThisWeek: 0,
  inputTokensThisWeek: 0,
  outputTokensThisWeek: 0,
  lastResetCheck: Date.now(),
};

function weekStart(d) {
  const x = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()));
  // ISO week start (Monday 00:00 UTC) — matches Anthropic's weekly window closely enough for allowance tracking.
  const day = (x.getUTCDay() + 6) % 7;
  x.setUTCDate(x.getUTCDate() - day);
  return x.getTime();
}

function rollWeekIfNeeded() {
  const now = Date.now();
  if (now - state.weekStartedAt >= 7 * 24 * 3600 * 1000) {
    state.weekStartedAt = weekStart(new Date());
    state.requestsThisWeek = 0;
    state.inputTokensThisWeek = 0;
    state.outputTokensThisWeek = 0;
  }
}

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

async function complete(body) {
  return mutex.run(async () => {
    const model = typeof body.model === 'string' && body.model ? body.model : MODEL;
    const prompt = buildPrompt(body);
    // `-p` with no positional arg reads the prompt from stdin. Tools disabled so
    // the model only reads the prompt and answers — no FS/shell access.
    const args = [
      '-p',
      '--output-format', 'json',
      '--model', model,
      '--effort', EFFORT,
      '--allowedTools', '',
    ];

    let result;
    try {
      result = await runCli('claude', args, { timeoutMs: TIMEOUT_MS, input: prompt });
    } catch (err) {
      throw err;
    }
    const combined = `${result.stdout}\n${result.stderr}`;
    if (result.code !== 0 && looksLikeQuotaExceeded(combined)) {
      throw new QuotaExceededError(`Claude subscription quota/rate limit: ${combined.slice(-400)}`);
    }
    if (result.code !== 0) {
      throw new Error(`claude exited ${result.code}: ${combined.slice(-400)}`);
    }

    const events = parseJsonLines(result.stdout);
    const final = events.find((e) => e && (e.type === 'result' || e.result)) || events[events.length - 1];
    if (!final) throw new Error('claude returned no parseable result event');

    if (final.is_error === true || final.subtype === 'error') {
      const errText = final.result || final.error || combined;
      if (looksLikeQuotaExceeded(String(errText))) throw new QuotaExceededError(String(errText).slice(-400));
      throw new Error(`claude error: ${String(errText).slice(-400)}`);
    }

    const text = typeof final.result === 'string'
      ? final.result
      : (typeof final.text === 'string' ? final.text : '');
    if (!text.trim()) throw new Error('claude returned empty completion');

    const usage = final.usage || final.modelUsage || {};
    const inputTokens = Number(usage.input_tokens ?? usage.inputTokens ?? 0) || 0;
    const outputTokens = Number(usage.output_tokens ?? usage.outputTokens ?? 0) || 0;

    rollWeekIfNeeded();
    state.requestsThisWeek += 1;
    state.inputTokensThisWeek += inputTokens;
    state.outputTokensThisWeek += outputTokens;

    // Anthropic Messages shape.
    return {
      id: `msg_sidecar_${Date.now().toString(36)}`,
      type: 'message',
      role: 'assistant',
      model,
      content: [{ type: 'text', text }],
      stop_reason: 'end_turn',
      usage: { input_tokens: inputTokens, output_tokens: outputTokens },
    };
  });
}

async function usage() {
  rollWeekIfNeeded();
  const cap = Number(process.env.WRITING_CLAUDE_WEEKLY_TOKEN_CAP || 0);
  const used = state.inputTokensThisWeek + state.outputTokensThisWeek;
  const utilisation = cap > 0 ? Math.min(1, used / cap) : null;
  const resetsAt = new Date(state.weekStartedAt + 7 * 24 * 3600 * 1000).toISOString();
  return {
    engine: 'claude',
    model: MODEL,
    effort: EFFORT,
    source: 'estimated', // subscription CLIs don't expose raw allowance counters
    weekStartedAt: new Date(state.weekStartedAt).toISOString(),
    resetsAt,
    requestsThisWeek: state.requestsThisWeek,
    inputTokensThisWeek: state.inputTokensThisWeek,
    outputTokensThisWeek: state.outputTokensThisWeek,
    weeklyTokenCap: cap > 0 ? cap : null,
    utilisation, // 0..1 when a cap is configured, else null (backend falls back to its own estimate)
  };
}

createSidecarServer({
  engineName: 'claude',
  completionPath: '/v1/messages',
  onCompletion: complete,
  onUsage: usage,
  port: Number(process.env.PORT || 8080),
});
