// End-to-end tests against a FAKE Docker daemon listening on a unix socket in
// a temp dir. No real Docker, no network beyond loopback.
import { mkdtempSync, rmSync } from 'node:fs';
import http from 'node:http';
import net, { type AddressInfo } from 'node:net';
import os from 'node:os';
import path from 'node:path';
import type { Duplex } from 'node:stream';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ApprovalBroker, type ApprovalRequestBody, type ApprovalResult } from '../src/approvals.js';
import type { DockerProxyConfig } from '../src/config.js';
import { SessionGrants, TtlSet } from '../src/grants.js';
import { createLogger } from '../src/log.js';
import { createDockerProxyServer } from '../src/server.js';
import { DockerUpstream } from '../src/upstream.js';

const TOKEN = 'test-proxy-token-0123456789abcdefABCDEF';
const SESSION = '01J9ZQ4Y8M3K2N7P5R6S8T0V1W';
const EXEC_ID = 'e'.repeat(64);

interface FakeContainer {
  Id: string;
  Name: string;
  Config: { Env: string[] };
}

const CONTAINERS: FakeContainer[] = [
  { Id: 'a'.repeat(64), Name: '/oet-api-blue', Config: { Env: ['SECRET=hunter2-value', 'PATH=/usr/bin'] } },
  { Id: 'b'.repeat(64), Name: '/oet-postgres', Config: { Env: ['POSTGRES_PASSWORD=pw-value'] } },
  { Id: 'c'.repeat(64), Name: '/oet-agent-console', Config: { Env: [] } },
  { Id: 'd'.repeat(64), Name: '/ubag-vps-gateway-1', Config: { Env: [] } },
  { Id: 'f'.repeat(64), Name: '/oet-fleet-manager', Config: { Env: ['FLEET_SECRET=never-visible'] } },
];

function findContainer(ref: string): FakeContainer | undefined {
  return CONTAINERS.find((c) => c.Name.slice(1) === ref || c.Id.startsWith(ref));
}

interface Seen {
  method: string;
  url: string;
  headers: http.IncomingHttpHeaders;
  upgrade?: boolean;
}

interface Harness {
  port: number;
  seen: Seen[];
  approvals: ApprovalRequestBody[];
  logs: Record<string, unknown>[];
  setDecision(result: ApprovalResult): void;
}

let cleanups: Array<() => Promise<void> | void> = [];
/** Bytes the fake daemon received on a hijack connection it did NOT upgrade. */
let afterNotUpgraded: string[] = [];

beforeEach(() => {
  cleanups = [];
  afterNotUpgraded = [];
});

afterEach(async () => {
  for (const cleanup of cleanups.reverse()) await cleanup();
});

function closeHttp(server: http.Server): () => Promise<void> {
  return () =>
    new Promise<void>((resolve) => {
      server.closeAllConnections();
      server.close(() => resolve());
    });
}

function json(res: http.ServerResponse, status: number, body: unknown): void {
  const payload = JSON.stringify(body);
  res.writeHead(status, { 'content-type': 'application/json', 'content-length': Buffer.byteLength(payload), 'api-version': '1.47' });
  res.end(payload);
}

async function startHarness(): Promise<Harness> {
  const dir = mkdtempSync(path.join(os.tmpdir(), 'oetdp-'));
  cleanups.push(() => rmSync(dir, { recursive: true, force: true }));
  const socketPath = path.join(dir, 'docker.sock');
  const seen: Seen[] = [];

  const daemon = http.createServer((req, res) => {
    seen.push({ method: req.method ?? '', url: req.url ?? '', headers: req.headers });
    const chunks: Buffer[] = [];
    req.on('data', (c: Buffer) => chunks.push(c));
    req.on('end', () => {
      const p = (req.url ?? '/').split('?')[0]?.replace(/^\/v[0-9.]+/, '') ?? '/';
      let m: RegExpExecArray | null;
      if (p === '/_ping') {
        res.writeHead(200, { 'content-type': 'text/plain' });
        res.end('OK');
      } else if (p === '/containers/json') {
        json(res, 200, CONTAINERS.map((c) => ({ Id: c.Id, Names: [c.Name] })));
      } else if ((m = /^\/containers\/([^/]+)\/json$/.exec(p))) {
        const container = findContainer(m[1] ?? '');
        if (container) json(res, 200, container);
        else json(res, 404, { message: `No such container: ${m[1] ?? ''}` });
      } else if ((m = /^\/containers\/([^/]+)\/exec$/.exec(p)) && req.method === 'POST') {
        json(res, 201, { Id: EXEC_ID });
      } else if ((m = /^\/exec\/([^/]+)\/json$/.exec(p))) {
        if (m[1] === EXEC_ID) json(res, 200, { ID: EXEC_ID, ContainerID: 'b'.repeat(64), Running: false, ExitCode: 0 });
        else json(res, 404, { message: 'No such exec instance' });
      } else if ((m = /^\/exec\/([^/]+)\/start$/.exec(p)) && req.method === 'POST') {
        res.writeHead(200, { 'content-type': 'application/vnd.docker.raw-stream' });
        res.end('started');
      } else if ((m = /^\/containers\/([^/]+)\/(stop|restart)$/.exec(p)) && req.method === 'POST') {
        res.writeHead(204);
        res.end();
      } else if ((m = /^\/containers\/([^/]+)\/logs$/.exec(p))) {
        res.writeHead(200, { 'content-type': 'application/vnd.docker.multiplexed-stream' });
        res.write('line one\n');
        res.end('line two\n');
      } else {
        json(res, 404, { message: 'page not found' });
      }
    });
  });
  daemon.on('upgrade', (req: http.IncomingMessage, socket: Duplex, head: Buffer) => {
    seen.push({ method: req.method ?? '', url: req.url ?? '', headers: req.headers, upgrade: true });
    if ((req.url ?? '').includes('/attach')) {
      // Like dockerd attaching to a PAUSED container: a plain HTTP error, no
      // hijack, connection kept alive — anything that arrives next would be
      // parsed as a new API request.
      let after = head.toString('latin1');
      socket.on('data', (c: Buffer) => {
        after += c.toString('latin1');
      });
      socket.on('close', () => afterNotUpgraded.push(after));
      // Like dockerd: once the proxy closes its side, close ours. (The http
      // server's sockets allow half-open, so without this the fake daemon kept
      // the connection alive forever and server.close() never resolved.)
      socket.on('end', () => socket.destroy());
      const payload = '{"message":"container is paused"}';
      socket.write(`HTTP/1.1 409 Conflict\r\nContent-Type: application/json\r\nContent-Length: ${payload.length}\r\n\r\n${payload}`);
      return;
    }
    socket.write('HTTP/1.1 101 UPGRADED\r\nContent-Type: application/vnd.docker.raw-stream\r\nConnection: Upgrade\r\nUpgrade: tcp\r\n\r\n');
    socket.end('exec-output');
  });
  await new Promise<void>((resolve) => daemon.listen(socketPath, () => resolve()));
  cleanups.push(closeHttp(daemon));

  const approvals: ApprovalRequestBody[] = [];
  const logs: Record<string, unknown>[] = [];
  let decision: ApprovalResult = { decision: 'deny', scope: 'once' };
  const config: DockerProxyConfig = {
    listenHost: '127.0.0.1',
    listenPort: 0,
    socketPath,
    proxyToken: TOKEN,
    approvalUrl: 'http://127.0.0.1:1/unused',
    approvalTimeoutMs: 1_000,
    maxRequestBodyBytes: 64 * 1024,
    maxTransformBytes: 1024 * 1024,
    sessionGrantTtlMs: 60_000,
    execApprovalTtlMs: 60_000,
  };
  const proxy = createDockerProxyServer({
    config,
    broker: new ApprovalBroker(
      async (body) => {
        approvals.push(body);
        return decision;
      },
      { denyCacheMs: 0 },
    ),
    grants: new SessionGrants(60_000),
    execApprovals: new TtlSet(60_000),
    upstream: new DockerUpstream(socketPath),
    log: createLogger('dockerproxy-test', (line) => logs.push(JSON.parse(line) as Record<string, unknown>)),
  });
  await new Promise<void>((resolve) => proxy.listen(0, '127.0.0.1', () => resolve()));
  cleanups.push(closeHttp(proxy));
  return {
    port: (proxy.address() as AddressInfo).port,
    seen,
    approvals,
    logs,
    setDecision(result) {
      decision = result;
    },
  };
}

interface Reply {
  status: number;
  body: string;
  json: unknown;
}

function call(port: number, method: string, urlPath: string, headers: Record<string, string> = {}, body?: unknown): Promise<Reply> {
  return new Promise((resolve, reject) => {
    const payload = body === undefined ? undefined : Buffer.from(JSON.stringify(body));
    const req = http.request(
      {
        host: '127.0.0.1',
        port,
        method,
        path: urlPath,
        agent: false,
        headers: { ...headers, ...(payload ? { 'content-type': 'application/json', 'content-length': String(payload.length) } : {}) },
      },
      (res) => {
        const chunks: Buffer[] = [];
        res.on('data', (c: Buffer) => chunks.push(c));
        res.on('end', () => {
          const text = Buffer.concat(chunks).toString('utf8');
          let parsed: unknown;
          try {
            parsed = JSON.parse(text);
          } catch {
            parsed = undefined;
          }
          resolve({ status: res.statusCode ?? 0, body: text, json: parsed });
        });
      },
    );
    req.on('error', reject);
    req.end(payload);
  });
}

function rawUpgrade(port: number, urlPath: string, headers: Record<string, string>, pipelined = ''): Promise<string> {
  return new Promise((resolve, reject) => {
    const socket = net.connect({ host: '127.0.0.1', port });
    let data = '';
    let done = false;
    const finish = (): void => {
      if (!done) {
        done = true;
        resolve(data);
      }
    };
    socket.on('connect', () => {
      const body = '{"Detach":false,"Tty":false}';
      const extra = Object.entries(headers)
        .map(([k, v]) => `${k}: ${v}\r\n`)
        .join('');
      socket.write(
        `POST ${urlPath} HTTP/1.1\r\nHost: docker\r\nConnection: Upgrade\r\nUpgrade: tcp\r\nContent-Type: application/json\r\nContent-Length: ${body.length}\r\n${extra}\r\n${body}${pipelined}`,
      );
    });
    socket.on('data', (c: Buffer) => {
      data += c.toString('utf8');
    });
    socket.on('end', finish);
    socket.on('close', finish);
    socket.on('error', reject);
  });
}

const agent = { 'X-Oet-Agent-Session': SESSION };

describe('dockerproxy server', () => {
  it('filters docker ps to OET containers and strips attribution headers', async () => {
    const h = await startHarness();
    const res = await call(h.port, 'GET', '/v1.47/containers/json?all=1', agent);
    expect(res.status).toBe(200);
    expect((res.json as Array<{ Names: string[] }>).map((c) => c.Names[0])).toEqual(['/oet-api-blue', '/oet-postgres']);
    const forwarded = h.seen.find((s) => s.url.startsWith('/v1.47/containers/json'));
    expect(forwarded?.headers['x-oet-agent-session']).toBeUndefined();
    expect(h.logs.some((l) => l.event === 'decision' && l.decision === 'allow' && l.rule === 2 && l.sessionId === SESSION)).toBe(true);
  });

  it('redacts Config.Env and forwards by full id', async () => {
    const h = await startHarness();
    const res = await call(h.port, 'GET', '/v1.47/containers/oet-api-blue/json', agent);
    expect(res.status).toBe(200);
    expect((res.json as FakeContainer).Config.Env).toEqual(['SECRET=<redacted>', 'PATH=<redacted>']);
    expect(res.body).not.toContain('hunter2-value');
    expect(h.seen.some((s) => s.url === `/v1.47/containers/${'a'.repeat(64)}/json`)).toBe(true);
  });

  it('lets the control plane through unfiltered and never forwards the token', async () => {
    const h = await startHarness();
    const res = await call(h.port, 'GET', '/containers/oet-api-blue/json', { 'X-Oet-Control-Token': TOKEN });
    expect(res.status).toBe(200);
    expect((res.json as FakeContainer).Config.Env).toContain('SECRET=hunter2-value');
    expect(h.seen.every((s) => s.headers['x-oet-control-token'] === undefined)).toBe(true);
  });

  it('rejects a wrong control token outright', async () => {
    const h = await startHarness();
    const res = await call(h.port, 'GET', '/containers/json', { 'X-Oet-Control-Token': 'not-the-token-not-the-token-000000' });
    expect(res.status).toBe(403);
    expect(h.seen).toHaveLength(0);
  });

  it('denies the console containers without asking, even by id prefix', async () => {
    const h = await startHarness();
    const stop = await call(h.port, 'POST', '/containers/oet-agent-console/stop', agent);
    expect(stop.status).toBe(403);
    expect((stop.json as { message: string }).message).toMatch(/^blocked by oet-agent-dockerproxy: /);
    const byPrefix = await call(h.port, 'GET', '/containers/cccccccc/json', agent);
    expect(byPrefix.status).toBe(403);
    expect(h.approvals).toHaveLength(0);
    expect(h.seen.some((s) => s.url.includes('/stop'))).toBe(false);
  });

  it('denies the Owner Fleet manager without asking, even by id prefix, and never lists it', async () => {
    const h = await startHarness();
    const stop = await call(h.port, 'POST', '/containers/oet-fleet-manager/stop', agent);
    expect(stop.status).toBe(403);
    expect((stop.json as { message: string }).message).toMatch(/^blocked by oet-agent-dockerproxy: /);
    const logs = await call(h.port, 'GET', '/containers/oet-fleet-manager/logs', agent);
    expect(logs.status).toBe(403);
    const byPrefix = await call(h.port, 'GET', '/containers/ffffffff/json', agent);
    expect(byPrefix.status).toBe(403);
    expect(byPrefix.body).not.toContain('never-visible');
    const exec = await call(h.port, 'POST', '/containers/oet-fleet-manager/exec', agent, { Cmd: ['sh'] });
    expect(exec.status).toBe(403);
    const listed = await call(h.port, 'GET', '/containers/json?all=1', agent);
    expect((listed.json as Array<{ Names: string[] }>).map((c) => c.Names[0])).not.toContain('/oet-fleet-manager');
    expect(h.approvals).toHaveLength(0);
    expect(h.seen.some((s) => s.url.includes('/stop') || s.url.includes('/logs') || s.url.endsWith('/exec'))).toBe(false);
  });

  it('relays daemon 404s for unknown containers without an approval card', async () => {
    const h = await startHarness();
    const res = await call(h.port, 'GET', '/containers/nope/json', agent);
    expect(res.status).toBe(404);
    expect((res.json as { message: string }).message).toBe('No such container: nope');
    expect(h.approvals).toHaveLength(0);
  });

  it('asks before exec into postgres, then lets that exec start (plain and hijacked) without a second card', async () => {
    const h = await startHarness();
    h.setDecision({ decision: 'approve', scope: 'once' });
    const create = await call(h.port, 'POST', '/v1.47/containers/oet-postgres/exec', agent, {
      Cmd: ['psql', '-c', 'select 1'],
      Env: ['PGPASSWORD=do-not-show'],
    });
    expect(create.status).toBe(201);
    expect(create.json).toEqual({ Id: EXEC_ID });
    expect(h.approvals).toHaveLength(1);
    expect(h.approvals[0]).toMatchObject({ source: 'docker', sessionId: SESSION, target: 'oet-postgres' });
    expect(JSON.stringify(h.approvals[0])).not.toContain('do-not-show');

    h.setDecision({ decision: 'deny', scope: 'once' });
    const start = await call(h.port, 'POST', `/v1.47/exec/${EXEC_ID}/start`, agent, { Detach: true });
    expect(start.status).toBe(200);
    const hijacked = await rawUpgrade(h.port, `/v1.47/exec/${EXEC_ID}/start`, agent);
    expect(hijacked).toContain('101 UPGRADED');
    expect(hijacked).toContain('exec-output');
    const inspect = await call(h.port, 'GET', `/v1.47/exec/${EXEC_ID}/json`, agent);
    expect(inspect.status).toBe(200);
    expect(h.approvals).toHaveLength(1);
    const upgraded = h.seen.find((s) => s.upgrade === true);
    expect(upgraded?.headers['x-oet-agent-session']).toBeUndefined();
  });

  it('blocks exec when the owner denies', async () => {
    const h = await startHarness();
    const res = await call(h.port, 'POST', '/containers/oet-api-blue/exec', agent, { Cmd: ['env'] });
    expect(res.status).toBe(403);
    expect((res.json as { message: string }).message).toContain('not approved');
    expect(h.approvals).toHaveLength(1);
    expect(h.seen.some((s) => s.url.endsWith('/exec'))).toBe(false);
  });

  it('refuses hijacked upgrades on anything but attach and exec start', async () => {
    const h = await startHarness();
    const res = await rawUpgrade(h.port, '/containers/create', agent);
    expect(res).toContain('403');
    expect(h.seen).toHaveLength(0);
  });

  it('never lets bytes pipelined behind a non-upgraded hijack reach the daemon', async () => {
    const h = await startHarness();
    const smuggled =
      'POST /containers/create HTTP/1.1\r\nHost: docker\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\n{}';
    const res = await rawUpgrade(h.port, '/v1.47/containers/oet-api-blue/attach?stream=1&stdout=1', agent, smuggled);
    expect(res).toContain('409');
    expect(res).toContain('container is paused');
    // Let the daemon side observe the proxy closing its connection.
    await new Promise((resolve) => setTimeout(resolve, 100));
    expect(afterNotUpgraded.join('')).not.toContain('/containers/create');
    expect(h.seen.some((s) => s.url.includes('/containers/create'))).toBe(false);
    expect(h.seen.find((s) => s.upgrade === true)?.url).toBe(`/v1.47/containers/${'a'.repeat(64)}/attach?stream=1&stdout=1`);
  });

  it('remembers approve-for-session for co-tenant reads in that session only', async () => {
    const h = await startHarness();
    h.setDecision({ decision: 'approve', scope: 'session' });
    const first = await call(h.port, 'GET', '/containers/ubag-vps-gateway-1/logs?tail=10', agent);
    expect(first.status).toBe(200);
    expect(first.body).toBe('line one\nline two\n');
    h.setDecision({ decision: 'deny', scope: 'once' });
    const second = await call(h.port, 'GET', '/containers/ubag-vps-gateway-1/logs?tail=10', agent);
    expect(second.status).toBe(200);
    expect(h.approvals).toHaveLength(1);
    const otherSession = await call(h.port, 'GET', '/containers/ubag-vps-gateway-1/logs', { 'X-Oet-Agent-Session': '01J9ZQ4Y8M3K2N7P5R6S8T0V1X' });
    expect(otherSession.status).toBe(403);
    expect(h.approvals).toHaveLength(2);
  });

  it('streams logs of OET containers without approval', async () => {
    const h = await startHarness();
    const res = await call(h.port, 'GET', '/containers/oet-api-blue/logs?follow=1', agent);
    expect(res.status).toBe(200);
    expect(res.body).toBe('line one\nline two\n');
    expect(h.approvals).toHaveLength(0);
  });

  it('serves /healthz from the daemon ping', async () => {
    const h = await startHarness();
    const res = await call(h.port, 'GET', '/healthz');
    expect(res.status).toBe(200);
    expect(res.json).toEqual({ ok: true });
  });

  it('protects the grant-reset endpoint with the control token', async () => {
    const h = await startHarness();
    expect((await call(h.port, 'DELETE', `/internal/sessions/${SESSION}`)).status).toBe(403);
    expect((await call(h.port, 'DELETE', `/internal/sessions/${SESSION}`, { 'X-Oet-Control-Token': TOKEN })).status).toBe(200);
  });
});
