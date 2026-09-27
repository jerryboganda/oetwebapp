// Loopback-only integration tests: DNS and upstream connects are injected, so
// no real network is used. The fake "internet" is a local TCP echo server and
// a local HTTP server that the injected connectTo() dials instead of the
// vetted public address.
import http from 'node:http';
import net, { type AddressInfo } from 'node:net';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ApprovalBroker, type ApprovalRequestBody, type ApprovalResult } from '../src/approvals.js';
import type { EgressConfig } from '../src/config.js';
import { SessionGrants } from '../src/grants.js';
import { createLogger } from '../src/log.js';
import { createEgressServer } from '../src/server.js';

const TOKEN = 'test-proxy-token-0123456789abcdefABCDEF';
const SESSION = '01J9ZQ4Y8M3K2N7P5R6S8T0V1W';
const PUBLIC_IP = '93.184.216.34';

interface Harness {
  proxyPort: number;
  approvals: ApprovalRequestBody[];
  logs: Record<string, unknown>[];
  setDecision(result: ApprovalResult): void;
  resolved: string[];
}

let closers: Array<() => Promise<void>> = [];

function closeServer(server: net.Server): () => Promise<void> {
  return () =>
    new Promise<void>((resolve) => {
      if (server instanceof http.Server) server.closeAllConnections();
      server.close(() => resolve());
    });
}

async function listen(server: net.Server): Promise<number> {
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', () => resolve()));
  closers.push(closeServer(server));
  return (server.address() as AddressInfo).port;
}

async function startHarness(resolveMap: Record<string, string[]> = {}): Promise<Harness> {
  const echo = net.createServer((socket) => socket.pipe(socket));
  const echoPort = await listen(echo);
  const web = http.createServer((req, res) => {
    res.writeHead(200, { 'content-type': 'text/plain', 'x-seen-host': req.headers.host ?? '', 'x-seen-control': String(req.headers['x-oet-control-token'] ?? '') });
    res.end(`upstream saw ${req.method} ${req.url}`);
  });
  const webPort = await listen(web);

  const approvals: ApprovalRequestBody[] = [];
  const logs: Record<string, unknown>[] = [];
  const resolved: string[] = [];
  let decision: ApprovalResult = { decision: 'deny', scope: 'once' };
  const broker = new ApprovalBroker(
    async (b) => {
      approvals.push(b);
      return decision;
    },
    { denyCacheMs: 0 },
  );
  const config: EgressConfig = {
    listenHost: '127.0.0.1',
    listenPort: 0,
    proxyToken: TOKEN,
    approvalUrl: 'http://127.0.0.1:1/unused',
    approvalTimeoutMs: 1_000,
    connectPorts: new Set([443]),
    httpPorts: new Set([80]),
    sessionGrantTtlMs: 60_000,
    denyCacheMs: 0,
    tunnelIdleTimeoutMs: 10_000,
    connectTimeoutMs: 2_000,
    maxConnections: 64,
  };
  const proxy = createEgressServer({
    config,
    broker,
    grants: new SessionGrants(60_000),
    log: createLogger('egress-test', (line) => logs.push(JSON.parse(line) as Record<string, unknown>)),
    resolveHost: async (host) => {
      resolved.push(host);
      return resolveMap[host] ?? [PUBLIC_IP];
    },
    connectTo: (_address, port) => net.connect({ host: '127.0.0.1', port: port === 80 ? webPort : echoPort }),
  });
  const proxyPort = await listen(proxy);
  return {
    proxyPort,
    approvals,
    logs,
    resolved,
    setDecision(result) {
      decision = result;
    },
  };
}

function basic(sessionId: string): string {
  return `Basic ${Buffer.from(`${sessionId}:x`).toString('base64')}`;
}

/** Sends a CONNECT, returns the status line/body; on 200 also round-trips `probe` through the tunnel. */
function connect(proxyPort: number, target: string, headers: Record<string, string> = {}, probe = 'ping'): Promise<{ status: number; body: string; echoed?: string }> {
  return new Promise((resolve, reject) => {
    const socket = net.connect({ host: '127.0.0.1', port: proxyPort });
    let buffer = '';
    let established = false;
    socket.on('error', reject);
    socket.on('connect', () => {
      const extra = Object.entries(headers).map(([k, v]) => `${k}: ${v}\r\n`).join('');
      socket.write(`CONNECT ${target} HTTP/1.1\r\nHost: ${target}\r\n${extra}\r\n`);
    });
    socket.on('data', (chunk: Buffer) => {
      buffer += chunk.toString('utf8');
      if (!established) {
        const end = buffer.indexOf('\r\n\r\n');
        if (end < 0) return;
        const status = Number(buffer.slice(9, 12));
        if (status === 200) {
          established = true;
          buffer = buffer.slice(end + 4);
          socket.write(probe);
        }
      }
      if (established && buffer.length >= probe.length) {
        socket.destroy();
        resolve({ status: 200, body: '', echoed: buffer });
      }
    });
    socket.on('close', () => {
      if (!established) {
        const end = buffer.indexOf('\r\n\r\n');
        resolve({ status: Number(buffer.slice(9, 12)), body: end >= 0 ? buffer.slice(end + 4) : '' });
      }
    });
  });
}

function proxiedGet(proxyPort: number, url: string, headers: Record<string, string> = {}): Promise<{ status: number; body: string; headers: http.IncomingHttpHeaders }> {
  return new Promise((resolve, reject) => {
    const req = http.request({ host: '127.0.0.1', port: proxyPort, method: 'GET', path: url, headers, agent: false }, (res) => {
      const chunks: Buffer[] = [];
      res.on('data', (c: Buffer) => chunks.push(c));
      res.on('end', () => resolve({ status: res.statusCode ?? 0, body: Buffer.concat(chunks).toString('utf8'), headers: res.headers }));
    });
    req.on('error', reject);
    req.end();
  });
}

beforeEach(() => {
  closers = [];
});

afterEach(async () => {
  for (const close of closers.reverse()) await close();
});

describe('CONNECT', () => {
  it('tunnels to an allowlisted host without asking', async () => {
    const h = await startHarness();
    const result = await connect(h.proxyPort, 'api.github.com:443', { 'Proxy-Authorization': basic(SESSION) });
    expect(result.status).toBe(200);
    expect(result.echoed).toBe('ping');
    expect(h.approvals).toHaveLength(0);
    expect(h.logs.some((l) => l.decision === 'allow' && l.via === 'static' && l.sessionId === SESSION)).toBe(true);
  });

  it('asks the sidecar for an unknown host and blocks on deny with the contract body', async () => {
    const h = await startHarness();
    const result = await connect(h.proxyPort, 'example.com:443', { 'Proxy-Authorization': basic(SESSION) });
    expect(result.status).toBe(403);
    expect(result.body).toBe('blocked by oet-agent-egress: example.com');
    expect(h.approvals).toEqual([
      expect.objectContaining({ source: 'egress', sessionId: SESSION, target: 'example.com:443' }),
    ]);
    // Denied before any DNS lookup: resolving first would leak the name.
    expect(h.resolved).not.toContain('example.com');
  });

  it('remembers an approve-for-session grant for that session only', async () => {
    const h = await startHarness();
    h.setDecision({ decision: 'approve', scope: 'session' });
    const first = await connect(h.proxyPort, 'example.com:443', { 'Proxy-Authorization': basic(SESSION) });
    expect(first.status).toBe(200);
    h.setDecision({ decision: 'deny', scope: 'once' });
    const second = await connect(h.proxyPort, 'example.com:443', { 'Proxy-Authorization': basic(SESSION) });
    expect(second.status).toBe(200);
    expect(h.approvals).toHaveLength(1);
    const other = await connect(h.proxyPort, 'example.com:443', { 'Proxy-Authorization': basic('01J9ZQ4Y8M3K2N7P5R6S8T0V1X') });
    expect(other.status).toBe(403);
    expect(h.approvals).toHaveLength(2);
  });

  it('refuses allowlisted names that resolve to private addresses (DNS rebinding)', async () => {
    const h = await startHarness({ 'github.com': ['172.20.0.3'] });
    const result = await connect(h.proxyPort, 'github.com:443');
    expect(result.status).toBe(403);
    expect(result.body).toBe('blocked by oet-agent-egress: github.com');
  });

  it('never asks for private IP literals', async () => {
    const h = await startHarness();
    const result = await connect(h.proxyPort, '169.254.169.254:443', { 'Proxy-Authorization': basic(SESSION) });
    expect(result.status).toBe(403);
    expect(h.approvals).toHaveLength(0);
  });

  it('lets the control plane through with the shared token and rejects a wrong token', async () => {
    const h = await startHarness();
    const ok = await connect(h.proxyPort, 'example.com:443', { 'X-Oet-Control-Token': TOKEN });
    expect(ok.status).toBe(200);
    const bad = await connect(h.proxyPort, 'github.com:443', { 'X-Oet-Control-Token': 'wrong-token-wrong-token-wrong-token' });
    expect(bad.status).toBe(403);
    expect(h.approvals).toHaveLength(0);
  });

  it('rejects malformed targets', async () => {
    const h = await startHarness();
    const result = await connect(h.proxyPort, 'localhost:443');
    expect(result.status).toBe(400);
  });
});

describe('plain HTTP forwarding', () => {
  it('forwards allowlisted absolute-form requests and strips proxy credentials', async () => {
    const h = await startHarness();
    const res = await proxiedGet(h.proxyPort, 'http://github.com/login?x=1', { 'Proxy-Authorization': basic(SESSION), 'X-Oet-Control-Token': TOKEN });
    expect(res.status).toBe(200);
    expect(res.body).toBe('upstream saw GET /login?x=1');
    expect(res.headers['x-seen-host']).toBe('github.com');
    expect(res.headers['x-seen-control']).toBe('');
  });

  it('blocks unknown hosts with 403 and the contract body', async () => {
    const h = await startHarness();
    const res = await proxiedGet(h.proxyPort, 'http://example.com/', { 'Proxy-Authorization': basic(SESSION) });
    expect(res.status).toBe(403);
    expect(res.body).toBe('blocked by oet-agent-egress: example.com');
  });

  it('refuses absolute https:// requests', async () => {
    const h = await startHarness();
    const res = await proxiedGet(h.proxyPort, 'https://github.com/');
    expect(res.status).toBe(400);
  });
});

describe('local endpoints', () => {
  it('serves /healthz', async () => {
    const h = await startHarness();
    const res = await proxiedGet(h.proxyPort, '/healthz');
    expect(res.status).toBe(200);
    expect(JSON.parse(res.body)).toEqual({ ok: true });
  });

  it('clears session grants only with the control token', async () => {
    const h = await startHarness();
    h.setDecision({ decision: 'approve', scope: 'session' });
    expect((await connect(h.proxyPort, 'example.com:443', { 'Proxy-Authorization': basic(SESSION) })).status).toBe(200);

    const denied = await new Promise<number>((resolve, reject) => {
      const req = http.request({ host: '127.0.0.1', port: h.proxyPort, method: 'DELETE', path: `/internal/sessions/${SESSION}`, agent: false }, (res) => {
        res.resume();
        resolve(res.statusCode ?? 0);
      });
      req.on('error', reject);
      req.end();
    });
    expect(denied).toBe(403);

    const cleared = await new Promise<number>((resolve, reject) => {
      const req = http.request(
        { host: '127.0.0.1', port: h.proxyPort, method: 'DELETE', path: `/internal/sessions/${SESSION}`, headers: { 'X-Oet-Control-Token': TOKEN }, agent: false },
        (res) => {
          res.resume();
          resolve(res.statusCode ?? 0);
        },
      );
      req.on('error', reject);
      req.end();
    });
    expect(cleared).toBe(200);

    h.setDecision({ decision: 'deny', scope: 'once' });
    expect((await connect(h.proxyPort, 'example.com:443', { 'Proxy-Authorization': basic(SESSION) })).status).toBe(403);
  });
});
