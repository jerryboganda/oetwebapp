import { readTokenFile } from './secrets.js';

export interface DockerProxyConfig {
  listenHost: string;
  listenPort: number;
  socketPath: string;
  /** Shared proxy token: control-plane bypass (X-Oet-Control-Token) + approval callback auth. */
  proxyToken: string;
  approvalUrl: string;
  approvalTimeoutMs: number;
  maxRequestBodyBytes: number;
  maxTransformBytes: number;
  sessionGrantTtlMs: number;
  execApprovalTtlMs: number;
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

export const DEFAULT_TOKEN_FILE = '/run/secrets/owner_agent_proxy_token';
export const DEFAULT_APPROVAL_URL = 'http://oet-agent-console:8410/internal/approvals';

/** Loads configuration; throws (refuse to start) when the proxy token is missing or < 32 chars. */
export function loadConfig(env: Env = process.env, readToken: (path: string) => string = readTokenFile): DockerProxyConfig {
  return {
    listenHost: text(env, 'DOCKERPROXY_LISTEN_HOST', '0.0.0.0'),
    listenPort: integer(env, 'DOCKERPROXY_LISTEN_PORT', 2375, 1, 65535),
    socketPath: text(env, 'DOCKER_SOCKET_PATH', '/var/run/docker.sock'),
    proxyToken: readToken(text(env, 'OWNER_AGENT_PROXY_TOKEN_FILE', DEFAULT_TOKEN_FILE)),
    approvalUrl: text(env, 'OWNER_AGENT_APPROVAL_URL', DEFAULT_APPROVAL_URL),
    approvalTimeoutMs: integer(env, 'DOCKERPROXY_APPROVAL_TIMEOUT_SECONDS', 615, 1, 3600) * 1000,
    maxRequestBodyBytes: integer(env, 'DOCKERPROXY_MAX_REQUEST_BODY_BYTES', 1024 * 1024, 1024, 64 * 1024 * 1024),
    maxTransformBytes: integer(env, 'DOCKERPROXY_MAX_TRANSFORM_BYTES', 64 * 1024 * 1024, 1024 * 1024, 512 * 1024 * 1024),
    sessionGrantTtlMs: integer(env, 'DOCKERPROXY_SESSION_GRANT_TTL_SECONDS', 86_400, 60, 7 * 86_400) * 1000,
    execApprovalTtlMs: integer(env, 'DOCKERPROXY_EXEC_APPROVAL_TTL_SECONDS', 900, 30, 86_400) * 1000,
  };
}
