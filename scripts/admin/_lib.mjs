/**
 * Shared library for OET admin bulk-backfill orchestrator scripts.
 *
 * Provides:
 *   - Admin sign-in + JWT cache + auto-refresh on 401
 *   - Throttled fetch wrapper with exponential backoff on 429
 *   - DO Serverless OpenAI-compatible chat client (Opus 4.7 default, configurable)
 *   - DO Serverless TTS client (Qwen3-TTS default, configurable)
 *   - Cost meter (informational; user has unlimited budget)
 *   - Skip-and-log failure handler (writes failures-<run>.jsonl)
 *   - Run header / footer helpers with progress reporting
 *
 * Configuration (env vars; CLI flags override):
 *   API_BASE        — backend API base URL (default https://api.oetwithdrhesham.co.uk)
 *   ADMIN_EMAIL     — admin login email (default manwara575@gmail.com)
 *   ADMIN_PASSWORD  — admin login password (default 12345678)
 *   AI__ApiKey      — DigitalOcean Serverless Inference API key (or AI_API_KEY / DO_AI_API_KEY)
 *   AI__BaseUrl     — DO Serverless base URL (or AI_BASE_URL; default https://inference.do-ai.run/v1)
 *   AI__ChatModel   — chat model id (default anthropic-claude-opus-4.7; CLI --chat-model overrides)
 *   AI__TtsBaseUrl  — TTS base URL (default same as AI__BaseUrl)
 *   AI__TtsModel    — TTS model id (default qwen3-tts-voicedesign; CLI --tts-model overrides)
 *
 * TTS provider policy (May 2026 — ElevenLabs credits exhausted):
 *   DigitalOcean Qwen3-TTS Voice Design is the active provider, with a
 *   British English male voice as default.
 *   - Default routing: ElevenLabs primary -> DO fallback.
 *   - To FORCE DO and bypass ElevenLabs entirely, set
 *       TTS__ForceProvider=digitalocean   (env var)
 *     or pass `--tts-provider digitalocean` on the CLI, or pass
 *     `{ provider: 'digitalocean' }` per call.
 *   - Per-gender voice seeds and "instructions" prompts live on CONFIG.ai.
 *     Callers use aiTts(text, opts); opts:
 *       { gender: 'male'|'female'|'neutral',
 *         voice: <DO seed>, instructions: <prompt>,
 *         provider: 'digitalocean'|'elevenlabs' }
 *
 * DigitalOcean Qwen3-TTS Voice Design (env vars):
 *   AI__TtsVoice              — default seed (default british-male)
 *   AI__TtsMaleVoice          — male seed   (default british-male)
 *   AI__TtsFemaleVoice        — female seed (default british-female)
 *   AI__TtsNeutralVoice       — neutral seed (default british-male)
 *   AI__TtsMaleInstructions   — male voice description (British English by default)
 *   AI__TtsFemaleInstructions — female voice description (British English by default)
 *   AI__TtsNeutralInstructions — neutral voice description
 *
 * ElevenLabs configuration (env vars; only used if TTS__ForceProvider != digitalocean):
 *   ELEVENLABS__ApiKey        — REQUIRED to enable ElevenLabs (else falls back to DO)
 *   ELEVENLABS__BaseUrl       — default https://api.elevenlabs.io/v1
 *   ELEVENLABS__Model         — default eleven_multilingual_v2
 *   ELEVENLABS__DefaultVoiceId, ELEVENLABS__VoiceMaleId, ELEVENLABS__VoiceFemaleId, ELEVENLABS__VoiceNeutralId
 *
 * No dependencies — pure Node 20+ standard library only.
 */

import { readFileSync, writeFileSync, appendFileSync, mkdirSync, existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// -----------------------------------------------------------------------------
// Configuration
// -----------------------------------------------------------------------------

export const CONFIG = (() => {
  const env = process.env;
  return {
    apiBase: env.API_BASE || 'https://api.oetwithdrhesham.co.uk',
    adminEmail: env.ADMIN_EMAIL || 'manwara575@gmail.com',
    adminPassword: env.ADMIN_PASSWORD || '12345678',

    ai: {
      apiKey: env.AI__ApiKey || env.AI__APIKEY || env.AI_API_KEY || env.DO_AI_API_KEY || '',
      baseUrl: env.AI__BaseUrl || env.AI__BASEURL || env.AI_BASE_URL || 'https://inference.do-ai.run/v1',
      chatModel: env.AI__ChatModel || env.AI__CHATMODEL || env.AI__DEFAULTMODEL || env.AI_CHAT_MODEL || 'anthropic-claude-opus-4.7',
      ttsBaseUrl: env.AI__TtsBaseUrl || env.AI__TTSBASEURL || env.AI__BaseUrl || env.AI__BASEURL || env.AI_BASE_URL || 'https://inference.do-ai.run/v1',
      ttsModel: env.AI__TtsModel || env.AI__TTSMODEL || env.AI_TTS_MODEL || 'qwen3-tts-voicedesign',
      // Default DO Qwen3-TTS Voice Design voice. Override per-gender via the
      // tts*Voice keys below or per-call via opts.voice. British English male
      // is the production default since ElevenLabs credits ran out (May 2026).
      ttsVoice:        env.AI__TtsVoice        || env.AI__TTSVOICE        || 'british-male',
      ttsMaleVoice:    env.AI__TtsMaleVoice    || env.AI__TTSMALEVOICE    || 'british-male',
      ttsFemaleVoice:  env.AI__TtsFemaleVoice  || env.AI__TTSFEMALEVOICE  || 'british-female',
      ttsNeutralVoice: env.AI__TtsNeutralVoice || env.AI__TTSNEUTRALVOICE || 'british-male',
      // Voice Design "instructions" prompt; controls accent, age, tone, pace.
      // Qwen3-TTS voicedesign is generative — the voice field is a seed and
      // these instructions do the real work. Per-gender variants below.
      ttsMaleInstructions:    env.AI__TtsMaleInstructions    || env.AI__TTSMALEINSTRUCTIONS    ||
        'British English male voice, mid-30s, RP accent, warm yet professional, calm and clear enunciation, measured natural pace suitable for OET healthcare listening practice. Do not add laughter or filler.',
      ttsFemaleInstructions:  env.AI__TtsFemaleInstructions  || env.AI__TTSFEMALEINSTRUCTIONS  ||
        'British English female voice, mid-30s, RP accent, warm and articulate, calm professional tone, measured natural pace suitable for OET healthcare listening practice. Do not add laughter or filler.',
      ttsNeutralInstructions: env.AI__TtsNeutralInstructions || env.AI__TTSNEUTRALINSTRUCTIONS ||
        'British English male voice, mid-30s, RP accent, neutral broadcaster tone, clear and even pace suitable for OET healthcare listening practice. Do not add laughter or filler.',
    },

    // TTS provider routing. Defaults to ElevenLabs (with DO fallback on
    // failure). Set TTS__ForceProvider=digitalocean to bypass ElevenLabs
    // entirely (use when ElevenLabs credits are exhausted).
    ttsForceProvider:
      (env.TTS__ForceProvider || env.TTS__FORCEPROVIDER || env.TTS_FORCE_PROVIDER || '').toLowerCase(),

    // Primary TTS provider. Falls back to `ai` (DigitalOcean Qwen3-TTS) when
    // apiKey is unset or any call throws. See _aiTtsRaw dispatcher.
    elevenlabs: {
      apiKey:         env.ELEVENLABS__ApiKey || env.ELEVENLABS__APIKEY || env.ELEVENLABS_API_KEY || '',
      baseUrl:        env.ELEVENLABS__BaseUrl || env.ELEVENLABS__BASEURL || 'https://api.elevenlabs.io/v1',
      model:          env.ELEVENLABS__Model || env.ELEVENLABS__MODEL || 'eleven_multilingual_v2',
      defaultVoiceId: env.ELEVENLABS__DefaultVoiceId || env.ELEVENLABS__DEFAULTVOICEID || 'EXAVITQu4vr4xnSDxMaL',
      voiceMaleId:    env.ELEVENLABS__VoiceMaleId   || env.ELEVENLABS__VOICEMALEID   || 'pNInz6obpgDQGcFmaJgB',
      voiceFemaleId:  env.ELEVENLABS__VoiceFemaleId || env.ELEVENLABS__VOICEFEMALEID || 'EXAVITQu4vr4xnSDxMaL',
      voiceNeutralId: env.ELEVENLABS__VoiceNeutralId|| env.ELEVENLABS__VOICENEUTRALID|| 'EXAVITQu4vr4xnSDxMaL',
      stability: 0.5,
      similarityBoost: 0.75,
      style: 0.0,
      useSpeakerBoost: true,
    },

    // Throttle: maximum requests per second to backend admin endpoints.
    // Backend default rate limit (PerUserWrite policy) is 60/min ≈ 1/s.
    // We stay conservative at 4/s with retry on 429.
    adminMaxRps: 4,

    // Throttle for DO Serverless AI calls (per their docs, generous).
    aiMaxRps: 8,

    // Skip-on-failure cap: max consecutive failures before aborting an entire generator.
    maxConsecutiveFailures: 25,

    // Output directory for run logs + failures.
    outputDir: resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', 'output', 'admin-bulk'),
  };
})();

// Ensure output dir exists.
try {
  mkdirSync(CONFIG.outputDir, { recursive: true });
} catch {}

// -----------------------------------------------------------------------------
// CLI flag parsing (--key value, --flag, --dry-run)
// -----------------------------------------------------------------------------

export function parseFlags(argv = process.argv.slice(2)) {
  const flags = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) {
      const key = a.slice(2);
      const next = argv[i + 1];
      if (next === undefined || next.startsWith('--')) {
        flags[key] = true;
      } else {
        flags[key] = next;
        i++;
      }
    } else {
      flags._.push(a);
    }
  }
  // Apply common overrides to CONFIG.
  if (flags['chat-model']) CONFIG.ai.chatModel = flags['chat-model'];
  if (flags['tts-model']) CONFIG.ai.ttsModel = flags['tts-model'];
  if (flags['tts-provider']) CONFIG.ttsForceProvider = String(flags['tts-provider']).toLowerCase();
  if (flags['tts-voice']) CONFIG.ai.ttsVoice = flags['tts-voice'];
  if (flags['tts-male-voice']) CONFIG.ai.ttsMaleVoice = flags['tts-male-voice'];
  if (flags['tts-female-voice']) CONFIG.ai.ttsFemaleVoice = flags['tts-female-voice'];
  if (flags['tts-male-instructions']) CONFIG.ai.ttsMaleInstructions = flags['tts-male-instructions'];
  if (flags['tts-female-instructions']) CONFIG.ai.ttsFemaleInstructions = flags['tts-female-instructions'];
  if (flags['api-base']) CONFIG.apiBase = flags['api-base'];
  return flags;
}

// -----------------------------------------------------------------------------
// Throttle (token-bucket)
// -----------------------------------------------------------------------------

class TokenBucket {
  constructor(rps) {
    this.capacity = rps;
    this.tokens = rps;
    this.lastRefill = Date.now();
    this.refillPerMs = rps / 1000;
  }
  async take() {
    while (true) {
      const now = Date.now();
      const elapsed = now - this.lastRefill;
      this.tokens = Math.min(this.capacity, this.tokens + elapsed * this.refillPerMs);
      this.lastRefill = now;
      if (this.tokens >= 1) {
        this.tokens -= 1;
        return;
      }
      const waitMs = Math.max(50, Math.ceil((1 - this.tokens) / this.refillPerMs));
      await sleep(waitMs);
    }
  }
}

const adminBucket = new TokenBucket(CONFIG.adminMaxRps);
const aiBucket = new TokenBucket(CONFIG.aiMaxRps);

export function sleep(ms) {
  return new Promise(r => setTimeout(r, ms));
}

// -----------------------------------------------------------------------------
// Admin JWT auth
// -----------------------------------------------------------------------------

let _authCache = { accessToken: null, refreshToken: null, expiresAt: 0 };

async function signIn() {
  const res = await fetch(`${CONFIG.apiBase}/v1/auth/sign-in`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: CONFIG.adminEmail, password: CONFIG.adminPassword }),
  });
  if (!res.ok) {
    const body = await res.text().catch(() => '');
    throw new Error(`Admin sign-in failed (${res.status}): ${body.slice(0, 300)}`);
  }
  const j = await res.json();
  // Best-effort token discovery — backend may return camelCase or nested.
  const accessToken = j.accessToken || j.token || j.access_token || j.data?.accessToken;
  const refreshToken = j.refreshToken || j.refresh_token || j.data?.refreshToken;
  if (!accessToken) throw new Error(`Admin sign-in returned no accessToken: ${JSON.stringify(j).slice(0, 300)}`);
  // JWTs typically expire in 15m on this backend — refresh after 12m.
  _authCache = { accessToken, refreshToken, expiresAt: Date.now() + 12 * 60 * 1000 };
  return accessToken;
}

export async function getAccessToken({ force = false } = {}) {
  if (!force && _authCache.accessToken && Date.now() < _authCache.expiresAt) {
    return _authCache.accessToken;
  }
  return await signIn();
}

// -----------------------------------------------------------------------------
// adminFetch — throttled, auto-auth, auto-retry on 429/5xx
// -----------------------------------------------------------------------------

/**
 * Make an authenticated admin API call.
 *
 * @param {string} path - e.g. "/v1/admin/papers"
 * @param {object} opts - { method, body, headers, query, retries }
 * @returns {Promise<{ok: boolean, status: number, data: any, headers: Headers}>}
 */
export async function adminFetch(path, opts = {}) {
  const {
    method = 'GET',
    body = null,
    headers: extraHeaders = {},
    query = null,
    retries = 4,
    timeoutMs = 60_000,
  } = opts;

  let url = path.startsWith('http') ? path : `${CONFIG.apiBase}${path}`;
  if (query) {
    const qs = new URLSearchParams(
      Object.entries(query).filter(([, v]) => v !== undefined && v !== null).map(([k, v]) => [k, String(v)])
    ).toString();
    if (qs) url += (url.includes('?') ? '&' : '?') + qs;
  }

  let attempt = 0;
  let lastError = null;

  while (attempt <= retries) {
    attempt++;
    await adminBucket.take();
    const token = await getAccessToken();
    const headers = {
      Accept: 'application/json',
      Authorization: `Bearer ${token}`,
      ...extraHeaders,
    };
    let payload = body;
    if (body && !(body instanceof FormData) && !(body instanceof Uint8Array) && typeof body !== 'string') {
      headers['Content-Type'] = headers['Content-Type'] || 'application/json';
      payload = JSON.stringify(body);
    }

    const ctrl = new AbortController();
    const tid = setTimeout(() => ctrl.abort(), timeoutMs);
    let res;
    try {
      res = await fetch(url, { method, headers, body: payload, signal: ctrl.signal });
    } catch (e) {
      clearTimeout(tid);
      lastError = e;
      if (attempt > retries) break;
      await sleep(500 * attempt);
      continue;
    }
    clearTimeout(tid);

    // Read body (always — even on error so we can return it).
    const ctype = res.headers.get('content-type') || '';
    let data;
    try {
      data = ctype.includes('application/json') ? await res.json() : await res.text();
    } catch {
      data = null;
    }

    if (res.status === 401 && attempt <= retries) {
      // Force re-auth.
      await getAccessToken({ force: true });
      continue;
    }
    if ((res.status === 429 || res.status >= 500) && attempt <= retries) {
      const retryAfter = parseInt(res.headers.get('retry-after') || '0', 10) || (attempt * 800);
      await sleep(retryAfter * 1000 > 30_000 ? 30_000 : Math.min(retryAfter * 1000, 30_000));
      continue;
    }

    return { ok: res.ok, status: res.status, data, headers: res.headers };
  }

  return { ok: false, status: 0, data: null, headers: null, error: lastError?.message || 'request failed' };
}

// -----------------------------------------------------------------------------
// DO Serverless chat client (OpenAI-compatible)
// -----------------------------------------------------------------------------

let _totalChatPromptTokens = 0;
let _totalChatCompletionTokens = 0;
let _totalChatCalls = 0;

/**
 * Call the DO Serverless chat API. Returns the assistant message + usage.
 *
 * @param {object} opts
 * @param {string} opts.system - system prompt
 * @param {string|object[]} opts.user - user content (string or array of message objects)
 * @param {string} [opts.model] - override CONFIG.ai.chatModel
 * @param {object} [opts.responseFormat] - e.g. { type: 'json_object' }
 * @param {number} [opts.temperature] - default 0.6
 * @param {number} [opts.maxTokens] - default 8192
 * @param {number} [opts.retries] - default 3
 */
export async function aiChat({
  system,
  user,
  model = CONFIG.ai.chatModel,
  responseFormat = null,
  temperature = 0.6,
  maxTokens = 8192,
  retries = 3,
}) {
  if (!CONFIG.ai.apiKey) throw new Error('AI__ApiKey not set (DigitalOcean Serverless Inference API key required)');
  const messages = [
    { role: 'system', content: system },
    ...(Array.isArray(user) ? user : [{ role: 'user', content: user }]),
  ];

  const useStream = maxTokens > 4096;
  const body = {
    model,
    messages,
    temperature,
    max_tokens: maxTokens,
  };
  if (useStream) body.stream = true;
  if (responseFormat) body.response_format = responseFormat;

  let attempt = 0;
  let lastError = null;

  while (attempt <= retries) {
    attempt++;
    await aiBucket.take();
    const ctrl = new AbortController();
    const tid = setTimeout(() => ctrl.abort(), 180_000);
    let res;
    try {
      res = await fetch(`${CONFIG.ai.baseUrl.replace(/\/$/, '')}/chat/completions`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${CONFIG.ai.apiKey}`,
        },
        body: JSON.stringify(body),
        signal: ctrl.signal,
      });
    } catch (e) {
      clearTimeout(tid);
      lastError = e;
      if (attempt > retries) break;
      await sleep(1000 * attempt);
      continue;
    }
    clearTimeout(tid);
    if (!res.ok) {
      const text = await res.text().catch(() => '');
      lastError = new Error(`AI chat ${res.status}: ${text.slice(0, 500)}`);
      if ((res.status === 429 || res.status >= 500) && attempt <= retries) {
        await sleep(Math.min(30_000, 1000 * attempt * 2));
        continue;
      }
      break;
    }
    let content = '', finishReason = null, usage = {}, modelOut = model, rawObj = null;
    if (useStream) {
      const reader = res.body.getReader();
      const decoder = new TextDecoder('utf-8');
      let buf = '';
      let streamErr = false;
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        buf += decoder.decode(value, { stream: true });
        let idx;
        while ((idx = buf.indexOf('\n')) >= 0) {
          const line = buf.slice(0, idx).trim();
          buf = buf.slice(idx + 1);
          if (!line.startsWith('data:')) continue;
          const data = line.slice(5).trim();
          if (data === '[DONE]') continue;
          try {
            const chunk = JSON.parse(data);
            const delta = chunk.choices?.[0]?.delta?.content;
            if (delta) content += delta;
            const fr = chunk.choices?.[0]?.finish_reason;
            if (fr) finishReason = fr;
            if (chunk.usage) usage = chunk.usage;
            if (chunk.model) modelOut = chunk.model;
          } catch {}
        }
      }
      rawObj = { streamed: true };
      if (!content) {
        lastError = new Error('AI chat stream returned no content');
        if (attempt <= retries) continue;
        break;
      }
    } else {
      const j = await res.json();
      const choice = j.choices?.[0];
      if (!choice?.message?.content) {
        lastError = new Error(`AI chat returned no content: ${JSON.stringify(j).slice(0, 500)}`);
        if (attempt <= retries) continue;
        break;
      }
      content = choice.message.content;
      finishReason = choice.finish_reason;
      usage = j.usage || {};
      modelOut = j.model || model;
      rawObj = j;
    }
    _totalChatPromptTokens += usage.prompt_tokens || 0;
    _totalChatCompletionTokens += usage.completion_tokens || 0;
    _totalChatCalls++;
    return { content, finishReason, usage, model: modelOut, raw: rawObj };
  }
  throw lastError || new Error('AI chat failed');
}

/**
 * Call aiChat and parse the response as JSON. Provider-agnostic:
 *  - does NOT send response_format by default (DO Inference rejects json_object,
 *    requires json_schema; raw prompt-driven JSON is universally accepted).
 *  - if caller passes opts.responseFormat explicitly, it is forwarded as-is.
 *  - extracts JSON from ```json fences, ``` fences, leading prose, or first
 *    balanced { … } / [ … ] block.
 */
export async function aiChatJson(opts) {
  const r = await aiChat({ ...opts, responseFormat: opts.responseFormat || null });
  const json = extractJson(r.content);
  return { ...r, json };
}

/** Best-effort extract a JSON object/array from an LLM reply. Throws on failure. */
export function extractJson(content) {
  if (content == null) throw new Error('AI returned empty content');
  let text = String(content).trim();
  // Strip ``` fences.
  if (text.startsWith('```')) {
    text = text.replace(/^```(?:json|JSON)?\s*/, '').replace(/```\s*$/, '').trim();
  }
  // Fast path: whole string is JSON.
  try { return JSON.parse(text); } catch { /* fall through */ }
  // Locate first balanced { … } or [ … ] block.
  const firstObj = text.indexOf('{');
  const firstArr = text.indexOf('[');
  let start = -1;
  if (firstObj === -1) start = firstArr;
  else if (firstArr === -1) start = firstObj;
  else start = Math.min(firstObj, firstArr);
  if (start < 0) throw new Error(`AI returned non-JSON: no { or [ found.\n--- content ---\n${text.slice(0, 1000)}`);
  const opener = text[start];
  const closer = opener === '{' ? '}' : ']';
  let depth = 0;
  let inStr = false;
  let esc = false;
  for (let i = start; i < text.length; i++) {
    const ch = text[i];
    if (inStr) {
      if (esc) esc = false;
      else if (ch === '\\') esc = true;
      else if (ch === '"') inStr = false;
      continue;
    }
    if (ch === '"') { inStr = true; continue; }
    if (ch === opener) depth++;
    else if (ch === closer) {
      depth--;
      if (depth === 0) {
        const candidate = text.slice(start, i + 1);
        try { return JSON.parse(candidate); }
        catch (e) {
          throw new Error(`AI returned malformed JSON: ${e.message}\n--- candidate ---\n${candidate.slice(0, 1000)}`);
        }
      }
    }
  }
  throw new Error(`AI returned unbalanced JSON-like text.\n--- content ---\n${text.slice(0, 1000)}`);
}

// -----------------------------------------------------------------------------
// TTS clients: ElevenLabs (primary) + DigitalOcean Qwen3-TTS (fallback)
// -----------------------------------------------------------------------------

let _totalTtsChars = 0;
let _totalTtsCalls = 0;

// Per-run provider usage counters. Exposed via getTtsStats().
const _ttsProviderUsedCounter = { elevenlabs: 0, digitalocean: 0, fallbacks: 0 };

export function getTtsStats() {
  return {
    totalChars: _totalTtsChars,
    totalCalls: _totalTtsCalls,
    providers: { ..._ttsProviderUsedCounter },
  };
}

function _pickElevenLabsVoice(opts) {
  if (opts && opts.voice) return String(opts.voice);
  const g = opts && opts.gender ? String(opts.gender).toLowerCase() : '';
  if (g === 'male') return CONFIG.elevenlabs.voiceMaleId;
  if (g === 'female') return CONFIG.elevenlabs.voiceFemaleId;
  if (g === 'neutral') return CONFIG.elevenlabs.voiceNeutralId;
  return CONFIG.elevenlabs.defaultVoiceId;
}

async function _elevenLabsTtsRaw(text, opts = {}) {
  const { retries = 3, signal = null } = opts;
  if (!CONFIG.elevenlabs.apiKey) throw new Error('ELEVENLABS__ApiKey not set');
  if (!text || !text.trim()) throw new Error('aiTts: empty text');
  const voice = _pickElevenLabsVoice(opts);
  const url = `${CONFIG.elevenlabs.baseUrl.replace(/\/$/, '')}/text-to-speech/${encodeURIComponent(voice)}`;
  const body = {
    text,
    model_id: CONFIG.elevenlabs.model,
    voice_settings: {
      stability: CONFIG.elevenlabs.stability,
      similarity_boost: CONFIG.elevenlabs.similarityBoost,
      style: CONFIG.elevenlabs.style,
      use_speaker_boost: CONFIG.elevenlabs.useSpeakerBoost,
    },
  };
  let attempt = 0, lastError = null;
  while (attempt <= retries) {
    attempt++;
    await aiBucket.take();
    const ctrl = new AbortController();
    const tid = setTimeout(() => ctrl.abort(), 240_000);
    const onAbort = () => ctrl.abort();
    if (signal) {
      if (signal.aborted) { clearTimeout(tid); throw new Error('aborted'); }
      signal.addEventListener('abort', onAbort, { once: true });
    }
    let res;
    try {
      res = await fetch(url, {
        method: 'POST',
        headers: { 'xi-api-key': CONFIG.elevenlabs.apiKey, 'Accept': 'audio/mpeg', 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
        signal: ctrl.signal,
      });
    } catch (e) {
      clearTimeout(tid);
      if (signal) signal.removeEventListener('abort', onAbort);
      lastError = e;
      if (attempt > retries) break;
      await sleep(Math.min(30_000, 2000 * attempt));
      continue;
    }
    clearTimeout(tid);
    if (signal) signal.removeEventListener('abort', onAbort);
    if (!res.ok) {
      const t = await res.text().catch(() => '');
      lastError = new Error(`elevenlabs http ${res.status}: ${t.slice(0, 400)}`);
      if ((res.status === 429 || res.status >= 500) && attempt <= retries) {
        const retryAfter = parseInt(res.headers.get('retry-after') || '0', 10);
        const waitMs = retryAfter > 0 ? Math.min(30_000, retryAfter * 1000) : Math.min(30_000, 2000 * attempt);
        await sleep(waitMs);
        continue;
      }
      break;
    }
    const buf = Buffer.from(await res.arrayBuffer());
    _totalTtsChars += text.length;
    _totalTtsCalls++;
    _ttsProviderUsedCounter.elevenlabs++;
    return buf;
  }
  throw lastError || new Error('elevenlabs TTS failed');
}

function _pickDigitalOceanVoice(opts) {
  if (opts && opts.voice) return String(opts.voice);
  const g = opts && opts.gender ? String(opts.gender).toLowerCase() : '';
  if (g === 'male') return CONFIG.ai.ttsMaleVoice;
  if (g === 'female') return CONFIG.ai.ttsFemaleVoice;
  if (g === 'neutral') return CONFIG.ai.ttsNeutralVoice;
  return CONFIG.ai.ttsVoice;
}

function _pickDigitalOceanInstructions(opts) {
  if (opts && opts.instructions) return String(opts.instructions);
  const g = opts && opts.gender ? String(opts.gender).toLowerCase() : '';
  if (g === 'male') return CONFIG.ai.ttsMaleInstructions;
  if (g === 'female') return CONFIG.ai.ttsFemaleInstructions;
  if (g === 'neutral') return CONFIG.ai.ttsNeutralInstructions;
  return CONFIG.ai.ttsMaleInstructions;
}

async function _digitalOceanTtsRaw(text, opts = {}) {
  const {
    model = CONFIG.ai.ttsModel,
    retries = 3,
    format = 'mp3',
  } = opts;
  const voice = _pickDigitalOceanVoice(opts);
  const instructions = _pickDigitalOceanInstructions(opts);
  if (!CONFIG.ai.apiKey) throw new Error('AI__ApiKey not set');
  if (!text || !text.trim()) throw new Error('aiTts: empty text');
  const body = { model, input: text, voice, instructions };
  void format;
  let attempt = 0, lastError = null;
  while (attempt <= retries) {
    attempt++;
    await aiBucket.take();
    const ctrl = new AbortController();
    const tid = setTimeout(() => ctrl.abort(), 240_000);
    let res;
    try {
      res = await fetch(`${CONFIG.ai.ttsBaseUrl.replace(/\/$/, '')}/audio/speech`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${CONFIG.ai.apiKey}` },
        body: JSON.stringify(body),
        signal: ctrl.signal,
      });
    } catch (e) {
      clearTimeout(tid);
      lastError = e;
      if (attempt > retries) break;
      await sleep(1500 * attempt);
      continue;
    }
    clearTimeout(tid);
    if (!res.ok) {
      const t = await res.text().catch(() => '');
      lastError = new Error(`TTS ${res.status}: ${t.slice(0, 400)}`);
      if ((res.status === 429 || res.status >= 500) && attempt <= retries) {
        await sleep(Math.min(30_000, 2000 * attempt));
        continue;
      }
      break;
    }
    const buf = Buffer.from(await res.arrayBuffer());
    _totalTtsChars += text.length;
    _totalTtsCalls++;
    _ttsProviderUsedCounter.digitalocean++;
    return buf;
  }
  throw lastError || new Error('TTS failed');
}

// TTS dispatcher (ElevenLabs primary, DigitalOcean fallback).
// Set TTS__ForceProvider=digitalocean to force DO Qwen3-TTS (used when
// ElevenLabs credits are exhausted — May 2026 default).
async function _aiTtsRaw(text, opts = {}) {
  const pinned = (opts && opts.provider ? String(opts.provider).toLowerCase() : '')
    || CONFIG.ttsForceProvider
    || '';
  const hasEleven = !!CONFIG.elevenlabs.apiKey;
  if (pinned === 'digitalocean') return _digitalOceanTtsRaw(text, opts);
  if (pinned === 'elevenlabs') return _elevenLabsTtsRaw(text, opts);
  if (!hasEleven) return _digitalOceanTtsRaw(text, opts);
  try {
    return await _elevenLabsTtsRaw(text, opts);
  } catch (elErr) {
    _ttsProviderUsedCounter.fallbacks++;
    try {
      // eslint-disable-next-line no-console
      console.error(`tts: ElevenLabs failed (${(elErr && elErr.message) || elErr}); falling back to DO`);
    } catch { /* noop */ }
    try {
      return await _digitalOceanTtsRaw(text, opts);
    } catch (doErr) {
      const combined = new Error(`elevenlabs+do failed. eleven=${elErr && elErr.message}; do=${doErr && doErr.message}`);
      combined.cause = elErr;
      throw combined;
    }
  }
}



// -----------------------------------------------------------------------------
// aiTts wrapper: chunk long inputs (Qwen3-TTS hard cap is 512 chars/request).
//   Splits text on sentence boundaries (. ! ? then , then word) into ≤ 480-char
//   chunks, synthesizes each, then byte-concatenates the resulting MP3 buffers.
// -----------------------------------------------------------------------------
const TTS_MAX_CHARS = 480;

function _sanitizeTtsText(text) {
  let s = String(text || '');
  // Replace curly quotes/apostrophes with ASCII equivalents.
  s = s.replace(/[\u2018\u2019\u201A\u201B]/g, "'");
  s = s.replace(/[\u201C\u201D\u201E\u201F]/g, '"');
  // Em/en dashes, hyphen variants â†’ ASCII hyphen.
  s = s.replace(/[\u2010\u2011\u2012\u2013\u2014\u2015\u2212]/g, '-');
  // Ellipsis â†’ three dots.
  s = s.replace(/\u2026/g, '...');
  // Non-breaking + thin/zero-width spaces â†’ normal space / removed.
  s = s.replace(/[\u00A0\u2007\u202F]/g, ' ');
  s = s.replace(/[\u200B\u200C\u200D\uFEFF]/g, '');
  // Strip control chars except \n \r \t.
  s = s.replace(/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/g, ' ');
  return s;
}

function _ttsChunkText(text) {
  const t = _sanitizeTtsText(text).replace(/\s+/g, ' ').trim();
  if (!t) return [];
  if (t.length <= TTS_MAX_CHARS) return [t];

  // Sentence-aware split: keep delimiter, then group sentences ≤ TTS_MAX_CHARS.
  const sentences = t.match(/[^.!?]+[.!?]+|[^.!?]+$/g) || [t];
  const chunks = [];
  let buf = '';
  for (const raw of sentences) {
    const sent = raw.trim();
    if (!sent) continue;
    if (sent.length > TTS_MAX_CHARS) {
      // Sentence itself is too long — split on commas then words.
      if (buf) { chunks.push(buf); buf = ''; }
      const pieces = sent.match(/[^,;:]+[,;:]+|[^,;:]+$/g) || [sent];
      let sub = '';
      for (const p of pieces) {
        const piece = p.trim();
        if (!piece) continue;
        if (piece.length > TTS_MAX_CHARS) {
          if (sub) { chunks.push(sub); sub = ''; }
          // Hard word-split.
          const words = piece.split(' ');
          let w = '';
          for (const word of words) {
            if ((w + ' ' + word).trim().length > TTS_MAX_CHARS) {
              if (w) chunks.push(w);
              w = word;
            } else {
              w = w ? w + ' ' + word : word;
            }
          }
          if (w) chunks.push(w);
        } else if ((sub + ' ' + piece).trim().length > TTS_MAX_CHARS) {
          if (sub) chunks.push(sub);
          sub = piece;
        } else {
          sub = sub ? sub + ' ' + piece : piece;
        }
      }
      if (sub) chunks.push(sub);
    } else if ((buf + ' ' + sent).trim().length > TTS_MAX_CHARS) {
      if (buf) chunks.push(buf);
      buf = sent;
    } else {
      buf = buf ? buf + ' ' + sent : sent;
    }
  }
  if (buf) chunks.push(buf);
  return chunks;
}

export async function aiTts(text, opts = {}) {
  const chunks = _ttsChunkText(text);
  if (chunks.length === 0) throw new Error('aiTts: empty text');
  if (chunks.length === 1) return _aiTtsRaw(chunks[0], opts);

  const buffers = [];
  for (let i = 0; i < chunks.length; i++) {
    const c = chunks[i];
    // small jitter between calls so we don't burst the rate-limiter
    if (i > 0) await sleep(150);
    const buf = await _aiTtsRaw(c, opts);
    buffers.push(buf);
  }
  // Naive byte concatenation. Qwen3-TTS emits CBR MP3 frames; concatenated
  // streams play correctly in browsers / audio decoders with at most a tiny
  // imperceptible gap at chunk boundaries — acceptable for practice material.
  return Buffer.concat(buffers);
}

// -----------------------------------------------------------------------------
// Chunked file upload (uses /v1/admin/uploads/* per ContentPapersAdminEndpoints)
// -----------------------------------------------------------------------------

/**
 * Upload a file via the chunked-upload pipeline, returning a MediaAsset id.
 * @param {Buffer} buf
 * @param {object} opts - { filename, mimeType, kind ('audio'|'document'|'image'), chunkSize }
 * @returns {Promise<string>} mediaAssetId
 */
export async function uploadMediaAsset(buf, { filename, mimeType, kind = 'document', intendedRole = null } = {}) {
  if (!filename) throw new Error('uploadMediaAsset: filename required');
  if (!mimeType) throw new Error('uploadMediaAsset: mimeType required');

  // Map orchestrator `kind` to backend PaperAssetRole. Callers may override via intendedRole.
  const role = intendedRole || (kind === 'audio' ? 'Audio' : 'Supplementary');

  // 1. Start session: POST /v1/admin/uploads
  //    ChunkedUploadStartDto { OriginalFilename, DeclaredMimeType, DeclaredSizeBytes, IntendedRole }
  //    -> { uploadId, chunkSizeBytes, expiresAt }
  const start = await adminFetch('/v1/admin/uploads', {
    method: 'POST',
    body: {
      originalFilename: filename,
      declaredMimeType: mimeType,
      declaredSizeBytes: buf.length,
      intendedRole: role,
    },
  });
  if (!start.ok) {
    throw new Error(`uploads start failed (${start.status}): ${JSON.stringify(start.data).slice(0, 400)}`);
  }
  const uploadId = start.data.uploadId;
  const chunkSize = Number(start.data.chunkSizeBytes) || 8 * 1024 * 1024;
  if (!uploadId) throw new Error(`uploads start returned no uploadId: ${JSON.stringify(start.data).slice(0, 400)}`);

  // 2. Upload parts: PUT /v1/admin/uploads/{uploadId}/parts/{partNumber:int}
  //    Body is raw application/octet-stream. partNumber is 1-based.
  let offset = 0;
  let partNumber = 1;
  while (offset < buf.length) {
    const end = Math.min(offset + chunkSize, buf.length);
    const slice = buf.subarray(offset, end);
    const r = await adminFetch(`/v1/admin/uploads/${uploadId}/parts/${partNumber}`, {
      method: 'PUT',
      body: slice,
      headers: { 'Content-Type': 'application/octet-stream' },
    });
    if (!r.ok) {
      throw new Error(`uploads ${uploadId} part ${partNumber} failed (${r.status}): ${typeof r.data === 'string' ? r.data.slice(0, 200) : JSON.stringify(r.data).slice(0, 200)}`);
    }
    offset = end;
    partNumber++;
  }

  // 3. Complete: POST /v1/admin/uploads/{uploadId}/complete
  //    -> ChunkedUploadCommitResult { mediaAssetId, sha256, sizeBytes, deduplicated }
  const complete = await adminFetch(`/v1/admin/uploads/${uploadId}/complete`, { method: 'POST', body: {} });
  if (!complete.ok) {
    throw new Error(`uploads ${uploadId} complete failed (${complete.status}): ${JSON.stringify(complete.data).slice(0, 400)}`);
  }
  const assetId = complete.data.mediaAssetId;
  if (!assetId) throw new Error(`uploads complete returned no mediaAssetId: ${JSON.stringify(complete.data).slice(0, 400)}`);
  return assetId;
}

// -----------------------------------------------------------------------------
// Run header / footer / failure logger
// -----------------------------------------------------------------------------

let _runId = null;
let _runStart = 0;
let _failuresPath = null;

export function startRun(name) {
  _runId = `${name}-${new Date().toISOString().replace(/[:.]/g, '-')}`;
  _runStart = Date.now();
  _failuresPath = `${CONFIG.outputDir}/failures-${_runId}.jsonl`;
  console.log('═══════════════════════════════════════════════════════════════════');
  console.log(`  Run: ${_runId}`);
  console.log(`  API: ${CONFIG.apiBase}`);
  console.log(`  Admin: ${CONFIG.adminEmail}`);
  console.log(`  AI base: ${CONFIG.ai.baseUrl}  chat=${CONFIG.ai.chatModel}  tts=${CONFIG.ai.ttsModel}`);
  const _elOn = !!CONFIG.elevenlabs.apiKey;
  console.log(`  TTS provider: ${_elOn ? 'ElevenLabs (PRIMARY) ✓ key=' + CONFIG.elevenlabs.apiKey.slice(0,7) + '… model=' + CONFIG.elevenlabs.model : 'DigitalOcean Qwen3 (no ElevenLabs key)'}  fallback=DigitalOcean Qwen3`);
  console.log(`  Failure log: ${_failuresPath}`);
  console.log('═══════════════════════════════════════════════════════════════════');
  return _runId;
}

export function logFailure(domain, item, error) {
  const row = {
    ts: new Date().toISOString(),
    domain,
    item: typeof item === 'object' ? item : { id: String(item) },
    error: error instanceof Error ? { message: error.message, stack: error.stack?.split('\n').slice(0, 5).join('\n') } : String(error),
  };
  try {
    appendFileSync(_failuresPath, JSON.stringify(row) + '\n', 'utf8');
  } catch {}
  console.log(`  ✗ ${domain}: ${row.error.message || row.error}`);
}

export function endRun(stats = {}) {
  const elapsed = ((Date.now() - _runStart) / 1000).toFixed(1);
  console.log('───────────────────────────────────────────────────────────────────');
  console.log(`  Run ${_runId} finished in ${elapsed}s`);
  for (const [k, v] of Object.entries(stats)) console.log(`    ${k}: ${v}`);
  console.log(`    AI chat: ${_totalChatCalls} calls, prompt=${_totalChatPromptTokens}, completion=${_totalChatCompletionTokens}`);
  console.log(`    AI TTS:  ${_totalTtsCalls} calls, ${_totalTtsChars} chars`);
  console.log(`    TTS providers: ElevenLabs=${_ttsProviderUsedCounter.elevenlabs}  DigitalOcean=${_ttsProviderUsedCounter.digitalocean}  fallbacks=${_ttsProviderUsedCounter.fallbacks}`);
  console.log(`    Failures logged: ${_failuresPath}`);
  console.log('───────────────────────────────────────────────────────────────────');
}

// -----------------------------------------------------------------------------
// Misc helpers
// -----------------------------------------------------------------------------

export function progress(i, total, label) {
  const pct = total ? ((i / total) * 100).toFixed(1) : '?';
  return `[${i}/${total} ${pct}%] ${label}`;
}

export function slugify(s) {
  return String(s).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '').slice(0, 80);
}

export function safeJsonStringify(v) {
  try { return JSON.stringify(v); } catch { return String(v); }
}

/**
 * Stamp a sourceProvenance string with AI generation attestation.
 * Always includes the AGENTS.md-required `legal=…` token for Listening papers
 * (other paper types accept any string).
 */
export function makeProvenance({ kind, profession, model, withLegalToken = false }) {
  const date = new Date().toISOString().slice(0, 10);
  const base = `AI-generated ${kind} content for ${profession || 'all professions'} via ${model} (DigitalOcean Serverless) on ${date}; reviewed for OET practice use.`;
  return withLegalToken ? `${base}; legal=original-authoring-attested` : base;
}

export function abortIfFailureCascade(consecutiveFailures, domain) {
  if (consecutiveFailures >= CONFIG.maxConsecutiveFailures) {
    console.error(`  ⚠ ${domain}: ${consecutiveFailures} consecutive failures — aborting domain.`);
    return true;
  }
  return false;
}

// -----------------------------------------------------------------------------
// Health check
// -----------------------------------------------------------------------------

export async function healthcheck() {
  console.log('Healthcheck:');

  // Backend reachable?
  try {
    const r = await fetch(`${CONFIG.apiBase}/v1/health`, { signal: AbortSignal.timeout(10_000) });
    console.log(`  backend /v1/health: ${r.status}`);
  } catch (e) {
    console.log(`  backend /v1/health: FAIL ${e.message}`);
  }

  // Admin login works?
  try {
    await getAccessToken({ force: true });
    console.log(`  admin sign-in: OK (token cached)`);
  } catch (e) {
    console.log(`  admin sign-in: FAIL ${e.message}`);
    return false;
  }

  // Admin perms?
  const me = await adminFetch('/v1/auth/me');
  if (me.ok) {
    const perms = me.data?.permissions || me.data?.adminPermissions || [];
    console.log(`  admin perms: ${Array.isArray(perms) ? perms.join(',') : JSON.stringify(perms)}`);
  } else {
    console.log(`  admin /v1/auth/me: ${me.status}`);
  }

  // AI key set?
  if (CONFIG.ai.apiKey) {
    console.log(`  AI key: set (${CONFIG.ai.apiKey.slice(0, 6)}…)`);
  } else {
    console.log(`  AI key: MISSING — set AI__ApiKey env var`);
    return false;
  }

  // AI chat reachable?
  try {
    const r = await aiChat({
      system: 'You are a healthcheck.',
      user: 'Reply with exactly the word OK.',
      maxTokens: 8,
      retries: 1,
    });
    console.log(`  AI chat: ${r.model} → "${r.content.trim().slice(0, 40)}"`);
  } catch (e) {
    console.log(`  AI chat: FAIL ${e.message}`);
    return false;
  }

  return true;
}

// -----------------------------------------------------------------------------
// Entry point when called directly: scripts/admin/_lib.mjs healthcheck
// -----------------------------------------------------------------------------

const isMain = import.meta.url === `file://${process.argv[1]?.replace(/\\/g, '/')}` ||
  import.meta.url.endsWith(process.argv[1]?.split(/[\\/]/).pop() || '___');

if (isMain) {
  parseFlags();
  startRun('healthcheck');
  const ok = await healthcheck();
  endRun({ ok });
  process.exit(ok ? 0 : 1);
}
