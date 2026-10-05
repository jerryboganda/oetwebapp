import assert from 'node:assert/strict';
import { createHash, createHmac } from 'node:crypto';
import test from 'node:test';
import {
  applyRuntimeConfig, buildOpenAiAnswer, buildWebhookEvent, createSimulator, latencyFor, makeConfig, signWebhook, sseChunks,
} from './provider-sim.mjs';

const FAST = { SIM_LATENCY_MS: '0', SIM_JITTER_MS: '0' };

async function withSim(env, fn, deps = {}) {
  const sim = createSimulator(makeConfig({ ...FAST, ...env }), deps);
  await new Promise((resolve) => sim.server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${sim.server.address().port}`;
  try {
    await fn(base, sim);
  } finally {
    await sim.close();
  }
}

const post = (url, body) => fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body ?? {}) });

test('makeConfig reads the environment with defaults', () => {
  const c = makeConfig({});
  assert.equal(c.port, 9100);
  assert.equal(c.latencyMs, 150);
  assert.equal(c.failRate, 0);
  assert.equal(c.openAiLatencyMs, null);
  const d = makeConfig({ SIM_PORT: '9200', SIM_FAIL_RATE: '3', SIM_OPENAI_LATENCY_MS: '400', SIM_WEBHOOK_URL: 'http://api/x' });
  assert.equal(d.port, 9200);
  assert.equal(d.failRate, 1); // clamped
  assert.equal(d.openAiLatencyMs, 400);
  assert.equal(d.webhookUrl, 'http://api/x');
});

test('latency is base +/- jitter, per-provider override first, never negative', () => {
  const c = makeConfig({ SIM_LATENCY_MS: '100', SIM_JITTER_MS: '50', SIM_GEMINI_LATENCY_MS: '300' });
  assert.equal(latencyFor(c, 'openai', () => 0.5), 100);
  assert.equal(latencyFor(c, 'openai', () => 1), 150);
  assert.equal(latencyFor(c, 'openai', () => 0), 50);
  assert.equal(latencyFor(c, 'gemini', () => 0.5), 300);
  assert.equal(latencyFor(makeConfig({ SIM_LATENCY_MS: '10', SIM_JITTER_MS: '50' }), 'x', () => 0), 0);
});

test('runtime config accepts known numeric keys only and validates them', () => {
  const c = makeConfig({});
  assert.deepEqual(applyRuntimeConfig(c, { latencyMs: 900, failRate: 0.25, unknown: 1 }), { latencyMs: 900, failRate: 0.25 });
  assert.equal(c.latencyMs, 900);
  assert.deepEqual(applyRuntimeConfig(c, { openAiLatencyMs: null }), { openAiLatencyMs: null });
  assert.throws(() => applyRuntimeConfig(c, { latencyMs: -1 }), RangeError);
  assert.throws(() => applyRuntimeConfig(c, { failRate: 2 }), /between 0 and 1/);
  assert.throws(() => applyRuntimeConfig(c, { failStatus: 200 }), /HTTP error status/);
  assert.throws(() => applyRuntimeConfig(c, { latencyMs: null }), RangeError);
  assert.deepEqual(applyRuntimeConfig(c, 'nonsense'), {});
});

test('the OpenAI answer has the two fields the API parses', () => {
  const a = buildOpenAiAnswer();
  assert.match(a.session.id, /^sess_[0-9a-f]{32}$/);
  assert.ok(a.transport.sdp.startsWith('v=0'));
});

test('webhook signatures are HS256 JWTs whose sha256 claim is the body hash', () => {
  const body = '{"event":"room_finished"}';
  const token = signWebhook(body, { apiKey: 'k', apiSecret: 's3cret', now: 1000 });
  const [header, claims, signature] = token.split('.');
  assert.deepEqual(JSON.parse(Buffer.from(header, 'base64url')), { alg: 'HS256', typ: 'JWT' });
  const payload = JSON.parse(Buffer.from(claims, 'base64url'));
  assert.equal(payload.iss, 'k');
  assert.equal(payload.exp, 1300);
  assert.equal(payload.sha256, createHash('sha256').update(body).digest('base64'));
  assert.equal(signature, createHmac('sha256', 's3cret').update(`${header}.${claims}`).digest('base64url'));
});

test('webhook events carry a unique id and the room name the API looks up', () => {
  const a = buildWebhookEvent('room_started', { sid: 'RM_1', name: 'oet-speaking-sps_1' });
  const b = buildWebhookEvent('room_started', { sid: 'RM_1', name: 'oet-speaking-sps_1' });
  assert.equal(a.event, 'room_started');
  assert.deepEqual(a.room, { sid: 'RM_1', name: 'oet-speaking-sps_1' });
  assert.notEqual(a.id, b.id);
});

test('SSE chunks end the way each dialect ends', () => {
  const openai = sseChunks('openai', { chunks: 3 });
  assert.equal(openai.length, 5);
  assert.equal(openai[openai.length - 1], 'data: [DONE]\n\n');
  const anthropic = sseChunks('anthropic', { chunks: 2 });
  assert.ok(anthropic[0].startsWith('event: message_start'));
  assert.ok(anthropic[anthropic.length - 1].startsWith('event: message_stop'));
});

test('OpenAI and Gemini routes answer the shapes the live-voice service parses', async () => {
  await withSim({}, async (base) => {
    const offer = await (await post(`${base}/openai/v1/live/sessions`, { session: { model: 'm' }, transport: { type: 'webrtc', sdp: 'v=0' } })).json();
    assert.ok(offer.session.id);
    assert.ok(offer.transport.sdp);
    assert.equal((await post(`${base}/openai/v1/live/sessions/${offer.session.id}/hangup`)).status, 200);
    assert.deepEqual(await (await fetch(`${base}/openai/v1/models/${encodeURIComponent('gpt-live-1')}`)).json(), { id: 'gpt-live-1', object: 'model', owned_by: 'load-sim' });
    const token = await (await post(`${base}/gemini/v1beta/auth_tokens`, { uses: 1 })).json();
    assert.match(token.name, /^auth_tokens\/[0-9a-f]{32}$/);
    assert.deepEqual(await (await fetch(`${base}/gemini/v1beta/models/gemini-3.8-live`)).json(), { name: 'models/gemini-3.8-live' });
  });
});

test('LiveKit Twirp rooms are created idempotently, tracked and deleted', async () => {
  await withSim({}, async (base, sim) => {
    const create = () => post(`${base}/twirp/livekit.RoomService/CreateRoom`, { name: 'oet-speaking-sps_1' }).then((r) => r.json());
    const first = await create();
    const second = await create();
    assert.equal(first.sid, second.sid);
    assert.equal(first.name, 'oet-speaking-sps_1');
    assert.equal(sim.state.roomsCreated, 1);
    const egress = await (await post(`${base}/twirp/livekit.Egress/StartEgress`, { room_name: 'oet-speaking-sps_1' })).json();
    assert.match(egress.egress_id, /^EG_/);
    assert.equal(egress.room_id, first.sid);
    assert.equal((await post(`${base}/twirp/livekit.Egress/StopEgress`, { egress_id: egress.egress_id })).status, 200);
    assert.equal((await post(`${base}/twirp/livekit.RoomService/DeleteRoom`, { room: 'oet-speaking-sps_1' })).status, 200);
    assert.equal(sim.state.roomsDeleted, 1);
    const stats = await (await fetch(`${base}/sim/stats`)).json();
    assert.deepEqual(stats.rooms, { created: 1, deleted: 1, active: 0 });
    assert.equal(stats.config.liveKitApiSecret, '[redacted]');
  });
});

test('webhooks are signed, delivered to the configured URL and scheduled for a created room', async () => {
  const sent = [];
  const fetchImpl = async (url, init) => {
    sent.push({ url, init });
    return { status: 200 };
  };
  await withSim({ SIM_WEBHOOK_URL: 'http://api.test/hook', SIM_ROOM_LIFETIME_S: '1', SIM_LIVEKIT_API_SECRET: 'top-secret' }, async (base, sim) => {
    await post(`${base}/twirp/livekit.RoomService/CreateRoom`, { name: 'oet-speaking-sps_9' });
    await new Promise((resolve) => setTimeout(resolve, 1900));
    const events = sent.map((s) => JSON.parse(s.init.body).event);
    assert.deepEqual(events, ['room_started', 'participant_joined', 'participant_joined', 'egress_ended', 'room_finished']);
    for (const s of sent) {
      assert.equal(s.url, 'http://api.test/hook');
      const [header, claims, signature] = s.init.headers.Authorization.split('.');
      assert.equal(signature, createHmac('sha256', 'top-secret').update(`${header}.${claims}`).digest('base64url'));
      assert.equal(JSON.parse(Buffer.from(claims, 'base64url')).sha256, createHash('sha256').update(s.init.body).digest('base64'));
    }
    const finished = JSON.parse(sent[3].init.body);
    assert.equal(finished.egressInfo.fileResults[0].location, 's3://load-sim/oet-speaking-sps_9.ogg');
    assert.equal(sim.state.webhooksSent, 5);
  }, { fetchImpl });
});

test('deleting a room cancels its scheduled events and emits room_finished once', async () => {
  const sent = [];
  await withSim({ SIM_WEBHOOK_URL: 'http://api.test/hook', SIM_ROOM_LIFETIME_S: '60' }, async (base) => {
    await post(`${base}/twirp/livekit.RoomService/CreateRoom`, { name: 'r1' });
    await post(`${base}/twirp/livekit.RoomService/DeleteRoom`, { room: 'r1' });
    await new Promise((resolve) => setTimeout(resolve, 600));
    assert.deepEqual(sent.map((s) => JSON.parse(s.init.body).event), ['room_finished']);
  }, { fetchImpl: async (url, init) => { sent.push({ url, init }); return { status: 202 }; } });
});

test('a failed webhook delivery is counted, not thrown', async () => {
  await withSim({ SIM_WEBHOOK_URL: 'http://api.test/hook' }, async (base, sim) => {
    const r = await (await post(`${base}/sim/emit`, { event: 'room_finished', room: 'x' })).json();
    assert.equal(r.status, 500);
    assert.equal(sim.state.webhooksFailed, 1);
  }, { fetchImpl: async () => ({ status: 500 }) });
  await withSim({ SIM_WEBHOOK_URL: 'http://api.test/hook' }, async (base, sim) => {
    await post(`${base}/sim/emit`, { event: 'room_finished', room: 'x' });
    assert.equal(sim.state.webhooksFailed, 1);
  }, { fetchImpl: async () => { throw new Error('connect ECONNREFUSED'); } });
});

test('fault injection returns the configured status with Retry-After, and can be switched off', async () => {
  await withSim({}, async (base, sim) => {
    assert.equal((await post(`${base}/sim/config`, { failRate: 1, failStatus: 503 })).status, 200);
    const failed = await post(`${base}/openai/v1/live/sessions`, {});
    assert.equal(failed.status, 503);
    assert.equal(failed.headers.get('retry-after'), '2');
    assert.equal(sim.state.injectedFailures, 1);
    // control routes are never faulted
    assert.equal((await fetch(`${base}/sim/health`)).status, 200);
    await post(`${base}/sim/config`, { failRate: 0 });
    assert.equal((await post(`${base}/openai/v1/live/sessions`, {})).status, 200);
    assert.equal((await post(`${base}/sim/config`, { failRate: 9 })).status, 400);
  });
});

test('latency is applied before the answer', async () => {
  await withSim({ SIM_LATENCY_MS: '120', SIM_JITTER_MS: '0' }, async (base) => {
    const started = Date.now();
    await post(`${base}/openai/v1/live/sessions`, {});
    assert.ok(Date.now() - started >= 100);
  });
});

test('streamed completions arrive as server-sent events', async () => {
  await withSim({ SIM_STREAM_CHUNKS: '4', SIM_STREAM_DELAY_MS: '1' }, async (base) => {
    const res = await post(`${base}/openai/v1/chat/completions`, { stream: true });
    assert.equal(res.headers.get('content-type'), 'text/event-stream');
    const text = await res.text();
    assert.equal((text.match(/^data: \{/gm) ?? []).length, 5);
    assert.ok(text.trimEnd().endsWith('data: [DONE]'));
    const plain = await (await post(`${base}/anthropic/v1/messages`, { model: 'x' })).json();
    assert.equal(plain.content[0].text, 'simulated');
  });
});

test('unknown routes and malformed bodies are answered, not crashed on', async () => {
  await withSim({}, async (base) => {
    assert.equal((await fetch(`${base}/nope`)).status, 404);
    const bad = await fetch(`${base}/gemini/v1beta/auth_tokens`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{not json' });
    assert.equal(bad.status, 400);
    const stats = await (await fetch(`${base}/sim/stats`)).json();
    assert.ok(stats.requests['POST /gemini/v1beta/auth_tokens'] >= 1);
  });
});
