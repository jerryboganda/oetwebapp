import http from 'node:http';
import type { AddressInfo } from 'node:net';
import type { FastifyInstance } from 'fastify';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SYSTEM_SESSION_ID } from '../src/contract.js';
import { buildServer, tokenMatches } from '../src/server.js';
import { OWNER_ID, TEST_PROXY_TOKEN, TEST_TOKEN, activateLease, authHeaders, createHarness, type TestHarness } from './helpers.js';

describe('control server', () => {
  let h: TestHarness;
  let app: FastifyInstance;

  beforeEach(async () => {
    h = createHarness();
    app = buildServer(h.context, { heartbeatMs: 200 });
    await app.ready();
  });

  afterEach(async () => {
    await app.close();
    h.cleanup();
  });

  describe('authentication', () => {
    it('serves /healthz without credentials', async () => {
      const res = await app.inject({ method: 'GET', url: '/healthz' });
      expect(res.statusCode).toBe(200);
      expect(res.json()).toEqual({ ok: true, version: 'test', activeTurns: 0, draining: false });
    });

    it('rejects a missing or wrong internal token with 401 and the error envelope', async () => {
      const missing = await app.inject({ method: 'GET', url: '/v1/status' });
      expect(missing.statusCode).toBe(401);
      expect(missing.json()).toEqual({ error: { code: 'unauthorized', message: expect.any(String) } });

      const wrong = await app.inject({ method: 'GET', url: '/v1/status', headers: { ...authHeaders, 'x-oet-internal-token': `${TEST_TOKEN}x` } });
      expect(wrong.statusCode).toBe(401);

      const bearer = await app.inject({ method: 'GET', url: '/v1/status', headers: { authorization: `Bearer ${TEST_TOKEN}`, 'x-oet-owner-account': OWNER_ID } });
      expect(bearer.statusCode).toBe(401);
    });

    it('requires an allow-listed owner account (403 otherwise)', async () => {
      const none = await app.inject({ method: 'GET', url: '/v1/status', headers: { 'x-oet-internal-token': TEST_TOKEN } });
      expect(none.statusCode).toBe(403);
      expect(none.json().error.code).toBe('not_owner');

      const other = await app.inject({ method: 'GET', url: '/v1/status', headers: { ...authHeaders, 'x-oet-owner-account': '00000000-0000-4000-8000-000000000000' } });
      expect(other.statusCode).toBe(403);

      const caseInsensitive = await app.inject({ method: 'GET', url: '/v1/status', headers: { ...authHeaders, 'x-oet-owner-account': OWNER_ID.toUpperCase() } });
      expect(caseInsensitive.statusCode).toBe(200);
    });

    it('requires auth even for unknown routes, then answers 404 in the envelope', async () => {
      expect((await app.inject({ method: 'GET', url: '/v1/nope' })).statusCode).toBe(401);
      const res = await app.inject({ method: 'GET', url: '/v1/nope', headers: authHeaders });
      expect(res.statusCode).toBe(404);
      expect(res.json()).toEqual({ error: { code: 'not_found', message: expect.any(String) } });
    });

    it('guards /internal/approvals with the proxy token, not the internal token', async () => {
      const payload = { source: 'egress', sessionId: null, summary: 'CONNECT example.invalid:443', target: 'example.invalid', reasons: [] };
      const withInternal = await app.inject({ method: 'POST', url: '/internal/approvals', headers: authHeaders, payload });
      expect(withInternal.statusCode).toBe(401);

      h.control.killed = true; // resolves immediately without a card
      const withProxy = await app.inject({ method: 'POST', url: '/internal/approvals', headers: { 'x-oet-proxy-token': TEST_PROXY_TOKEN }, payload });
      expect(withProxy.statusCode).toBe(200);
      expect(withProxy.json()).toEqual({ decision: 'deny', scope: 'once' });

      const bad = await app.inject({ method: 'POST', url: '/internal/approvals', headers: { 'x-oet-proxy-token': TEST_PROXY_TOKEN }, payload: { source: 'ftp' } });
      expect(bad.statusCode).toBe(400);
    });

    it('compares tokens in constant time without leaking length', () => {
      expect(tokenMatches(TEST_TOKEN, TEST_TOKEN)).toBe(true);
      expect(tokenMatches(TEST_TOKEN, TEST_TOKEN.slice(0, -1))).toBe(false);
      expect(tokenMatches(TEST_TOKEN, undefined)).toBe(false);
      expect(tokenMatches(TEST_TOKEN, ['a'])).toBe(false);
      expect(tokenMatches(null, TEST_TOKEN)).toBe(false);
    });
  });

  describe('routes', () => {
    it('reports ConsoleStatus including the system approval queue', async () => {
      activateLease(h);
      const res = await app.inject({ method: 'GET', url: '/v1/status', headers: authHeaders });
      expect(res.statusCode).toBe(200);
      const body = res.json();
      expect(body).toMatchObject({
        version: 'test',
        draining: false,
        killed: false,
        updatePending: false,
        activeTurns: 0,
        maxConcurrentTurns: 2,
        github: { agentTokenSet: false, shipTokenSet: false },
        systemApprovals: [],
        systemSessionId: SYSTEM_SESSION_ID,
      });
      expect(body.engines.claude.auth.state).toBe('signed_in');
      expect(body.engines.opencode.auth.state).toBe('signed_in');
      expect(body.lease.expiresAt).toEqual(expect.any(String));
    });

    it('validates OpenCode OAuth provider selection and drops credential fields', async () => {
      const missing = await app.inject({ method: 'POST', url: '/v1/auth/opencode/connect', headers: authHeaders, payload: {} });
      expect(missing.statusCode).toBe(400);
      expect(h.adapters.opencode.connectOptions).toBeUndefined();

      const connected = await app.inject({
        method: 'POST',
        url: '/v1/auth/opencode/connect',
        headers: authHeaders,
        payload: { providerId: 'github-copilot', methodIndex: 1, apiKey: 'must-not-forward' },
      });
      expect(connected.statusCode).toBe(200);
      expect(h.adapters.opencode.connectOptions).toEqual({ providerId: 'github-copilot', apiKey: 'must-not-forward' });

      const oauth = await app.inject({
        method: 'POST',
        url: '/v1/auth/opencode/connect',
        headers: authHeaders,
        payload: { providerId: 'github-copilot', methodIndex: 1 },
      });
      expect(oauth.statusCode).toBe(200);
      expect(h.adapters.opencode.connectOptions).toEqual({ providerId: 'github-copilot', methodIndex: 1 });

      const badKey = await app.inject({
        method: 'POST',
        url: '/v1/auth/opencode/connect',
        headers: authHeaders,
        payload: { providerId: 'github-copilot', apiKey: '   ' },
      });
      expect(badKey.statusCode).toBe(400);
    });

    it('clamps the lease to 3 minutes', async () => {
      const res = await app.inject({ method: 'POST', url: '/v1/lease', headers: authHeaders, payload: { expiresAt: new Date(Date.now() + 3_600_000).toISOString() } });
      expect(res.statusCode).toBe(200);
      const expiresAt = Date.parse(res.json().expiresAt);
      expect(expiresAt - Date.now()).toBeLessThanOrEqual(180_000);
      expect(expiresAt - Date.now()).toBeGreaterThan(170_000);
      expect((await app.inject({ method: 'POST', url: '/v1/lease', headers: authHeaders, payload: { expiresAt: 'soon' } })).statusCode).toBe(400);
    });

    it('creates sessions and enforces the 2-turn concurrency cap with 429', async () => {
      activateLease(h);
      const ids: string[] = [];
      for (let i = 0; i < 3; i += 1) {
        const res = await app.inject({ method: 'POST', url: '/v1/sessions', headers: authHeaders, payload: { engine: 'claude', model: 'model-a', mode: 'guarded', title: `s${i}` } });
        expect(res.statusCode).toBe(200);
        ids.push(res.json().id);
      }
      for (const id of ids.slice(0, 2)) {
        const res = await app.inject({ method: 'POST', url: `/v1/sessions/${id}/messages`, headers: authHeaders, payload: { text: 'go' } });
        expect(res.statusCode).toBe(200);
        expect(res.json().turnId).toMatch(/^[0-9A-HJKMNP-TV-Z]{26}$/);
      }
      const third = await app.inject({ method: 'POST', url: `/v1/sessions/${ids[2]}/messages`, headers: authHeaders, payload: { text: 'go' } });
      expect(third.statusCode).toBe(429);
      expect(third.json().error.code).toBe('concurrency_limit');
      expect((await app.inject({ method: 'GET', url: '/healthz' })).json().activeTurns).toBe(2);

      const list = await app.inject({ method: 'GET', url: '/v1/sessions', headers: authHeaders });
      expect(list.json()).toHaveLength(3);
    });

    it('validates bodies and ids', async () => {
      const bad = await app.inject({ method: 'POST', url: '/v1/sessions', headers: authHeaders, payload: { engine: 'claude' } });
      expect(bad.statusCode).toBe(400);
      expect(bad.json().error.code).toBe('bad_request');
      const badJson = await app.inject({ method: 'POST', url: '/v1/sessions', headers: { ...authHeaders, 'content-type': 'application/json' }, payload: '{nope' });
      expect(badJson.statusCode).toBe(400);
      expect((await app.inject({ method: 'GET', url: '/v1/sessions/not-a-ulid', headers: authHeaders })).statusCode).toBe(404);
      expect((await app.inject({ method: 'GET', url: '/v1/sessions/01J9ZQ4X7V3N8K2M5P6R7S8T9V', headers: authHeaders })).statusCode).toBe(404);
    });

    it('records the creating owner account as createdBy (lower-cased)', async () => {
      const created = await app.inject({
        method: 'POST',
        url: '/v1/sessions',
        headers: { ...authHeaders, 'x-oet-owner-account': OWNER_ID.toUpperCase() },
        payload: { engine: 'claude', model: 'model-a', mode: 'guarded', title: 'Mine' },
      });
      expect(created.statusCode).toBe(200);
      const id = created.json().id as string;
      expect(created.json().createdBy).toBe(OWNER_ID.toLowerCase());
      expect(created.json().firstMessage).toBeUndefined();
      expect(h.store.getSession(id)?.createdBy).toBe(OWNER_ID.toLowerCase());

      const list = await app.inject({ method: 'GET', url: '/v1/sessions', headers: authHeaders });
      expect(list.json()).toEqual([expect.objectContaining({ id, createdBy: OWNER_ID.toLowerCase() })]);
    });

    it('filters and pages GET /v1/sessions', async () => {
      const make = async (title: string, engine: 'claude' | 'codex' | 'opencode'): Promise<string> => {
        const res = await app.inject({ method: 'POST', url: '/v1/sessions', headers: authHeaders, payload: { engine, model: 'model-a', mode: 'guarded', title } });
        expect(res.statusCode).toBe(200);
        return res.json().id as string;
      };
      const a = await make('Fix T3 login', 'claude');
      const b = await make('Docs tweak', 'codex');
      const c = await make('Another t3 thing', 'claude');
      const d = await make('OpenCode session', 'opencode');
      // Deterministic order: d newest, then c, b, and a.
      h.store.updateSession(a, { updatedAt: '2026-09-01T10:00:00.000Z' });
      h.store.updateSession(b, { updatedAt: '2026-09-02T10:00:00.000Z' });
      h.store.updateSession(c, { updatedAt: '2026-09-03T10:00:00.000Z' });
      h.store.updateSession(d, { updatedAt: '2026-09-04T10:00:00.000Z' });

      const get = async (qs: string): Promise<string[]> => {
        const res = await app.inject({ method: 'GET', url: `/v1/sessions${qs}`, headers: authHeaders });
        expect(res.statusCode).toBe(200);
        return (res.json() as { id: string }[]).map((s) => s.id);
      };
      expect(await get('')).toEqual([d, c, b, a]);
      expect(await get('?q=t3')).toEqual([c, a]);
      expect(await get('?engine=codex')).toEqual([b]);
      expect(await get('?engine=opencode')).toEqual([d]);
      expect(await get('?status=idle&engine=claude')).toEqual([c, a]);
      expect(await get('?limit=2')).toEqual([d, c]);
      expect(await get(`?limit=2&before=${encodeURIComponent('2026-09-02T10:00:00.000Z')}`)).toEqual([a]);
      expect(await get('?unknown=1')).toEqual([d, c, b, a]);
    });

    it('rejects invalid GET /v1/sessions query values with 400', async () => {
      for (const qs of [
        '?limit=0',
        '?limit=201',
        '?limit=abc',
        '?limit=1.5',
        '?status=done',
        '?engine=gemini',
        '?includeArchived=yes',
        '?before=yesterday',
        `?q=${'x'.repeat(101)}`,
        '?status=idle&status=error',
      ]) {
        const res = await app.inject({ method: 'GET', url: `/v1/sessions${qs}`, headers: authHeaders });
        expect(res.statusCode, qs).toBe(400);
        expect(res.json()).toEqual({ error: { code: 'bad_request', message: expect.any(String) } });
      }
      expect((await app.inject({ method: 'GET', url: '/v1/sessions?limit=200&includeArchived=TRUE&status=archived', headers: authHeaders })).statusCode).toBe(200);
    });

    it('returns 423 for new turns while draining and after stop-all; drain:false resumes', async () => {
      activateLease(h);
      const created = await app.inject({ method: 'POST', url: '/v1/sessions', headers: authHeaders, payload: { engine: 'claude', model: 'model-a', mode: 'guarded' } });
      const id = created.json().id as string;
      await app.inject({ method: 'POST', url: `/v1/sessions/${id}/messages`, headers: authHeaders, payload: { text: 'long task' } });
      await vi.waitFor(() => expect(h.adapters.claude.turns).toHaveLength(1));

      const stop = await app.inject({ method: 'POST', url: '/v1/admin/stop-all', headers: authHeaders });
      expect(stop.statusCode).toBe(200);
      expect(stop.json()).toEqual({ stoppedTurns: 1, killedProcesses: 3 });
      expect(h.killed).toEqual({ processes: 1, containers: 1 });
      await vi.waitFor(() => expect(h.sessions.activeTurnCount()).toBe(0));

      const blocked = await app.inject({ method: 'POST', url: `/v1/sessions/${id}/messages`, headers: authHeaders, payload: { text: 'again' } });
      expect(blocked.statusCode).toBe(423);
      expect(blocked.json().error.code).toBe('killed');

      const drain = await app.inject({ method: 'POST', url: '/v1/admin/drain', headers: authHeaders, payload: { draining: true } });
      expect(drain.json()).toEqual({ draining: true, activeTurns: 0 });
      expect((await app.inject({ method: 'GET', url: '/healthz' })).json().draining).toBe(true);

      const resume = await app.inject({ method: 'POST', url: '/v1/admin/drain', headers: authHeaders, payload: { draining: false } });
      expect(resume.json()).toEqual({ draining: false, activeTurns: 0 });
      const ok = await app.inject({ method: 'POST', url: `/v1/sessions/${id}/messages`, headers: authHeaders, payload: { text: 'again' } });
      expect(ok.statusCode).toBe(200);
    });

    it('apply-update drains and dispatches the console workflow', async () => {
      const res = await app.inject({ method: 'POST', url: '/v1/admin/apply-update', headers: authHeaders });
      expect(res.statusCode).toBe(200);
      expect(res.json()).toEqual({ draining: true, activeTurns: 0, dispatched: true });
      expect((await app.inject({ method: 'GET', url: '/healthz' })).json().draining).toBe(true);
      expect((await app.inject({ method: 'POST', url: '/v1/admin/apply-update' })).statusCode).toBe(401);
    });

    it('resolves approvals through the API with a single-use nonce', async () => {
      activateLease(h);
      h.adapters.claude.script = async (turn) => {
        await turn.hooks.onToolCall({ toolCallId: 'a', name: 'Bash', input: {}, command: 'bash -c id' }, turn.signal);
      };
      const created = await app.inject({ method: 'POST', url: '/v1/sessions', headers: authHeaders, payload: { engine: 'claude', model: 'model-a', mode: 'guarded' } });
      const id = created.json().id as string;
      await app.inject({ method: 'POST', url: `/v1/sessions/${id}/messages`, headers: authHeaders, payload: { text: 'x' } });
      await vi.waitFor(() => expect(h.approvals.listPending(id)).toHaveLength(1));

      const detail = await app.inject({ method: 'GET', url: `/v1/sessions/${id}`, headers: authHeaders });
      const card = detail.json().pendingApprovals[0];
      expect(card).toMatchObject({ uid: 10002, tainted: false, command: 'bash -c id' });

      const url = `/v1/sessions/${id}/approvals/${card.approvalId}`;
      const wrong = await app.inject({ method: 'POST', url, headers: authHeaders, payload: { decision: 'approve', nonce: 'nope' } });
      expect(wrong.statusCode).toBe(409);
      const ok = await app.inject({ method: 'POST', url, headers: authHeaders, payload: { decision: 'approve', nonce: card.nonce } });
      expect(ok.statusCode).toBe(200);
      expect(ok.json()).toEqual({ ok: true });
      const replay = await app.inject({ method: 'POST', url, headers: authHeaders, payload: { decision: 'approve', nonce: card.nonce } });
      expect(replay.statusCode).toBe(404);
    });

    it('returns null when a session has never shipped', async () => {
      const created = await app.inject({ method: 'POST', url: '/v1/sessions', headers: authHeaders, payload: { engine: 'claude', model: 'model-a', mode: 'guarded' } });
      const res = await app.inject({ method: 'GET', url: `/v1/sessions/${created.json().id}/ship`, headers: authHeaders });
      expect(res.statusCode).toBe(200);
      expect(res.body).toBe('null');
    });

    it('keeps GitHub tokens write-only', async () => {
      const res = await app.inject({ method: 'PUT', url: '/v1/github-tokens', headers: authHeaders, payload: { shipToken: 'whatever' } });
      expect(res.statusCode).toBe(200);
      expect(JSON.stringify(res.json())).not.toContain('whatever');
    });
  });

  describe('SSE event stream', () => {
    it('replays events after ?after=N, then streams live events and heartbeats', async () => {
      const created = await app.inject({ method: 'POST', url: '/v1/sessions', headers: authHeaders, payload: { engine: 'claude', model: 'model-a', mode: 'guarded' } });
      const id = created.json().id as string;
      h.store.appendEvent(id, 'user_message', { text: 'one' });
      h.store.appendEvent(id, 'user_message', { text: 'two' });
      h.store.appendEvent(id, 'user_message', { text: 'three' });

      await app.listen({ host: '127.0.0.1', port: 0 });
      const { port } = app.server.address() as AddressInfo;
      let received = '';
      const req = http.get({ host: '127.0.0.1', port, path: `/v1/sessions/${id}/events?after=1`, headers: authHeaders });
      const response = await new Promise<http.IncomingMessage>((resolve, reject) => {
        req.on('response', resolve);
        req.on('error', reject);
      });
      expect(response.statusCode).toBe(200);
      expect(response.headers['content-type']).toMatch(/text\/event-stream/);
      response.setEncoding('utf8');
      response.on('data', (chunk: string) => {
        received += chunk;
      });

      await vi.waitFor(() => expect(received).toContain('id: 3'));
      expect(received).not.toContain('id: 1\n');
      expect(received).toContain('id: 2\nevent: user_message\ndata: {');

      h.store.appendEvent(id, 'text', { messageId: 'm', text: 'live' });
      await vi.waitFor(() => expect(received).toContain('id: 4\nevent: text'));
      await vi.waitFor(() => expect(received).toContain('event: heartbeat'));

      const dataLines = received.split('\n').filter((l) => l.startsWith('data: '));
      const parsed = dataLines.map((l) => JSON.parse(l.slice(6)) as { seq?: number; type: string });
      expect(parsed.filter((e) => e.type !== 'heartbeat').map((e) => e.seq)).toEqual([2, 3, 4]);
      expect(parsed.find((e) => e.type === 'heartbeat')?.seq).toBeUndefined();
      req.destroy();
    });

    it('404s for unknown sessions but serves the system queue', async () => {
      const unknown = await app.inject({ method: 'GET', url: '/v1/sessions/01J9ZQ4X7V3N8K2M5P6R7S8T9V/events', headers: authHeaders });
      expect(unknown.statusCode).toBe(404);
      expect(h.sessions.exists(SYSTEM_SESSION_ID)).toBe(true);
    });
  });
});
