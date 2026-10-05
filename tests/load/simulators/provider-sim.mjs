#!/usr/bin/env node
// Provider simulator for the fleet load test: stands in for OpenAI GPT-Live, Gemini Live and LiveKit
// so the OET control plane can be driven at 100 AI sessions + 50 tutor rooms without a single paid
// provider call. Zero dependencies (node:http, node:crypto, global fetch); one process, one port.
//
// What it is, and is not: the OET API never carries provider MEDIA (the browser talks to the
// provider), so the load on the API is the control plane only: session creation, SDP exchange,
// ephemeral tokens, LiveKit room / egress calls and LiveKit webhooks. Those are what this simulates,
// with configurable latency, jitter and failure rate. REAL provider capacity is an owner-supplied
// assumption that this simulator cannot validate (the report says so).
//
// Routes (paths are what the API's configuration points at; see docs/ops/LOAD-TESTING.md):
//   OpenAI   POST /openai/v1/live/sessions               { session, transport:{type,sdp} } -> { session:{id}, transport:{sdp} }
//            POST /openai/v1/live/sessions/{id}/hangup
//            GET  /openai/v1/models/{model}               -> { id }
//   Gemini   POST /gemini/v1beta/auth_tokens              -> { name }
//            GET  /gemini/v1beta/models/{model}           -> { name: "models/<model>" }
//   LiveKit  POST /twirp/livekit.RoomService/CreateRoom   -> { sid, name, creation_time }  (+ scheduled webhooks)
//            POST /twirp/livekit.RoomService/DeleteRoom   -> {}                            (+ room_finished webhook)
//            POST /twirp/livekit.Egress/StartEgress       -> { egress_id, room_id, status }
//            POST /twirp/livekit.Egress/StopEgress        -> { egress_id, status }
//   Streaming POST /openai/v1/chat/completions, POST /anthropic/v1/messages  (stream:true -> SSE chunks)
//   Control  GET /sim/health   GET /sim/stats   POST /sim/config   POST /sim/emit

import { createHash, createHmac, randomUUID } from 'node:crypto';
import { createServer } from 'node:http';
import { pathToFileURL } from 'node:url';

const num = (value, fallback) => {
  if (value === undefined || value === null || value === '') return fallback;
  const n = Number(value);
  return Number.isFinite(n) ? n : fallback;
};

/** Configuration from the environment (all optional). */
export function makeConfig(env = {}) {
  return {
    port: num(env.SIM_PORT, 9100),
    latencyMs: num(env.SIM_LATENCY_MS, 150),
    jitterMs: num(env.SIM_JITTER_MS, 50),
    openAiLatencyMs: num(env.SIM_OPENAI_LATENCY_MS, null),
    geminiLatencyMs: num(env.SIM_GEMINI_LATENCY_MS, null),
    liveKitLatencyMs: num(env.SIM_LIVEKIT_LATENCY_MS, null),
    failRate: Math.min(1, Math.max(0, num(env.SIM_FAIL_RATE, 0))),
    failStatus: num(env.SIM_FAIL_STATUS, 503),
    streamChunks: Math.max(1, Math.floor(num(env.SIM_STREAM_CHUNKS, 12))),
    streamDelayMs: Math.max(0, num(env.SIM_STREAM_DELAY_MS, 40)),
    liveKitApiKey: env.SIM_LIVEKIT_API_KEY ?? 'load-sim-key',
    liveKitApiSecret: env.SIM_LIVEKIT_API_SECRET ?? 'load-sim-secret-load-sim-secret-0000',
    webhookUrl: env.SIM_WEBHOOK_URL ?? '',
    roomLifetimeS: num(env.SIM_ROOM_LIFETIME_S, 420),
  };
}

export const RUNTIME_KEYS = ['latencyMs', 'jitterMs', 'openAiLatencyMs', 'geminiLatencyMs', 'liveKitLatencyMs', 'failRate', 'failStatus',
  'streamChunks', 'streamDelayMs', 'roomLifetimeS'];

const NULLABLE_KEYS = ['openAiLatencyMs', 'geminiLatencyMs', 'liveKitLatencyMs'];

/** Merge a runtime change into the config; only known numeric keys, with sane bounds. */
export function applyRuntimeConfig(config, patch) {
  const source = patch !== null && typeof patch === 'object' && !Array.isArray(patch) ? patch : {};
  const applied = {};
  for (const key of RUNTIME_KEYS) {
    if (!(key in source)) continue;
    const raw = source[key];
    const numeric = typeof raw === 'number' || (typeof raw === 'string' && raw.trim() !== '');
    if (raw === null ? !NULLABLE_KEYS.includes(key) : !numeric) throw new RangeError(key + ' must be a non-negative number');
    const value = raw === null ? null : Number(raw);
    if (value !== null && (!Number.isFinite(value) || value < 0)) throw new RangeError(key + ' must be a non-negative number');
    if (key === 'failRate' && value > 1) throw new RangeError('failRate must be between 0 and 1');
    if (key === 'failStatus' && (!Number.isInteger(value) || value < 400 || value > 599)) throw new RangeError('failStatus must be an HTTP error status');
    config[key] = value;
    applied[key] = value;
  }
  return applied;
}

export function latencyFor(config, provider, random = Math.random) {
  const specific = { openai: config.openAiLatencyMs, gemini: config.geminiLatencyMs, livekit: config.liveKitLatencyMs }[provider];
  const base = specific === null || specific === undefined ? config.latencyMs : specific;
  return Math.max(0, Math.round(base + (random() * 2 - 1) * config.jitterMs));
}

const b64url = (input) => Buffer.from(input).toString('base64url');

/** LiveKit-style webhook signature: an HS256 JWT whose `sha256` claim is the base64 SHA-256 of the body. */
export function signWebhook(body, { apiKey, apiSecret, now = Math.floor(Date.now() / 1000) }) {
  const header = b64url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const claims = b64url(JSON.stringify({
    iss: apiKey, nbf: now - 5, exp: now + 300, sha256: createHash('sha256').update(body, 'utf8').digest('base64'),
  }));
  const signature = createHmac('sha256', apiSecret).update(`${header}.${claims}`).digest('base64url');
  return `${header}.${claims}.${signature}`;
}

export function buildWebhookEvent(event, room, extra = {}) {
  return { event, id: `EV_${randomUUID().replace(/-/g, '')}`, createdAt: Math.floor(Date.now() / 1000), room: { sid: room.sid, name: room.name }, ...extra };
}

/** The OpenAI GPT-Live session answer the API parses: session.id and transport.sdp. */
export function buildOpenAiAnswer() {
  return {
    session: { id: `sess_${randomUUID().replace(/-/g, '')}` },
    transport: { type: 'webrtc', sdp: 'v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=group:BUNDLE 0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\nc=IN IP4 0.0.0.0\r\na=rtpmap:111 opus/48000/2\r\na=setup:passive\r\n' },
  };
}

/** Server-sent-event chunks for a streamed completion; `dialect` is 'openai' or 'anthropic'. */
export function sseChunks(dialect, { chunks = 12, text = 'simulated' } = {}) {
  const out = [];
  if (dialect === 'anthropic') {
    out.push(`event: message_start\ndata: ${JSON.stringify({ type: 'message_start', message: { id: 'msg_sim', type: 'message', role: 'assistant', content: [], model: 'sim' } })}\n\n`);
    out.push(`event: content_block_start\ndata: ${JSON.stringify({ type: 'content_block_start', index: 0, content_block: { type: 'text', text: '' } })}\n\n`);
    for (let i = 0; i < chunks; i += 1) {
      out.push(`event: content_block_delta\ndata: ${JSON.stringify({ type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text: `${text} ` } })}\n\n`);
    }
    out.push(`event: content_block_stop\ndata: ${JSON.stringify({ type: 'content_block_stop', index: 0 })}\n\n`);
    out.push(`event: message_stop\ndata: ${JSON.stringify({ type: 'message_stop' })}\n\n`);
    return out;
  }
  for (let i = 0; i < chunks; i += 1) {
    out.push(`data: ${JSON.stringify({ id: 'chatcmpl-sim', object: 'chat.completion.chunk', choices: [{ index: 0, delta: { content: `${text} ` } }] })}\n\n`);
  }
  out.push(`data: ${JSON.stringify({ id: 'chatcmpl-sim', object: 'chat.completion.chunk', choices: [{ index: 0, delta: {}, finish_reason: 'stop' }] })}\n\n`);
  out.push('data: [DONE]\n\n');
  return out;
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function readJson(req, limit = 2 * 1024 * 1024) {
  const chunks = [];
  let size = 0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > limit) throw Object.assign(new Error('body too large'), { status: 413 });
    chunks.push(chunk);
  }
  const text = Buffer.concat(chunks).toString('utf8');
  if (text.trim() === '') return {};
  try {
    return JSON.parse(text);
  } catch (error) {
    throw Object.assign(new Error('invalid JSON'), { status: 400 });
  }
}

const sendJson = (res, status, body, headers = {}) => {
  const text = JSON.stringify(body);
  res.writeHead(status, { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(text), ...headers });
  res.end(text);
};

/**
 * Create the simulator. Returns { server, state, config, close }. `fetchImpl` is the webhook sender
 * (injectable for tests); timers are tracked so close() cancels them.
 */
export function createSimulator(config, { fetchImpl = globalThis.fetch } = {}) {
  const state = {
    startedAt: Date.now(),
    requests: {},
    inFlight: 0,
    maxInFlight: 0,
    injectedFailures: 0,
    rooms: new Map(),
    roomsCreated: 0,
    roomsDeleted: 0,
    webhooksSent: 0,
    webhooksFailed: 0,
  };
  const timers = new Set();
  const later = (ms, fn) => {
    const timer = setTimeout(() => { timers.delete(timer); fn(); }, ms);
    timers.add(timer);
    return timer;
  };

  async function emit(event, room, extra = {}) {
    if (!config.webhookUrl) return false;
    const body = JSON.stringify(buildWebhookEvent(event, room, extra));
    try {
      const response = await fetchImpl(config.webhookUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/webhook+json',
          Authorization: signWebhook(body, { apiKey: config.liveKitApiKey, apiSecret: config.liveKitApiSecret }),
        },
        body,
      });
      if (response.status >= 200 && response.status < 300) state.webhooksSent += 1;
      else state.webhooksFailed += 1;
      return response.status;
    } catch (error) {
      state.webhooksFailed += 1;
      return false;
    }
  }

  function scheduleRoomEvents(room) {
    const lifetimeMs = Math.max(1000, config.roomLifetimeS * 1000);
    room.timers = [
      later(200, () => emit('room_started', room)),
      later(500, () => emit('participant_joined', room, { participant: { identity: `learner:sim-${room.name}`, sid: `PA_${room.sid}` } })),
      later(800, () => emit('participant_joined', room, { participant: { identity: `tutor:sim-${room.name}`, sid: `PB_${room.sid}` } })),
      later(lifetimeMs, () => emit('egress_ended', room, {
        egressInfo: { egressId: room.egressId ?? `EG_${room.sid}`, fileResults: [{ location: `s3://load-sim/${room.name}.ogg`, duration: Math.floor(lifetimeMs * 1e6), size: 480000 }] },
      })),
      later(lifetimeMs + 500, () => emit('room_finished', room)),
    ];
  }

  async function handle(req, res) {
    const url = new URL(req.url, 'http://sim');
    const path = url.pathname.replace(/\/+$/, '') || '/';
    const route = `${req.method} ${path.replace(/\/(sess|EG|RM)_[^/]+/g, '/:id').replace(/\/models\/[^/]+/, '/models/:model')}`;
    state.requests[route] = (state.requests[route] ?? 0) + 1;

    if (path.startsWith('/sim/')) return control(req, res, path);

    // Latency and fault injection apply to every provider route.
    const provider = path.startsWith('/openai') ? 'openai' : path.startsWith('/gemini') ? 'gemini' : path.startsWith('/twirp') ? 'livekit' : 'other';
    await sleep(latencyFor(config, provider));
    if (config.failRate > 0 && Math.random() < config.failRate) {
      state.injectedFailures += 1;
      return sendJson(res, config.failStatus, { error: { message: 'simulated provider failure', type: 'simulated' } }, { 'Retry-After': '2' });
    }

    // ---- OpenAI GPT-Live
    if (req.method === 'POST' && path === '/openai/v1/live/sessions') {
      await readJson(req);
      return sendJson(res, 200, buildOpenAiAnswer());
    }
    if (req.method === 'POST' && /^\/openai\/v1\/live\/sessions\/[^/]+\/hangup$/.test(path)) return sendJson(res, 200, {});
    const openAiModel = /^\/openai\/v1\/models\/([^/]+)$/.exec(path);
    if (req.method === 'GET' && openAiModel) {
      const id = decodeURIComponent(openAiModel[1]);
      return sendJson(res, 200, { id, object: 'model', owned_by: 'load-sim' });
    }

    // ---- Gemini Live
    if (req.method === 'POST' && path === '/gemini/v1beta/auth_tokens') {
      await readJson(req);
      return sendJson(res, 200, { name: `auth_tokens/${randomUUID().replace(/-/g, '')}` });
    }
    const geminiModel = /^\/gemini\/v1beta\/models\/([^/]+)$/.exec(path);
    if (req.method === 'GET' && geminiModel) return sendJson(res, 200, { name: `models/${decodeURIComponent(geminiModel[1])}` });

    // ---- LiveKit Twirp
    if (req.method === 'POST' && path === '/twirp/livekit.RoomService/CreateRoom') {
      const body = await readJson(req);
      const name = typeof body.name === 'string' && body.name ? body.name : `room-${randomUUID()}`;
      const existing = state.rooms.get(name);
      const room = existing ?? { sid: `RM_${randomUUID().replace(/-/g, '').slice(0, 12)}`, name, created: Date.now() };
      if (!existing) {
        state.rooms.set(name, room);
        state.roomsCreated += 1;
        scheduleRoomEvents(room);
      }
      return sendJson(res, 200, { sid: room.sid, name: room.name, creation_time: Math.floor(room.created / 1000) });
    }
    if (req.method === 'POST' && path === '/twirp/livekit.RoomService/DeleteRoom') {
      const body = await readJson(req);
      const room = state.rooms.get(body.room);
      if (room) {
        for (const timer of room.timers ?? []) { clearTimeout(timer); timers.delete(timer); }
        state.rooms.delete(body.room);
        state.roomsDeleted += 1;
        later(200, () => emit('room_finished', room));
      }
      return sendJson(res, 200, {});
    }
    if (req.method === 'POST' && path === '/twirp/livekit.Egress/StartEgress') {
      const body = await readJson(req);
      const room = state.rooms.get(body.room_name);
      const egressId = `EG_${randomUUID().replace(/-/g, '').slice(0, 12)}`;
      if (room) room.egressId = egressId;
      return sendJson(res, 200, { egress_id: egressId, room_id: room?.sid ?? '', status: 'EGRESS_STARTING' });
    }
    if (req.method === 'POST' && path === '/twirp/livekit.Egress/StopEgress') {
      const body = await readJson(req);
      return sendJson(res, 200, { egress_id: body.egress_id ?? '', status: 'EGRESS_ENDING' });
    }

    // ---- streamed completions (any OpenAI / Anthropic compatible caller)
    if (req.method === 'POST' && (path === '/openai/v1/chat/completions' || path === '/anthropic/v1/messages')) {
      const body = await readJson(req);
      const dialect = path.startsWith('/anthropic') ? 'anthropic' : 'openai';
      if (body.stream === true) {
        res.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache', Connection: 'keep-alive' });
        for (const chunk of sseChunks(dialect, { chunks: config.streamChunks })) {
          if (res.destroyed) return undefined;
          res.write(chunk);
          await sleep(config.streamDelayMs);
        }
        return res.end();
      }
      return sendJson(res, 200, dialect === 'anthropic'
        ? { id: 'msg_sim', type: 'message', role: 'assistant', model: body.model ?? 'sim', content: [{ type: 'text', text: 'simulated' }], stop_reason: 'end_turn', usage: { input_tokens: 10, output_tokens: 5 } }
        : { id: 'chatcmpl-sim', object: 'chat.completion', model: body.model ?? 'sim', choices: [{ index: 0, message: { role: 'assistant', content: 'simulated' }, finish_reason: 'stop' }], usage: { prompt_tokens: 10, completion_tokens: 5, total_tokens: 15 } });
    }
    return sendJson(res, 404, { error: { message: `no simulator route for ${req.method} ${path}` } });
  }

  async function control(req, res, path) {
    if (path === '/sim/health') return sendJson(res, 200, { status: 'ok' });
    if (path === '/sim/stats') {
      return sendJson(res, 200, {
        uptimeS: Math.round((Date.now() - state.startedAt) / 1000),
        requests: state.requests,
        inFlight: state.inFlight,
        maxInFlight: state.maxInFlight,
        injectedFailures: state.injectedFailures,
        rooms: { created: state.roomsCreated, deleted: state.roomsDeleted, active: state.rooms.size },
        webhooks: { sent: state.webhooksSent, failed: state.webhooksFailed },
        config: { ...config, liveKitApiSecret: '[redacted]' },
      });
    }
    if (req.method === 'POST' && path === '/sim/config') {
      try {
        return sendJson(res, 200, { applied: applyRuntimeConfig(config, await readJson(req)) });
      } catch (error) {
        return sendJson(res, error.status ?? 400, { error: error.message });
      }
    }
    if (req.method === 'POST' && path === '/sim/emit') {
      const body = await readJson(req);
      const room = state.rooms.get(body.room) ?? { sid: 'RM_manual', name: String(body.room ?? '') };
      return sendJson(res, 200, { status: await emit(String(body.event ?? ''), room, body.extra ?? {}) });
    }
    return sendJson(res, 404, { error: 'unknown control route' });
  }

  const server = createServer((req, res) => {
    state.inFlight += 1;
    state.maxInFlight = Math.max(state.maxInFlight, state.inFlight);
    res.on('close', () => { state.inFlight -= 1; });
    handle(req, res).catch((error) => {
      if (!res.headersSent) sendJson(res, error.status ?? 500, { error: { message: error.message } });
      else res.end();
    });
  });

  return {
    server,
    state,
    config,
    emit,
    close: () => new Promise((resolve) => {
      for (const timer of timers) clearTimeout(timer);
      timers.clear();
      server.close(() => resolve());
      server.closeAllConnections?.();
    }),
  };
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const config = makeConfig(process.env);
  const sim = createSimulator(config);
  sim.server.listen(config.port, '0.0.0.0', () => {
    process.stdout.write(`provider-sim listening on :${config.port} (latency ${config.latencyMs}+/-${config.jitterMs} ms, fail ${config.failRate}, webhooks ${config.webhookUrl || 'off'})\n`);
  });
  const stop = () => sim.close().then(() => process.exit(0));
  process.on('SIGTERM', stop);
  process.on('SIGINT', stop);
}
