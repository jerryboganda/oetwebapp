// oet-agent-egress HTTP server: CONNECT tunnels + absolute-form plain-HTTP
// forwarding on :3128, plus two origin-form endpoints for the control plane:
//   GET    /healthz                    → { ok: true }
//   DELETE /internal/sessions/:id      → drop that session's dynamic grants (X-Oet-Control-Token)
//   DELETE /internal/sessions          → drop all dynamic grants (X-Oet-Control-Token)
import http from 'node:http';
import net from 'node:net';
import { lookup } from 'node:dns/promises';
import type { Duplex } from 'node:stream';
import type { ApprovalBroker, ApprovalResult } from './approvals.js';
import type { EgressConfig } from './config.js';
import type { SessionGrants } from './grants.js';
import {
  parseAbsoluteHttpUrl,
  parseConnectAuthority,
  parseProxyAuthorization,
  SESSION_ID_PATTERN,
  stripHopByHop,
} from './headers.js';
import type { Logger } from './log.js';
import { evaluateEgress, grantKey, isForbiddenAddress, STATIC_ALLOWLIST, type EgressKind } from './policy.js';
import { constantTimeEqual } from './secrets.js';

export interface EgressDeps {
  config: EgressConfig;
  broker: ApprovalBroker;
  grants: SessionGrants;
  log: Logger;
  /** DNS resolution (injectable for tests). Returns every address for the host. */
  resolveHost?: (host: string) => Promise<string[]>;
  /** TCP connect to a vetted address (injectable for tests). */
  connectTo?: (address: string, port: number) => net.Socket;
  allowlist?: readonly string[];
}

export async function defaultResolveHost(host: string): Promise<string[]> {
  const answers = await lookup(host, { all: true });
  return answers.map((answer) => answer.address);
}

export function blockedBody(host: string): string {
  return `blocked by oet-agent-egress: ${host}`;
}

type Authorization =
  | { ok: true; addresses: string[] }
  | { ok: false; status: number; body: string };

export function createEgressServer(deps: EgressDeps): http.Server {
  const { config, broker, grants, log } = deps;
  const resolveHost = deps.resolveHost ?? defaultResolveHost;
  const connectTo = deps.connectTo ?? ((address: string, port: number) => net.connect({ host: address, port }));
  const allowlist = deps.allowlist ?? STATIC_ALLOWLIST;
  const policy = {
    allowlist,
    connectPorts: config.connectPorts,
    httpPorts: config.httpPorts,
    hasGrant: (sessionId: string, host: string, port: number) => grants.has(sessionId, grantKey(host, port)),
  };

  function isControl(headers: http.IncomingHttpHeaders): 'yes' | 'no' | 'invalid' {
    const value = headers['x-oet-control-token'];
    if (value === undefined) return 'no';
    return typeof value === 'string' && constantTimeEqual(value, config.proxyToken) ? 'yes' : 'invalid';
  }

  async function authorize(
    kind: EgressKind,
    host: string,
    port: number,
    headers: http.IncomingHttpHeaders,
    method: string,
  ): Promise<Authorization> {
    const started = Date.now();
    const auth = parseProxyAuthorization(headers['proxy-authorization']);
    const base = { kind, method, host, port, sessionId: auth.sessionId, authProblem: auth.problem };
    const refused = (reasons: string[], extra: Record<string, unknown> = {}, status = 403): Authorization => {
      log.event('decision', { ...base, decision: 'deny', reasons, durationMs: Date.now() - started, ...extra });
      return { ok: false, status, body: status === 403 ? blockedBody(host) : `oet-agent-egress: ${reasons.join('; ')}` };
    };

    const control = isControl(headers);
    if (control === 'invalid') return refused(['invalid X-Oet-Control-Token']);

    const verdict = evaluateEgress({ host, port, kind, sessionId: auth.sessionId, control: control === 'yes' }, policy);
    if (verdict.decision === 'deny') return refused(verdict.reasons);

    let via: string;
    let approval: ApprovalResult | undefined;
    if (verdict.decision === 'approval') {
      // No DNS lookup happens before the owner decides: resolving an arbitrary
      // name first would itself be a DNS exfiltration channel.
      approval = await broker.request(
        {
          source: 'egress',
          sessionId: auth.sessionId,
          summary: `${kind === 'connect' ? 'CONNECT' : method} ${host}:${port}`,
          target: `${host}:${port}`,
          reasons: verdict.reasons,
          details: { host, port, kind, method },
        },
        `${auth.sessionId ?? 'system'}|${grantKey(host, port)}`,
      );
      if (approval.decision !== 'approve') {
        return refused(verdict.reasons, { via: 'approval', approval });
      }
      if (approval.scope === 'session' && auth.sessionId !== null) {
        grants.add(auth.sessionId, grantKey(host, port));
      }
      via = 'approval';
    } else {
      via = verdict.via;
    }

    let addresses: string[];
    if (net.isIP(host) !== 0) {
      addresses = [host];
    } else {
      try {
        addresses = await resolveHost(host);
      } catch (err) {
        const code = (err as NodeJS.ErrnoException).code ?? 'EDNS';
        return refused([`DNS resolution failed (${code})`], { via, approval }, 502);
      }
    }
    const permitted = addresses.filter((address) => !isForbiddenAddress(address));
    if (permitted.length === 0) {
      return refused(['host resolves only to private, loopback or reserved addresses'], { via, approval });
    }
    log.event('decision', {
      ...base,
      decision: 'allow',
      via,
      approval,
      addresses: permitted.slice(0, 4),
      durationMs: Date.now() - started,
    });
    return { ok: true, addresses: permitted.slice(0, 4) };
  }

  function connectOnce(address: string, port: number): Promise<net.Socket | null> {
    return new Promise((resolve) => {
      const socket = connectTo(address, port);
      const onError = (): void => {
        clearTimeout(timer);
        socket.destroy();
        resolve(null);
      };
      const timer = setTimeout(onError, config.connectTimeoutMs);
      socket.once('error', onError);
      socket.once('connect', () => {
        clearTimeout(timer);
        socket.removeListener('error', onError);
        resolve(socket);
      });
    });
  }

  async function openUpstream(addresses: string[], port: number): Promise<net.Socket | null> {
    for (const address of addresses) {
      const socket = await connectOnce(address, port);
      if (socket) return socket;
    }
    return null;
  }

  function writeRaw(socket: Duplex, status: number, body: string): void {
    const payload = Buffer.from(body, 'utf8');
    const head =
      `HTTP/1.1 ${status} ${http.STATUS_CODES[status] ?? 'Error'}\r\n` +
      'Content-Type: text/plain; charset=utf-8\r\n' +
      `Content-Length: ${payload.length}\r\n` +
      'Connection: close\r\n\r\n';
    if (socket.writable) {
      socket.end(Buffer.concat([Buffer.from(head, 'latin1'), payload]));
    } else {
      socket.destroy();
    }
  }

  function sendText(res: http.ServerResponse, status: number, body: string): void {
    if (res.headersSent) {
      res.destroy();
      return;
    }
    res.writeHead(status, { 'content-type': 'text/plain; charset=utf-8', 'content-length': Buffer.byteLength(body) });
    res.end(body);
  }

  function sendJson(res: http.ServerResponse, status: number, body: unknown): void {
    const payload = JSON.stringify(body);
    res.writeHead(status, { 'content-type': 'application/json', 'content-length': Buffer.byteLength(payload) });
    res.end(payload);
  }

  function wireTunnel(client: Duplex, upstream: net.Socket): void {
    const destroyBoth = (): void => {
      client.destroy();
      upstream.destroy();
    };
    client.on('error', destroyBoth);
    upstream.on('error', destroyBoth);
    // pipe() ends the other side when one side ends; on a bare 'close' end()
    // (not destroy()) the peer so already-buffered bytes still flush.
    client.on('close', () => {
      if (!upstream.destroyed) upstream.end();
    });
    upstream.on('close', () => {
      if (!client.destroyed) client.end();
    });
    upstream.setTimeout(config.tunnelIdleTimeoutMs, destroyBoth);
    if (client instanceof net.Socket) client.setTimeout(config.tunnelIdleTimeoutMs, destroyBoth);
    client.pipe(upstream);
    upstream.pipe(client);
  }

  async function handleConnect(req: http.IncomingMessage, client: Duplex, head: Buffer): Promise<void> {
    client.on('error', () => client.destroy());
    const target = parseConnectAuthority(req.url ?? '');
    if (target === null) {
      log.event('decision', { kind: 'connect', method: 'CONNECT', decision: 'deny', reasons: ['malformed CONNECT target'], target: (req.url ?? '').slice(0, 300) });
      writeRaw(client, 400, 'oet-agent-egress: malformed CONNECT target');
      return;
    }
    const authz = await authorize('connect', target.host, target.port, req.headers, 'CONNECT');
    if (!authz.ok) {
      writeRaw(client, authz.status, authz.body);
      return;
    }
    if (client.destroyed) return;
    const upstream = await openUpstream(authz.addresses, target.port);
    if (upstream === null) {
      log.event('upstream_error', { kind: 'connect', host: target.host, port: target.port, error: 'connect failed' });
      writeRaw(client, 502, `oet-agent-egress: could not connect to ${target.host}:${target.port}`);
      return;
    }
    if (client.destroyed) {
      upstream.destroy();
      return;
    }
    client.write('HTTP/1.1 200 Connection Established\r\nProxy-Agent: oet-agent-egress\r\n\r\n');
    if (head.length > 0) upstream.write(head);
    wireTunnel(client, upstream);
  }

  function handleLocal(req: http.IncomingMessage, res: http.ServerResponse): void {
    const method = (req.method ?? 'GET').toUpperCase();
    const path = (req.url ?? '/').split('?')[0] ?? '/';
    if (method === 'GET' && path === '/healthz') {
      sendJson(res, 200, { ok: true });
      return;
    }
    if (method === 'DELETE' && (path === '/internal/sessions' || path.startsWith('/internal/sessions/'))) {
      if (isControl(req.headers) !== 'yes') {
        log.event('internal_denied', { method, path });
        sendJson(res, 403, { error: { code: 'forbidden', message: 'control token required' } });
        return;
      }
      if (path === '/internal/sessions') {
        const cleared = grants.clearAll();
        log.event('grants_cleared', { scope: 'all', cleared });
        sendJson(res, 200, { cleared });
        return;
      }
      let sessionId = '';
      try {
        sessionId = decodeURIComponent(path.slice('/internal/sessions/'.length));
      } catch {
        sessionId = '';
      }
      if (!SESSION_ID_PATTERN.test(sessionId)) {
        sendJson(res, 400, { error: { code: 'bad_request', message: 'invalid session id' } });
        return;
      }
      const cleared = grants.clearSession(sessionId);
      log.event('grants_cleared', { scope: 'session', sessionId, cleared });
      sendJson(res, 200, { cleared });
      return;
    }
    sendJson(res, 404, { error: { code: 'not_found', message: 'not found' } });
  }

  async function handleRequest(req: http.IncomingMessage, res: http.ServerResponse): Promise<void> {
    const url = req.url ?? '';
    if (url.startsWith('/')) {
      handleLocal(req, res);
      return;
    }
    const method = (req.method ?? 'GET').toUpperCase();
    const target = parseAbsoluteHttpUrl(url);
    if (target === null) {
      req.resume();
      log.event('decision', { kind: 'http', method, decision: 'deny', reasons: ['only absolute http:// URLs can be forwarded'], target: url.slice(0, 300) });
      sendText(res, 400, 'oet-agent-egress: only absolute http:// URLs can be forwarded; use CONNECT for https');
      return;
    }
    const authz = await authorize('http', target.host, target.port, req.headers, method);
    if (!authz.ok) {
      req.resume();
      sendText(res, authz.status, authz.body);
      return;
    }
    const socket = await openUpstream(authz.addresses, target.port);
    if (socket === null) {
      req.resume();
      log.event('upstream_error', { kind: 'http', host: target.host, port: target.port, error: 'connect failed' });
      sendText(res, 502, `oet-agent-egress: could not connect to ${target.host}:${target.port}`);
      return;
    }
    const headers = stripHopByHop(req.headers);
    headers.host = target.hostHeader;
    // No agent: the request runs on the already-connected, vetted socket and
    // is not pooled (Node then sends Connection: close upstream).
    const upstreamReq = http.request({
      method,
      path: target.path,
      headers,
      createConnection: () => socket,
    });
    upstreamReq.setTimeout(config.tunnelIdleTimeoutMs, () => upstreamReq.destroy(new Error('upstream idle timeout')));
    upstreamReq.on('response', (upstreamRes) => {
      res.writeHead(upstreamRes.statusCode ?? 502, stripHopByHop(upstreamRes.headers));
      upstreamRes.pipe(res);
      upstreamRes.on('error', () => res.destroy());
    });
    upstreamReq.on('error', (err) => {
      log.event('upstream_error', { kind: 'http', host: target.host, port: target.port, error: err.message });
      sendText(res, 502, `oet-agent-egress: upstream error for ${target.host}`);
    });
    res.on('close', () => {
      if (!res.writableFinished) upstreamReq.destroy();
    });
    req.pipe(upstreamReq);
  }

  const server = http.createServer();
  // requestTimeout 0: long plain-HTTP uploads are bounded by the idle timeout instead.
  server.requestTimeout = 0;
  server.headersTimeout = 30_000;
  server.keepAliveTimeout = 5_000;
  server.maxConnections = config.maxConnections;

  server.on('request', (req: http.IncomingMessage, res: http.ServerResponse) => {
    handleRequest(req, res).catch((err: unknown) => {
      log.event('internal_error', { error: err instanceof Error ? err.message : String(err) });
      sendText(res, 500, 'oet-agent-egress: internal error');
    });
  });
  server.on('connect', (req: http.IncomingMessage, socket: Duplex, head: Buffer) => {
    handleConnect(req, socket, head).catch((err: unknown) => {
      log.event('internal_error', { error: err instanceof Error ? err.message : String(err) });
      writeRaw(socket, 500, 'oet-agent-egress: internal error');
    });
  });
  server.on('upgrade', (req: http.IncomingMessage, socket: Duplex) => {
    log.event('decision', { kind: 'http', method: req.method, decision: 'deny', reasons: ['protocol upgrade through the plain-HTTP proxy'], target: (req.url ?? '').slice(0, 300) });
    writeRaw(socket, 403, 'oet-agent-egress: protocol upgrades are not proxied; use CONNECT');
  });
  server.on('clientError', (_err: Error, socket: Duplex) => {
    if (socket.writable) {
      socket.end('HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n');
    } else {
      socket.destroy();
    }
  });
  return server;
}
