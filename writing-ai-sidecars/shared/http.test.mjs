import assert from 'node:assert/strict';
import { once } from 'node:events';
import http from 'node:http';
import test from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { AuthExpiredError, LaneBusyError, QuotaExceededError } from './engine.mjs';
import { createSidecarServer } from './http.mjs';

for (const completionPath of ['/v1/chat/completions', '/v1/messages']) {
  for (const requestPath of [completionPath, completionPath.replace('/v1', '')]) {
    test(`accepts ${requestPath} for ${completionPath}`, async (context) => {
      let calls = 0;
      const server = createSidecarServer({
        engineName: 'protocol-test',
        completionPath,
        port: 0,
        onUsage: async () => ({}),
        onCompletion: async (body) => {
          calls += 1;
          assert.deepEqual(body, { messages: [{ role: 'user', content: 'protocol fixture' }] });
          return { accepted: true };
        },
      });
      context.after(() => server.close());
      await once(server, 'listening');
      const base = `http://127.0.0.1:${server.address().port}`;
      const response = await fetch(`${base}${requestPath}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ messages: [{ role: 'user', content: 'protocol fixture' }] }),
      });

      assert.equal(response.status, 200);
      assert.deepEqual(await response.json(), { accepted: true });
      assert.equal(calls, 1);

      const unknown = await fetch(`${base}/not-a-completion`, { method: 'POST', body: '{}' });
      assert.equal(unknown.status, 404);
      assert.equal(calls, 1);

      const invalid = await fetch(`${base}${requestPath}`, { method: 'POST', body: '{' });
      assert.equal(invalid.status, 400);
      assert.equal(calls, 1);
    });
  }
}

async function start(t, options) {
  const server = createSidecarServer({
    engineName: 'test',
    completionPath: '/v1/messages',
    port: 0,
    onUsage: async () => ({}),
    ...options,
  });
  t.after(() => server.close());
  await once(server, 'listening');
  const { port } = server.address();
  return { base: `http://127.0.0.1:${port}`, port };
}

test('GET /readyz is 200 while the login is good or unknown, 503 when it is dead or the queue is full', async (t) => {
  const lane = { full: false, queueDepth: 2 };
  let login = { authOk: true, plan: 'max' };
  const { base } = await start(t, { lane, login: { status: async () => login }, onCompletion: async () => ({}) });
  const readyz = async () => {
    const response = await fetch(`${base}/readyz`);
    return [response.status, await response.json()];
  };

  assert.deepEqual(await readyz(), [200, { ready: true, reason: null, queueDepth: 2, authOk: true, plan: 'max' }]);
  login = { authOk: null, plan: null };
  assert.deepEqual(await readyz(), [200, { ready: true, reason: null, queueDepth: 2, authOk: null, plan: null }]);
  Object.assign(lane, { full: true, queueDepth: 40 });
  assert.deepEqual(await readyz(), [503, { ready: false, reason: 'lane_full', queueDepth: 40, authOk: null, plan: null }]);
  login = { authOk: false, plan: 'max' };
  assert.deepEqual(await readyz(), [503, { ready: false, reason: 'auth_expired', queueDepth: 40, authOk: false, plan: 'max' }]);
});

test('typed engine failures map to the statuses the backend error parser classifies', async (t) => {
  let failure;
  const { base } = await start(t, { onCompletion: async () => { throw failure; } });
  const post = async () => {
    const response = await fetch(`${base}/v1/messages`, { method: 'POST', body: '{}' });
    return [response.status, response.headers.get('retry-after'), await response.json()];
  };

  failure = new LaneBusyError('lane queue full (40 waiting)');
  assert.deepEqual(await post(), [503, '30', {
    error: { code: 'lane_busy', message: 'lane queue full (40 waiting)', type: 'overloaded_error' },
  }]);
  failure = new AuthExpiredError('Claude login expired or invalid: Please run /login');
  assert.deepEqual(await post(), [401, null, {
    error: { code: 'auth_expired', message: 'Claude login expired or invalid: Please run /login', type: 'authentication_error' },
  }]);
  failure = new QuotaExceededError('Claude subscription quota/rate limit: limit reached');
  assert.deepEqual(await post(), [429, null, {
    error: { code: 'quota_exceeded', message: 'Claude subscription quota/rate limit: limit reached', type: 'rate_limit_error' },
  }]);
  failure = new Error('claude exited 1: boom');
  assert.deepEqual(await post(), [502, null, { error: { code: 'engine_error', message: 'claude exited 1: boom' } }]);
});

test('the completion signal aborts when the client disconnects, never after a normal response', { timeout: 10_000 }, async (t) => {
  const signals = [];
  let entered;
  const inCompletion = new Promise((resolve) => { entered = resolve; });
  const { base, port } = await start(t, {
    onCompletion: (body, { signal }) => {
      signals.push(signal);
      if (!body.hang) return Promise.resolve({ ok: true });
      entered();
      return new Promise((_, reject) => signal.addEventListener('abort', () => reject(signal.reason), { once: true }));
    },
  });

  const answered = await fetch(`${base}/v1/messages`, { method: 'POST', body: '{}' });
  assert.deepEqual(await answered.json(), { ok: true });
  await delay(50);
  assert.equal(signals[0].aborted, false);

  const request = http.request({ host: '127.0.0.1', port, method: 'POST', path: '/v1/messages' });
  request.on('error', () => { /* the socket is destroyed on purpose */ });
  request.end(JSON.stringify({ hang: true }));
  await inCompletion;
  assert.equal(signals[1].aborted, false);
  request.destroy();
  await new Promise((resolve) => {
    if (signals[1].aborted) resolve();
    else signals[1].addEventListener('abort', resolve, { once: true });
  });
  assert.equal(signals[1].reason.name, 'AbortError');
});
