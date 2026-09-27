// Request-line and header parsing for the egress proxy. Pure functions.
import type { IncomingHttpHeaders, OutgoingHttpHeaders } from 'node:http';
import { normalizeHost } from './policy.js';

/** Session ids are ULIDs (CONTRACT.md: 26 chars Crockford base32); accept a conservative superset. */
export const SESSION_ID_PATTERN = /^[A-Za-z0-9_-]{1,64}$/;

export interface ProxyAuth {
  sessionId: string | null;
  /** Why a present header was ignored (logged, never fatal). */
  problem?: string;
}

/**
 * The sidecar sets HTTPS_PROXY=http://<sessionId>:x@oet-agent-egress:3128, so
 * clients send `Proxy-Authorization: Basic base64("<sessionId>:x")`. The
 * password part is ignored. Missing/invalid ⇒ no session (system queue).
 */
export function parseProxyAuthorization(value: string | string[] | undefined): ProxyAuth {
  if (value === undefined) return { sessionId: null };
  if (Array.isArray(value)) return { sessionId: null, problem: 'multiple Proxy-Authorization headers' };
  const match = /^\s*basic\s+([A-Za-z0-9+/=_-]+)\s*$/i.exec(value);
  if (!match || match[1] === undefined) {
    return { sessionId: null, problem: 'unsupported Proxy-Authorization scheme' };
  }
  const decoded = Buffer.from(match[1], 'base64').toString('utf8');
  const colon = decoded.indexOf(':');
  const user = colon >= 0 ? decoded.slice(0, colon) : decoded;
  if (!SESSION_ID_PATTERN.test(user)) {
    return { sessionId: null, problem: 'invalid session id in Proxy-Authorization' };
  }
  return { sessionId: user };
}

export interface HostPort {
  host: string;
  port: number;
}

function parsePort(text: string): number | null {
  if (!/^[0-9]{1,5}$/.test(text)) return null;
  const port = Number(text);
  return port >= 1 && port <= 65535 ? port : null;
}

/** Parses a CONNECT request target (authority-form): "host:port" or "[v6]:port". */
export function parseConnectAuthority(target: string): HostPort | null {
  const text = target.trim();
  let hostPart: string;
  let portPart: string;
  if (text.startsWith('[')) {
    const end = text.indexOf(']');
    if (end < 0 || text[end + 1] !== ':') return null;
    hostPart = text.slice(1, end);
    portPart = text.slice(end + 2);
  } else {
    const idx = text.lastIndexOf(':');
    if (idx <= 0) return null;
    hostPart = text.slice(0, idx);
    portPart = text.slice(idx + 1);
    if (hostPart.includes(':')) return null;
  }
  const port = parsePort(portPart);
  if (port === null) return null;
  const host = normalizeHost(hostPart);
  if (host === null) return null;
  return { host, port };
}

export interface AbsoluteHttpTarget extends HostPort {
  /** origin-form path + query to send upstream */
  path: string;
  /** Host header value for the upstream request */
  hostHeader: string;
}

/**
 * Parses an absolute-form plain-HTTP request target ("http://host[:port]/path").
 * https:// absolute-form and URLs with userinfo are rejected (clients must use CONNECT).
 */
export function parseAbsoluteHttpUrl(raw: string): AbsoluteHttpTarget | null {
  let url: URL;
  try {
    url = new URL(raw);
  } catch {
    return null;
  }
  if (url.protocol !== 'http:') return null;
  if (url.username !== '' || url.password !== '') return null;
  const host = normalizeHost(url.hostname);
  if (host === null) return null;
  const port = url.port === '' ? 80 : parsePort(url.port);
  if (port === null) return null;
  const path = `${url.pathname === '' ? '/' : url.pathname}${url.search}`;
  const hostHeader = url.port === '' ? url.hostname : `${url.hostname}:${url.port}`;
  return { host, port, path, hostHeader };
}

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
  'x-oet-control-token',
]);

/**
 * Removes hop-by-hop headers (RFC 9110 §7.6.1), any header named in
 * Connection, the proxy credentials and the control token before forwarding.
 */
export function stripHopByHop(headers: IncomingHttpHeaders): OutgoingHttpHeaders {
  const named = new Set<string>();
  const connection = headers.connection;
  if (typeof connection === 'string') {
    for (const token of connection.split(',')) {
      const name = token.trim().toLowerCase();
      if (name !== '') named.add(name);
    }
  }
  const out: OutgoingHttpHeaders = {};
  for (const [name, value] of Object.entries(headers)) {
    if (value === undefined) continue;
    const key = name.toLowerCase();
    if (HOP_BY_HOP.has(key) || named.has(key)) continue;
    out[key] = value;
  }
  return out;
}
