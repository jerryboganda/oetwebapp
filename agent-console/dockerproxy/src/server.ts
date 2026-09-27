// oet-agent-dockerproxy HTTP server on :2375 — the only holder of
// /var/run/docker.sock in the console stack (CONTRACT.md §6).
//
// Agent requests (X-Oet-Agent-Session attribution) go through the policy:
// resolve names → evaluate → (approval callback) → forward with the full id →
// optional response transform. Control-plane requests (valid
// X-Oet-Control-Token) bypass policy and transforms. Streaming endpoints
// (logs -f, stats, wait, events) are piped without buffering; attach and exec
// start use HTTP connection hijacking (Upgrade: tcp) and are tunnelled raw
// only after the daemon answered 101 (see tunnel()).
//
// Local endpoints: GET /healthz, DELETE /internal/sessions[/:id] (control token).
import http from 'node:http';
import net from 'node:net';
import type { Duplex } from 'node:stream';
import type { ApprovalBroker } from './approvals.js';
import type { DockerProxyConfig } from './config.js';
import { applyJsonTransform, createEventFilter } from './filters.js';
import type { SessionGrants, TtlSet } from './grants.js';
import type { Logger } from './log.js';
import { evaluateParsed, type PolicyDecision, type ResponseTransform } from './policy.js';
import { buildForwardPath, parseDockerPath, type DockerRoute } from './routes.js';
import { constantTimeEqual } from './secrets.js';
import { resolveTargets, type DockerUpstream, type ResolveOutcome, type UpstreamResponse } from './upstream.js';
import { asRecord, str } from './util.js';

export interface DockerProxyDeps {
  config: DockerProxyConfig;
  broker: ApprovalBroker;
  grants: SessionGrants;
  execApprovals: TtlSet;
  upstream: DockerUpstream;
  log: Logger;
}

export const SESSION_ID_PATTERN = /^[A-Za-z0-9_-]{1,64}$/;
const INTERNAL_HEADERS = new Set(['x-oet-agent-session', 'x-oet-control-token']);
const HOP_BY_HOP = new Set([
  'connection',
  'keep-alive',
  'proxy-authenticate',
  'proxy-authorization',
  'proxy-connection',
  'te',
  'trailer',
  'transfer-encoding',
  'upgrade',
]);

interface Identity {
  sessionId: string | null;
  control: boolean;
  badControl: boolean;
}

class BodyTooLargeError extends Error {}

/** attach / exec start bodies are tiny JSON ({"Detach":false,"Tty":true}). */
const MAX_UPGRADE_BODY_BYTES = 64 * 1024;
const UPGRADE_BODY_TIMEOUT_MS = 30_000;

function sanitizeHeaders(headers: http.IncomingHttpHeaders, drop: ReadonlySet<string>): http.OutgoingHttpHeaders {
  const out: http.OutgoingHttpHeaders = {};
  for (const [name, value] of Object.entries(headers)) {
    if (value === undefined) continue;
    const key = name.toLowerCase();
    if (HOP_BY_HOP.has(key) || drop.has(key)) continue;
    out[key] = value;
  }
  return out;
}

const NO_EXTRA = new Set<string>();

function needsJsonBody(method: string, route: DockerRoute): boolean {
  if (method !== 'POST') return false;
  if (route.kind === 'containers-create' || route.kind === 'networks-create') return true;
  if (route.kind === 'container' && route.action === 'exec') return true;
  return route.kind === 'network' && (route.action === 'connect' || route.action === 'disconnect');
}

function readBody(req: http.IncomingMessage, limit: number): Promise<Buffer> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    let size = 0;
    let failed = false;
    req.on('data', (chunk: Buffer) => {
      if (failed) return;
      size += chunk.length;
      if (size > limit) {
        failed = true;
        reject(new BodyTooLargeError(`request body exceeds ${limit} bytes`));
        return;
      }
      chunks.push(chunk);
    });
    req.on('end', () => {
      if (!failed) resolve(Buffer.concat(chunks));
    });
    req.on('error', (err) => {
      if (!failed) {
        failed = true;
        reject(err);
      }
    });
  });
}

export function createDockerProxyServer(deps: DockerProxyDeps): http.Server {
  const { config, broker, grants, execApprovals, upstream, log } = deps;

  function identify(headers: http.IncomingHttpHeaders): Identity {
    const control = headers['x-oet-control-token'];
    const session = headers['x-oet-agent-session'];
    const sessionText = typeof session === 'string' ? session.trim() : '';
    return {
      sessionId: SESSION_ID_PATTERN.test(sessionText) ? sessionText : null,
      control: typeof control === 'string' && constantTimeEqual(control, config.proxyToken),
      badControl: control !== undefined && !(typeof control === 'string' && constantTimeEqual(control, config.proxyToken)),
    };
  }

  function sendDockerError(res: http.ServerResponse, status: number, message: string): void {
    if (res.headersSent) {
      res.destroy();
      return;
    }
    const payload = JSON.stringify({ message });
    res.writeHead(status, { 'content-type': 'application/json', 'content-length': Buffer.byteLength(payload) });
    res.end(payload);
  }

  function sendJson(res: http.ServerResponse, status: number, body: unknown): void {
    const payload = JSON.stringify(body);
    res.writeHead(status, { 'content-type': 'application/json', 'content-length': Buffer.byteLength(payload) });
    res.end(payload);
  }

  function relayFailure(res: http.ServerResponse, failure: UpstreamResponse): void {
    const headers = sanitizeHeaders(failure.headers, NO_EXTRA);
    headers['content-length'] = String(failure.body.length);
    res.writeHead(failure.status, headers);
    res.end(failure.body);
  }

  function rawResponse(socket: Duplex, status: number, message: string): void {
    const payload = Buffer.from(JSON.stringify({ message }), 'utf8');
    const head =
      `HTTP/1.1 ${status} ${http.STATUS_CODES[status] ?? 'Error'}\r\n` +
      'Content-Type: application/json\r\n' +
      `Content-Length: ${payload.length}\r\n` +
      'Connection: close\r\n\r\n';
    if (socket.writable) socket.end(Buffer.concat([Buffer.from(head, 'latin1'), payload]));
    else socket.destroy();
  }

  function logDecision(fields: Record<string, unknown>): void {
    log.event('decision', fields);
  }

  /** Applies approvals / session grants to a policy decision. */
  async function settle(pd: PolicyDecision, who: Identity, base: Record<string, unknown>): Promise<{ allowed: boolean; message: string }> {
    const started = Date.now();
    const common = { ...base, rule: pd.rule, summary: pd.summary, target: pd.target, reasons: pd.reasons };
    if (pd.decision === 'allow') {
      logDecision({ ...common, decision: 'allow', via: 'policy' });
      return { allowed: true, message: '' };
    }
    if (pd.decision === 'deny') {
      logDecision({ ...common, decision: 'deny', via: 'policy' });
      return { allowed: false, message: `blocked by oet-agent-dockerproxy: ${pd.reasons.join('; ')}` };
    }
    if (pd.grantKey !== undefined && who.sessionId !== null && grants.has(who.sessionId, pd.grantKey)) {
      logDecision({ ...common, decision: 'allow', via: 'session_grant', grantKey: pd.grantKey });
      return { allowed: true, message: '' };
    }
    const result = await broker.request({
      source: 'docker',
      sessionId: who.sessionId,
      summary: pd.summary,
      target: pd.target,
      reasons: pd.reasons,
      details: { ...(pd.details ?? {}), method: base.method, path: base.path, rule: pd.rule },
    });
    const approval = { decision: result.decision, scope: result.scope, error: result.error, waitedMs: Date.now() - started };
    if (result.decision === 'approve') {
      if (result.scope === 'session' && pd.grantKey !== undefined && who.sessionId !== null) {
        grants.add(who.sessionId, pd.grantKey);
      }
      logDecision({ ...common, decision: 'allow', via: 'approval', approval });
      return { allowed: true, message: '' };
    }
    logDecision({ ...common, decision: 'deny', via: 'approval', approval });
    return { allowed: false, message: `blocked by oet-agent-dockerproxy: not approved (${pd.summary})` };
  }

  function forward(
    req: http.IncomingMessage,
    res: http.ServerResponse,
    method: string,
    path: string,
    body: Buffer,
    transform: ResponseTransform | undefined,
  ): void {
    const headers = sanitizeHeaders(req.headers, INTERNAL_HEADERS);
    delete headers['content-length'];
    if (body.length > 0 || method === 'POST' || method === 'PUT') headers['content-length'] = String(body.length);
    const upstreamReq = http.request({ socketPath: config.socketPath, method, path, headers });

    upstreamReq.on('response', (upstreamRes) => {
      const status = upstreamRes.statusCode ?? 502;
      const ok = status >= 200 && status < 300;
      const outHeaders = sanitizeHeaders(upstreamRes.headers, NO_EXTRA);

      if (ok && transform !== undefined && transform !== 'events') {
        const chunks: Buffer[] = [];
        let size = 0;
        upstreamRes.on('data', (chunk: Buffer) => {
          size += chunk.length;
          if (size > config.maxTransformBytes) {
            upstreamRes.destroy();
            sendDockerError(res, 502, 'oet-agent-dockerproxy: response too large to filter');
            return;
          }
          chunks.push(chunk);
        });
        upstreamRes.on('end', () => {
          if (res.headersSent) return;
          let json: unknown;
          try {
            json = JSON.parse(Buffer.concat(chunks).toString('utf8'));
          } catch {
            sendDockerError(res, 502, 'oet-agent-dockerproxy: unexpected non-JSON response from the Docker daemon');
            return;
          }
          if (transform === 'exec-create') {
            const id = str(asRecord(json).Id);
            if (/^[A-Fa-f0-9]{64}$/.test(id)) execApprovals.add(id);
          }
          const payload = Buffer.from(JSON.stringify(applyJsonTransform(transform, json)), 'utf8');
          outHeaders['content-type'] = 'application/json';
          outHeaders['content-length'] = String(payload.length);
          res.writeHead(status, outHeaders);
          res.end(payload);
        });
        upstreamRes.on('error', () => res.destroy());
        return;
      }

      if (ok && transform === 'events') {
        delete outHeaders['content-length'];
        res.writeHead(status, outHeaders);
        res.flushHeaders();
        upstreamRes.pipe(createEventFilter()).pipe(res);
        upstreamRes.on('error', () => res.destroy());
        return;
      }

      res.writeHead(status, outHeaders);
      res.flushHeaders();
      upstreamRes.pipe(res);
      upstreamRes.on('error', () => res.destroy());
    });
    upstreamReq.on('error', (err) => {
      log.event('upstream_error', { method, path, error: err.message });
      sendDockerError(res, 502, `oet-agent-dockerproxy: Docker daemon unreachable (${err.message})`);
    });
    // Client went away (e.g. Ctrl-C on `docker logs -f`): stop the daemon stream.
    res.on('close', () => upstreamReq.destroy());
    upstreamReq.end(body.length > 0 ? body : undefined);
  }

  /**
   * Reads exactly `length` request-body bytes of a hijack request from the raw
   * client socket (`head` holds what arrived with the headers). Bytes past the
   * body are returned as `rest`: they are only ever sent to the daemon AFTER it
   * answered 101, never while it still parses HTTP.
   */
  function readUpgradeBody(client: Duplex, head: Buffer, length: number): Promise<{ body: Buffer; rest: Buffer }> {
    if (head.length >= length) {
      return Promise.resolve({ body: head.subarray(0, length), rest: head.subarray(length) });
    }
    return new Promise((resolve, reject) => {
      const chunks: Buffer[] = [head];
      let size = head.length;
      const cleanup = (): void => {
        clearTimeout(timer);
        client.removeListener('data', onData);
        client.removeListener('end', onEnd);
        client.removeListener('close', onEnd);
        client.pause();
      };
      const onData = (chunk: Buffer): void => {
        chunks.push(chunk);
        size += chunk.length;
        if (size >= length) {
          cleanup();
          const all = Buffer.concat(chunks);
          resolve({ body: all.subarray(0, length), rest: all.subarray(length) });
        }
      };
      const onEnd = (): void => {
        cleanup();
        reject(new Error('client closed before sending the request body'));
      };
      const timer = setTimeout(() => {
        cleanup();
        reject(new Error('timed out reading the request body'));
      }, UPGRADE_BODY_TIMEOUT_MS);
      client.on('data', onData);
      client.on('end', onEnd);
      client.on('close', onEnd);
    });
  }

  /**
   * Hijacked attach / exec start. The request is re-issued with node:http so
   * the daemon's answer is parsed: only a real `101` switches the connection to
   * a raw bidirectional stream. Any other answer (e.g. 409 attaching to a paused
   * container, a detached exec start) is relayed and BOTH sockets are closed,
   * so bytes the client pipelined behind the request can never reach the
   * daemon's HTTP parser as a second, unfiltered API call.
   */
  function tunnel(req: http.IncomingMessage, client: Duplex, path: string, body: Buffer, rest: Buffer): void {
    const headers = sanitizeHeaders(req.headers, INTERNAL_HEADERS);
    delete headers['content-length'];
    headers.connection = 'Upgrade';
    headers.upgrade = 'tcp';
    headers['content-length'] = String(body.length);
    const method = req.method ?? 'POST';
    const upstreamReq = http.request({ socketPath: config.socketPath, method, path, headers, agent: false });
    let settled = false;
    // Held explicitly: once a non-upgraded response has ended, node detaches
    // the socket from upstreamReq, so upstreamReq.destroy() alone would leave
    // the (keep-alive) daemon connection open.
    let upstreamSocket: net.Socket | undefined;
    upstreamReq.on('socket', (s: net.Socket) => {
      upstreamSocket = s;
    });
    const closeUpstream = (): void => {
      upstreamReq.destroy();
      upstreamSocket?.destroy();
    };

    upstreamReq.on('upgrade', (res: http.IncomingMessage, daemon: net.Socket, daemonHead: Buffer) => {
      settled = true;
      if (client.destroyed) {
        daemon.destroy();
        return;
      }
      const lines = [`HTTP/1.1 ${res.statusCode ?? 101} ${res.statusMessage ?? 'UPGRADED'}`];
      for (let i = 0; i + 1 < res.rawHeaders.length; i += 2) {
        lines.push(`${res.rawHeaders[i] ?? ''}: ${res.rawHeaders[i + 1] ?? ''}`);
      }
      client.write(`${lines.join('\r\n')}\r\n\r\n`);
      if (daemonHead.length > 0) client.write(daemonHead);
      if (rest.length > 0) daemon.write(rest);
      const destroyBoth = (): void => {
        client.destroy();
        daemon.destroy();
      };
      daemon.on('error', destroyBoth);
      client.on('error', destroyBoth);
      // pipe() ends the peer when one side ends; on a bare 'close' end() (not
      // destroy()) the peer so already-buffered stream output still flushes.
      daemon.on('close', () => {
        if (!client.destroyed) client.end();
      });
      client.on('close', () => {
        if (!daemon.destroyed) daemon.end();
      });
      daemon.pipe(client);
      client.pipe(daemon);
    });

    upstreamReq.on('response', (res: http.IncomingMessage) => {
      settled = true;
      log.event('upstream_not_upgraded', { method, path, status: res.statusCode });
      const lines = [`HTTP/1.1 ${res.statusCode ?? 502} ${res.statusMessage ?? ''}`.trimEnd()];
      for (let i = 0; i + 1 < res.rawHeaders.length; i += 2) {
        const name = res.rawHeaders[i] ?? '';
        if (HOP_BY_HOP.has(name.toLowerCase()) || name.toLowerCase() === 'content-length') continue;
        lines.push(`${name}: ${res.rawHeaders[i + 1] ?? ''}`);
      }
      lines.push('Connection: close');
      if (!client.writable) {
        closeUpstream();
        client.destroy();
        return;
      }
      client.write(`${lines.join('\r\n')}\r\n\r\n`);
      // Body is delimited by closing the connection (Content-Length / chunking dropped above).
      res.on('data', (chunk: Buffer) => {
        if (client.writable) client.write(chunk);
      });
      res.on('end', () => {
        // Nothing more is ever read from this client connection.
        client.end(() => client.destroy());
        closeUpstream();
      });
      res.on('error', () => {
        client.destroy();
        closeUpstream();
      });
    });

    upstreamReq.on('error', (err) => {
      log.event('upstream_error', { method, path, error: err.message });
      if (!settled) rawResponse(client, 502, `oet-agent-dockerproxy: Docker daemon unreachable (${err.message})`);
      else client.destroy();
    });
    client.on('error', closeUpstream);
    upstreamReq.end(body);
  }

  async function handleRequest(req: http.IncomingMessage, res: http.ServerResponse): Promise<void> {
    const method = (req.method ?? 'GET').toUpperCase();
    const url = req.url ?? '/';
    const pathOnly = url.split('?')[0] ?? url;

    if (method === 'GET' && pathOnly === '/healthz') {
      try {
        const ping = await upstream.get('/_ping');
        sendJson(res, ping.status === 200 ? 200 : 503, { ok: ping.status === 200 });
      } catch {
        sendJson(res, 503, { ok: false });
      }
      return;
    }
    if (pathOnly === '/internal/sessions' || pathOnly.startsWith('/internal/sessions/')) {
      handleInternal(req, res, method, pathOnly);
      return;
    }

    const who = identify(req.headers);
    const base: Record<string, unknown> = { method, path: url.slice(0, 500), sessionId: who.sessionId };
    if (who.badControl) {
      req.resume();
      logDecision({ ...base, decision: 'deny', via: 'control', reasons: ['invalid X-Oet-Control-Token'] });
      sendDockerError(res, 403, 'blocked by oet-agent-dockerproxy: invalid control token');
      return;
    }

    let body: Buffer;
    try {
      body = await readBody(req, config.maxRequestBodyBytes);
    } catch (err) {
      logDecision({ ...base, decision: 'deny', via: 'policy', reasons: [err instanceof Error ? err.message : 'request body error'] });
      res.setHeader('connection', 'close');
      sendDockerError(res, 413, 'blocked by oet-agent-dockerproxy: request body too large');
      res.once('finish', () => req.destroy());
      return;
    }

    if (who.control) {
      logDecision({ ...base, decision: 'allow', via: 'control' });
      forward(req, res, method, url, body, undefined);
      return;
    }

    const parsed = parseDockerPath(method, url);
    if (!parsed.ok) {
      logDecision({ ...base, decision: 'deny', via: 'policy', rule: 5, reasons: [parsed.error] });
      sendDockerError(res, 400, `blocked by oet-agent-dockerproxy: ${parsed.error}`);
      return;
    }

    let json: unknown;
    if (needsJsonBody(method, parsed.value.route)) {
      if (body.length > 0) {
        try {
          json = JSON.parse(body.toString('utf8'));
        } catch {
          logDecision({ ...base, decision: 'deny', via: 'policy', rule: 5, reasons: ['request body is not valid JSON'] });
          sendDockerError(res, 400, 'blocked by oet-agent-dockerproxy: request body is not valid JSON');
          return;
        }
      } else {
        json = {};
      }
    }

    let outcome: ResolveOutcome;
    try {
      outcome = await resolveTargets(upstream, parsed.value.route, json);
    } catch (err) {
      log.event('upstream_error', { ...base, error: err instanceof Error ? err.message : String(err) });
      sendDockerError(res, 502, 'oet-agent-dockerproxy: Docker daemon unreachable');
      return;
    }
    if (outcome.failure !== undefined) {
      logDecision({ ...base, decision: 'relay', via: 'resolution', status: outcome.failure.status });
      relayFailure(res, outcome.failure);
      return;
    }

    const execPreApproved = parsed.value.route.kind === 'exec' && outcome.fullId !== undefined && execApprovals.has(outcome.fullId);
    const pd = evaluateParsed(method, parsed.value, { method, path: url, body: json, resolved: outcome.resolution, execPreApproved });
    const settled = await settle(pd, who, base);
    if (!settled.allowed) {
      sendDockerError(res, 403, settled.message);
      return;
    }
    forward(req, res, method, buildForwardPath(parsed.value, outcome.fullId), body, pd.transform);
  }

  function handleInternal(req: http.IncomingMessage, res: http.ServerResponse, method: string, pathOnly: string): void {
    req.resume();
    const who = identify(req.headers);
    if (method !== 'DELETE' || !who.control) {
      log.event('internal_denied', { method, path: pathOnly });
      sendJson(res, 403, { error: { code: 'forbidden', message: 'control token required' } });
      return;
    }
    if (pathOnly === '/internal/sessions') {
      const cleared = grants.clearAll();
      execApprovals.clear();
      log.event('grants_cleared', { scope: 'all', cleared });
      sendJson(res, 200, { cleared });
      return;
    }
    let sessionId: string;
    try {
      sessionId = decodeURIComponent(pathOnly.slice('/internal/sessions/'.length));
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
  }

  async function handleUpgrade(req: http.IncomingMessage, socket: Duplex, head: Buffer): Promise<void> {
    socket.on('error', () => socket.destroy());
    const method = (req.method ?? 'GET').toUpperCase();
    const url = req.url ?? '/';
    const who = identify(req.headers);
    const base: Record<string, unknown> = { method, path: url.slice(0, 500), sessionId: who.sessionId, upgrade: true };
    if (who.badControl) {
      logDecision({ ...base, decision: 'deny', via: 'control', reasons: ['invalid X-Oet-Control-Token'] });
      rawResponse(socket, 403, 'blocked by oet-agent-dockerproxy: invalid control token');
      return;
    }

    // Hijack requests carry a small JSON body (exec start) or none (attach).
    // Chunked bodies are refused so the body boundary is never ambiguous.
    if (req.headers['transfer-encoding'] !== undefined) {
      logDecision({ ...base, decision: 'deny', via: 'policy', rule: 5, reasons: ['chunked upgrade request'] });
      rawResponse(socket, 400, 'blocked by oet-agent-dockerproxy: chunked upgrade requests are not supported');
      return;
    }
    const lengthHeader = req.headers['content-length'] ?? '0';
    const bodyLength = /^[0-9]{1,9}$/.test(lengthHeader) ? Number(lengthHeader) : -1;
    if (bodyLength < 0 || bodyLength > MAX_UPGRADE_BODY_BYTES) {
      logDecision({ ...base, decision: 'deny', via: 'policy', rule: 5, reasons: ['invalid or oversized upgrade request body'] });
      rawResponse(socket, 413, 'blocked by oet-agent-dockerproxy: invalid or oversized upgrade request body');
      return;
    }
    let upgradeBody: { body: Buffer; rest: Buffer };
    try {
      upgradeBody = await readUpgradeBody(socket, head, bodyLength);
    } catch (err) {
      logDecision({ ...base, decision: 'deny', via: 'policy', rule: 5, reasons: [err instanceof Error ? err.message : 'request body error'] });
      rawResponse(socket, 400, 'blocked by oet-agent-dockerproxy: incomplete upgrade request body');
      return;
    }

    if (who.control) {
      logDecision({ ...base, decision: 'allow', via: 'control' });
      tunnel(req, socket, url, upgradeBody.body, upgradeBody.rest);
      return;
    }
    const parsed = parseDockerPath(method, url);
    if (!parsed.ok) {
      logDecision({ ...base, decision: 'deny', via: 'policy', rule: 5, reasons: [parsed.error] });
      rawResponse(socket, 400, `blocked by oet-agent-dockerproxy: ${parsed.error}`);
      return;
    }
    const route = parsed.value.route;
    const hijackable =
      method === 'POST' &&
      ((route.kind === 'container' && route.action === 'attach') || (route.kind === 'exec' && route.action === 'start'));
    if (!hijackable) {
      logDecision({ ...base, decision: 'deny', via: 'policy', rule: 5, reasons: ['connection upgrade is only proxied for attach and exec start'] });
      rawResponse(socket, 403, 'blocked by oet-agent-dockerproxy: connection upgrade is only proxied for attach and exec start');
      return;
    }
    let outcome: ResolveOutcome;
    try {
      outcome = await resolveTargets(upstream, route, undefined);
    } catch (err) {
      log.event('upstream_error', { ...base, error: err instanceof Error ? err.message : String(err) });
      rawResponse(socket, 502, 'oet-agent-dockerproxy: Docker daemon unreachable');
      return;
    }
    if (outcome.failure !== undefined) {
      const message = str(asRecord(outcome.failure.json).message) || `Docker daemon returned HTTP ${outcome.failure.status}`;
      logDecision({ ...base, decision: 'relay', via: 'resolution', status: outcome.failure.status });
      rawResponse(socket, outcome.failure.status, message);
      return;
    }
    const execPreApproved = route.kind === 'exec' && outcome.fullId !== undefined && execApprovals.has(outcome.fullId);
    const pd = evaluateParsed(method, parsed.value, { method, path: url, resolved: outcome.resolution, execPreApproved });
    const settled = await settle(pd, who, base);
    if (!settled.allowed) {
      rawResponse(socket, 403, settled.message);
      return;
    }
    if (socket.destroyed) return;
    tunnel(req, socket, buildForwardPath(parsed.value, outcome.fullId), upgradeBody.body, upgradeBody.rest);
  }

  const server = http.createServer();
  server.headersTimeout = 30_000;
  server.keepAliveTimeout = 5_000;

  server.on('request', (req: http.IncomingMessage, res: http.ServerResponse) => {
    handleRequest(req, res).catch((err: unknown) => {
      log.event('internal_error', { error: err instanceof Error ? err.message : String(err) });
      sendDockerError(res, 500, 'oet-agent-dockerproxy: internal error');
    });
  });
  server.on('upgrade', (req: http.IncomingMessage, socket: Duplex, head: Buffer) => {
    handleUpgrade(req, socket, head).catch((err: unknown) => {
      log.event('internal_error', { error: err instanceof Error ? err.message : String(err) });
      rawResponse(socket, 500, 'oet-agent-dockerproxy: internal error');
    });
  });
  server.on('connect', (_req: http.IncomingMessage, socket: Duplex) => {
    rawResponse(socket, 405, 'oet-agent-dockerproxy: CONNECT is not supported');
  });
  server.on('clientError', (_err: Error, socket: Duplex) => {
    if (socket.writable) socket.end('HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n');
    else socket.destroy();
  });
  return server;
}
