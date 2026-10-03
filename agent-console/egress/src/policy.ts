// Egress policy (CONTRACT.md §6). Pure functions — no I/O — so the policy
// table is unit-tested in tests/policy.test.ts.
//
//   1. Private / loopback / link-local / reserved destinations: always denied
//      (not approvable; also re-checked on every DNS answer in server.ts, so
//      DNS rebinding cannot reach oet_agent_net, the Docker host or metadata).
//   2. Control-plane requests (valid X-Oet-Control-Token): allowed.
//   3. Static suffix allowlist on the standard port (CONNECT 443, HTTP 80): allowed.
//   4. Per-session grant (owner chose "approve for session"): allowed.
//   5. Everything else: approval callback to the sidecar.
import { BlockList, isIP } from 'node:net';
import { domainToASCII } from 'node:url';

/** CONTRACT.md §6 static allowlist — suffix match on DNS label boundaries. */
export const STATIC_ALLOWLIST: readonly string[] = Object.freeze([
  'api.anthropic.com',
  'claude.ai',
  'console.anthropic.com',
  'platform.claude.com',
  'statsig.anthropic.com',
  'chatgpt.com',
  'auth.openai.com',
  'api.openai.com',
  'ab.chatgpt.com',
  'github.com',
  'api.github.com',
  'githubusercontent.com',
  'ghcr.io',
  'registry.npmjs.org',
  'oetwithdrhesham.co.uk',
  // Built-in OpenCode provider (native provider defaults, "opencode" / fledge
  // models and Zen); the console's first-level backend and free-model traffic.
  'opencode.ai',
  'models.opencode.ai',
  'oaiusercontent.com',
]);

export const DEFAULT_CONNECT_PORTS: readonly number[] = Object.freeze([443]);
export const DEFAULT_HTTP_PORTS: readonly number[] = Object.freeze([80]);

const FORBIDDEN_V4: ReadonlyArray<readonly [string, number]> = [
  ['0.0.0.0', 8],
  ['10.0.0.0', 8],
  ['100.64.0.0', 10],
  ['127.0.0.0', 8],
  ['169.254.0.0', 16],
  ['172.16.0.0', 12],
  ['192.0.0.0', 24],
  ['192.0.2.0', 24],
  ['192.88.99.0', 24],
  ['192.168.0.0', 16],
  ['198.18.0.0', 15],
  ['198.51.100.0', 24],
  ['203.0.113.0', 24],
  ['224.0.0.0', 4],
  ['240.0.0.0', 4],
];

const FORBIDDEN_V6: ReadonlyArray<readonly [string, number]> = [
  ['::', 96], // unspecified, loopback, IPv4-compatible
  ['64:ff9b::', 96], // NAT64
  ['64:ff9b:1::', 48],
  ['100::', 64], // discard-only
  ['2001::', 32], // Teredo
  ['2001:db8::', 32], // documentation
  ['2002::', 16], // 6to4 (embeds IPv4)
  ['fc00::', 7], // unique local
  ['fe80::', 10], // link-local
  ['fec0::', 10], // site-local (deprecated)
  ['ff00::', 8], // multicast
];

const forbidden = new BlockList();
for (const [network, prefix] of FORBIDDEN_V4) forbidden.addSubnet(network, prefix, 'ipv4');
for (const [network, prefix] of FORBIDDEN_V6) forbidden.addSubnet(network, prefix, 'ipv6');

const MAPPED_V4 = /^::ffff:([0-9a-f]{1,4}):([0-9a-f]{1,4})$/;

function canonicalIPv6(address: string): string | null {
  try {
    const host = new URL(`http://[${address}]/`).hostname;
    return host.startsWith('[') && host.endsWith(']') ? host.slice(1, -1) : host;
  } catch {
    return null;
  }
}

/**
 * True for any address the proxy must never connect to. Non-IP input returns
 * true (fail closed) — callers only pass resolved addresses or IP literals.
 */
export function isForbiddenAddress(input: string): boolean {
  let address = input.trim().toLowerCase();
  if (address.startsWith('[') && address.endsWith(']')) address = address.slice(1, -1);
  const zone = address.indexOf('%');
  if (zone >= 0) address = address.slice(0, zone);
  const family = isIP(address);
  if (family === 4) return forbidden.check(address, 'ipv4');
  if (family === 6) {
    const canonical = canonicalIPv6(address);
    if (canonical === null) return true;
    const mapped = MAPPED_V4.exec(canonical);
    if (mapped) {
      const hi = Number.parseInt(mapped[1] ?? '0', 16);
      const lo = Number.parseInt(mapped[2] ?? '0', 16);
      const v4 = `${hi >> 8}.${hi & 0xff}.${lo >> 8}.${lo & 0xff}`;
      return forbidden.check(v4, 'ipv4');
    }
    return forbidden.check(canonical, 'ipv6');
  }
  return true;
}

const LABEL = /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/;

/**
 * Lower-cases, strips brackets / one trailing dot and converts IDN to punycode.
 * Returns null for anything that is not a public DNS name or an IP literal:
 * single-label names (container DNS, "localhost"), numeric TLDs
 * ("2130706433", "0177.0.0.1") and invalid labels.
 */
export function normalizeHost(raw: string): string | null {
  let host = raw.trim().toLowerCase();
  if (host.startsWith('[') && host.endsWith(']')) host = host.slice(1, -1);
  if (host.endsWith('.')) host = host.slice(0, -1);
  if (host.length === 0 || host.length > 253) return null;
  if (isIP(host) !== 0) return host;
  const ascii = domainToASCII(host);
  if (!ascii) return null;
  const labels = ascii.split('.');
  if (labels.length < 2) return null;
  if (!labels.every((label) => LABEL.test(label))) return null;
  const tld = labels[labels.length - 1] ?? '';
  if (/^[0-9]+$/.test(tld) || /^0x/.test(tld)) return null;
  return ascii;
}

/** Suffix match on label boundaries: "api.github.com" matches "github.com"; "evilgithub.com" does not. */
export function matchesAllowlist(host: string, allowlist: readonly string[] = STATIC_ALLOWLIST): boolean {
  if (isIP(host) !== 0) return false;
  return allowlist.some((entry) => host === entry || host.endsWith(`.${entry}`));
}

export function grantKey(host: string, port: number): string {
  return `${host}:${port}`;
}

export type EgressKind = 'connect' | 'http';

export interface EgressTarget {
  /** Already normalized with normalizeHost(). */
  host: string;
  port: number;
  kind: EgressKind;
  sessionId: string | null;
  /** Request carried a valid X-Oet-Control-Token. */
  control: boolean;
}

export interface EgressPolicy {
  allowlist: readonly string[];
  connectPorts: ReadonlySet<number>;
  httpPorts: ReadonlySet<number>;
  hasGrant(sessionId: string, host: string, port: number): boolean;
}

export type EgressVerdict =
  | { decision: 'allow'; via: 'static' | 'session' | 'control'; reasons: string[] }
  | { decision: 'deny'; reasons: string[] }
  | { decision: 'approval'; reasons: string[] };

export function evaluateEgress(target: EgressTarget, policy: EgressPolicy): EgressVerdict {
  const { host, port, kind, sessionId } = target;
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    return { decision: 'deny', reasons: [`invalid port ${String(port)}`] };
  }
  const ipLiteral = isIP(host) !== 0;
  if (ipLiteral && isForbiddenAddress(host)) {
    return { decision: 'deny', reasons: ['private, loopback, link-local or reserved address'] };
  }
  if (target.control) {
    return { decision: 'allow', via: 'control', reasons: [] };
  }
  const standardPort = (kind === 'connect' ? policy.connectPorts : policy.httpPorts).has(port);
  const listed = matchesAllowlist(host, policy.allowlist);
  if (listed && standardPort) {
    return { decision: 'allow', via: 'static', reasons: [] };
  }
  if (sessionId !== null && policy.hasGrant(sessionId, host, port)) {
    return { decision: 'allow', via: 'session', reasons: [] };
  }
  const reasons: string[] = [];
  if (!listed) {
    reasons.push(ipLiteral ? `IP literal destination ${host}` : `${host} is not on the static egress allowlist`);
  }
  if (!standardPort) {
    reasons.push(`non-standard port ${port} for ${kind === 'connect' ? 'CONNECT' : 'plain HTTP'}`);
  }
  if (kind === 'http') reasons.push('unencrypted plain-HTTP request');
  if (sessionId === null) reasons.push('no agent session attribution (system queue)');
  return { decision: 'approval', reasons };
}
