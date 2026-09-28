// Codex writing sidecar — serves OpenAI chat-completions-compatible responses
// from the Codex/ChatGPT subscription signed into this container's `codex` CLI.
//
// Wire contract consumed by the .NET RegistryBackedProvider (OpenAiCompatible):
//   POST /v1/chat/completions  →  OpenAI chat.completion shape
//   GET  /usage                →  quota snapshot for the subscription selector
//
// Uses `codex exec` (non-interactive headless mode) with a JSON event stream.
// Tools are sandboxed off; the prompt travels on argv; nothing is written to
// disk by the request path.

import { createSidecarServer } from '../shared/http.mjs';
import { Mutex, QuotaExceededError, looksLikeQuotaExceeded, parseJsonLines, runCli } from '../shared/engine.mjs';

const MODEL = process.env.WRITING_CODEX_MODEL || 'gpt-6-sol';
const EFFORT = (process.env.WRITING_CODEX_EFFORT || 'high').toLowerCase();
const TIMEOUT_MS = Number(process.env.WRITING_CLI_TIMEOUT_MS || 300000);
const mutex = new Mutex();

const state = {
  weekStartedAt: Date.now(),
  requestsThisWeek: 0,
  inputTokensThisWeek: 0,
  outputTokensThisWeek: 0,
};

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

async function complete(body) {
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
      prompt,
    ];

    let result;
    try {
      result = await runCli('codex', args, { timeoutMs: TIMEOUT_MS, cwd: '/tmp' });
    } catch (err) {
      throw err;
    }
    const combined = `${result.stdout}\n${result.stderr}`;
    if (looksLikeQuotaExceeded(combined)) {
      throw new QuotaExceededError(`Codex subscription quota/rate limit: ${combined.slice(-400)}`);
    }
    if (result.code !== 0) {
      throw new Error(`codex exited ${result.code}: ${combined.slice(-400)}`);
    }

    const events = parseJsonLines(result.stdout);
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
      } else if (e.type === 'error') {
        const msg = e.message || JSON.stringify(e);
        if (looksLikeQuotaExceeded(msg)) throw new QuotaExceededError(String(msg).slice(-400));
        throw new Error(`codex error: ${String(msg).slice(-400)}`);
      }
    }
    if (!text.trim()) {
      // Fallback: last non-json stdout text (codex without --json would print the answer).
      const tail = result.stdout.trim().split('\n').filter((l) => l.trim() && !l.trim().startsWith('{')).pop();
      text = tail || '';
    }
    if (!text.trim()) throw new Error('codex returned empty completion');

    const inputTokens = Number(usage?.input_tokens ?? usage?.inputTokens ?? 0) || 0;
    const outputTokens = Number(usage?.output_tokens ?? usage?.outputTokens ?? usage?.completion_tokens ?? 0) || 0;

    state.requestsThisWeek += 1;
    state.inputTokensThisWeek += inputTokens;
    state.outputTokensThisWeek += outputTokens;

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
  });
}

async function usage() {
  const cap = Number(process.env.WRITING_CODEX_WEEKLY_TOKEN_CAP || 0);
  const used = state.inputTokensThisWeek + state.outputTokensThisWeek;
  const utilisation = cap > 0 ? Math.min(1, used / cap) : null;
  return {
    engine: 'codex',
    model: MODEL,
    effort: EFFORT,
    source: 'estimated',
    weekStartedAt: new Date(state.weekStartedAt).toISOString(),
    requestsThisWeek: state.requestsThisWeek,
    inputTokensThisWeek: state.inputTokensThisWeek,
    outputTokensThisWeek: state.outputTokensThisWeek,
    weeklyTokenCap: cap > 0 ? cap : null,
    utilisation,
  };
}

createSidecarServer({
  engineName: 'codex',
  completionPath: '/v1/chat/completions',
  onCompletion: complete,
  onUsage: usage,
  port: Number(process.env.PORT || 8080),
});
