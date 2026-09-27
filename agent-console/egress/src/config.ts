import { readTokenFile } from './secrets.js';
import { DEFAULT_CONNECT_PORTS, DEFAULT_HTTP_PORTS } from './policy.js';

export interface EgressConfig {
  listenHost: string;
  listenPort: number;
  /** Shared proxy token (control-plane bypass + approval callback auth). */
  proxyToken: string;
  approvalUrl: string;
  approvalTimeoutMs: number;
  connectPorts: ReadonlySet<number>;
  httpPorts: ReadonlySet<number>;
  sessionGrantTtlMs: number;
  denyCacheMs: number;
  tunnelIdleTimeoutMs: number;
  connectTimeoutMs: number;
  maxConnections: number;
}

type Env = Record<string, string | undefined>;

function text(env: Env, key: string, fallback: string): string {
  const value = env[key]?.trim();
  return value === undefined || value === '' ? fallback : value;
}

function integer(env: Env, key: string, fallback: number, min: number, max: number): number {
  const raw = env[key]?.trim();
  if (raw === undefined || raw === '') return fallback;
  const value = Number(raw);
  if (!Number.isInteger(value) || value < min || value > max) {
    throw new Error(`${key} must be an integer between ${min} and ${max}`);
  }
  return value;
}

function ports(env: Env, key: string, fallback: readonly number[]): ReadonlySet<number> {
  const raw = env[key]?.trim();
  if (raw === undefined || raw === '') return new Set(fallback);
  const out = new Set<number>();
  for (const part of raw.split(',')) {
    const value = Number(part.trim());
    if (!Number.isInteger(value) || value < 1 || value > 65535) {
      throw new Error(`${key} must be a comma-separated list of TCP ports`);
    }
    out.add(value);
  }
  return out;
}

export const DEFAULT_TOKEN_FILE = '/run/secrets/owner_agent_proxy_token';
export const DEFAULT_APPROVAL_URL = 'http://oet-agent-console:8410/internal/approvals';

/** Loads configuration; throws (refuse to start) when the proxy token is missing or < 32 chars. */
export function loadConfig(env: Env = process.env, readToken: (path: string) => string = readTokenFile): EgressConfig {
  return {
    listenHost: text(env, 'EGRESS_LISTEN_HOST', '0.0.0.0'),
    listenPort: integer(env, 'EGRESS_LISTEN_PORT', 3128, 1, 65535),
    proxyToken: readToken(text(env, 'OWNER_AGENT_PROXY_TOKEN_FILE', DEFAULT_TOKEN_FILE)),
    approvalUrl: text(env, 'OWNER_AGENT_APPROVAL_URL', DEFAULT_APPROVAL_URL),
    // The sidecar decides within 600 s; the extra 15 s covers its own bookkeeping.
    approvalTimeoutMs: integer(env, 'EGRESS_APPROVAL_TIMEOUT_SECONDS', 615, 1, 3600) * 1000,
    connectPorts: ports(env, 'EGRESS_CONNECT_PORTS', DEFAULT_CONNECT_PORTS),
    httpPorts: ports(env, 'EGRESS_HTTP_PORTS', DEFAULT_HTTP_PORTS),
    sessionGrantTtlMs: integer(env, 'EGRESS_SESSION_GRANT_TTL_SECONDS', 86_400, 60, 7 * 86_400) * 1000,
    denyCacheMs: integer(env, 'EGRESS_DENY_CACHE_SECONDS', 60, 0, 3600) * 1000,
    tunnelIdleTimeoutMs: integer(env, 'EGRESS_TUNNEL_IDLE_TIMEOUT_SECONDS', 600, 10, 86_400) * 1000,
    connectTimeoutMs: integer(env, 'EGRESS_CONNECT_TIMEOUT_SECONDS', 15, 1, 120) * 1000,
    maxConnections: integer(env, 'EGRESS_MAX_CONNECTIONS', 512, 1, 10_000),
  };
}
