import { chmod, mkdir, rename, writeFile } from 'node:fs/promises';
import path from 'node:path';

// Environment handed to every process that runs as the agent uid (engines,
// their tool subprocesses, git/gh for the workspace). It is built from an
// allow-list — nothing from the control plane's own environment leaks through
// except the harmless locale/PATH basics — and then scrubbed of vendor/API
// credentials and every OWNER_AGENT_* variable (plan Phase 1, src/env.ts).

/** Variables copied verbatim from the parent environment when present. */
export const PASSTHROUGH_ENV: readonly string[] = [
  'PATH',
  'LANG',
  'LANGUAGE',
  'LC_ALL',
  'LC_CTYPE',
  'TZ',
  'TERM',
  'NODE_EXTRA_CA_CERTS',
  'SSL_CERT_FILE',
  'SSL_CERT_DIR',
];

/** Parent variables copied by prefix (git identity overrides only). */
export const PASSTHROUGH_PREFIXES: readonly string[] = ['GIT_AUTHOR_', 'GIT_COMMITTER_'];

/** Removed from the final env even when a caller passes them explicitly. */
export const STRIPPED_ENV_PATTERNS: readonly RegExp[] = [
  /^ANTHROPIC_/i,
  /^CLAUDE_CODE_USE_/i,
  /^OPENAI_API_KEY$/i,
  /^CODEX_API_KEY$/i,
  /^OWNER_AGENT_/i,
  /^OWNERAGENT__/i,
  /^AGENT_CONSOLE_/i,
  /^GH_TOKEN$/i,
  /^GITHUB_TOKEN$/i,
  /^GH_ENTERPRISE_TOKEN$/i,
  /^GITHUB_ENTERPRISE_TOKEN$/i,
  /^GIT_CONFIG_/i,
  /^GIT_ASKPASS$/i,
  /^SSH_ASKPASS$/i,
  /^SSH_AUTH_SOCK$/i,
  /^AWS_/i,
  /^OET_AGENT_DATABASE_URL_FILE$/i,
  /^NODE_OPTIONS$/i,
  /^LD_PRELOAD$/i,
  /^LD_LIBRARY_PATH$/i,
];

const DEFAULT_PATH = '/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin';
const ULID_RE = /^[0-9A-HJKMNP-TV-Z]{26}$/;

/** Subset of AppConfig the env builders need (keeps tests independent of the full config). */
export interface AgentEnvConfig {
  agentHome: string;
  claudeConfigDir: string;
  codexHome: string;
  egressProxyUrl: string;
  noProxy: string;
  dockerHost: string;
  dockerConfigRoot: string;
  agentDatabaseUrl: string | null;
}

export interface AgentEnvOptions {
  /** Session to attribute egress/docker traffic to (CONTRACT.md §6). */
  sessionId?: string;
  /** Parent environment (defaults to process.env). */
  base?: NodeJS.ProcessEnv;
  /** Engine-specific additions; still subject to STRIPPED_ENV_PATTERNS. */
  extra?: Record<string, string | undefined>;
}

export function isStrippedEnvName(name: string): boolean {
  return STRIPPED_ENV_PATTERNS.some((pattern) => pattern.test(name));
}

export function assertSessionId(sessionId: string): void {
  if (!ULID_RE.test(sessionId)) throw new Error('Invalid session id.');
}

/** `http://<sessionId>:x@oet-agent-egress:3128` — the proxy reads the session from Proxy-Authorization. */
export function egressProxyUrl(base: string, sessionId?: string): string {
  const url = new URL(base);
  if (sessionId) {
    assertSessionId(sessionId);
    url.username = sessionId;
    url.password = 'x';
  } else {
    url.username = '';
    url.password = '';
  }
  return url.toString().replace(/\/$/, '');
}

export function dockerConfigDir(config: Pick<AgentEnvConfig, 'dockerConfigRoot'>, sessionId: string): string {
  assertSessionId(sessionId);
  return path.posix.join(config.dockerConfigRoot, sessionId);
}

/** Docker CLI config that stamps every Engine API request with the session id (CONTRACT.md §6). */
export function dockerConfigJson(sessionId: string): string {
  assertSessionId(sessionId);
  return `${JSON.stringify({ HttpHeaders: { 'X-Oet-Agent-Session': sessionId } }, null, 2)}\n`;
}

/**
 * Writes the per-session DOCKER_CONFIG dir. It lives under a root-owned,
 * world-readable root so the agent uid can read but not rewrite its header.
 */
export async function ensureDockerConfig(
  config: Pick<AgentEnvConfig, 'dockerConfigRoot'>,
  sessionId: string,
): Promise<string> {
  const dir = dockerConfigDir(config, sessionId);
  await mkdir(config.dockerConfigRoot, { recursive: true, mode: 0o755 });
  await mkdir(dir, { recursive: true, mode: 0o755 });
  await chmod(dir, 0o755);
  const file = path.posix.join(dir, 'config.json');
  const tmp = `${file}.${process.pid}.tmp`;
  await writeFile(tmp, dockerConfigJson(sessionId), { mode: 0o644 });
  await rename(tmp, file);
  return dir;
}

export function buildAgentEnv(config: AgentEnvConfig, options: AgentEnvOptions = {}): Record<string, string> {
  const base = options.base ?? process.env;
  const env: Record<string, string> = {};

  for (const name of PASSTHROUGH_ENV) {
    const value = base[name];
    if (typeof value === 'string' && value.length > 0) env[name] = value;
  }
  for (const [name, value] of Object.entries(base)) {
    if (typeof value === 'string' && PASSTHROUGH_PREFIXES.some((prefix) => name.startsWith(prefix))) {
      env[name] = value;
    }
  }

  env.PATH ??= DEFAULT_PATH;
  env.LANG ??= 'C.UTF-8';
  env.HOME = config.agentHome;
  env.USER = 'agent';
  env.LOGNAME = 'agent';
  env.SHELL = '/bin/bash';
  env.TMPDIR = '/tmp';

  env.CLAUDE_CONFIG_DIR = config.claudeConfigDir;
  env.CODEX_HOME = config.codexHome;

  const proxy = egressProxyUrl(config.egressProxyUrl, options.sessionId);
  env.HTTPS_PROXY = proxy;
  env.HTTP_PROXY = proxy;
  env.https_proxy = proxy;
  env.http_proxy = proxy;
  env.NO_PROXY = config.noProxy;
  env.no_proxy = config.noProxy;

  env.DOCKER_HOST = config.dockerHost;
  if (options.sessionId) env.DOCKER_CONFIG = dockerConfigDir(config, options.sessionId);

  if (config.agentDatabaseUrl) env.OET_AGENT_DATABASE_URL = config.agentDatabaseUrl;

  // No CLAUDE_CODE_SUBPROCESS_ENV_SCRUB: Claude Code >= 2.1.28x requires bubblewrap for it
  // and exits 1 at startup (login and every session) without it; bwrap cannot create user
  // namespaces in this container anyway. This env is already an allow-list with no secrets.
  env.DISABLE_AUTOUPDATER = '1';
  env.DISABLE_UPDATES = '1';
  env.GIT_TERMINAL_PROMPT = '0';
  env.GH_PROMPT_DISABLED = '1';
  env.GH_NO_UPDATE_NOTIFIER = '1';

  if (options.extra) {
    for (const [name, value] of Object.entries(options.extra)) {
      if (typeof value === 'string') env[name] = value;
    }
  }

  for (const name of Object.keys(env)) {
    if (isStrippedEnvName(name)) delete env[name];
  }
  return env;
}

export interface ControlEnvConfig {
  controlHome: string;
  egressProxyUrl: string;
  noProxy: string;
}

/**
 * Environment for control-plane children (gh/git/curl/gitleaks run as the
 * control identity). Never inherits process.env; callers add credentials such
 * as GH_TOKEN explicitly and only for the command that needs them.
 */
export function buildControlEnv(
  config: ControlEnvConfig,
  extra: Record<string, string | undefined> = {},
  base: NodeJS.ProcessEnv = process.env,
): Record<string, string> {
  const env: Record<string, string> = {
    PATH: base.PATH || DEFAULT_PATH,
    LANG: base.LANG || 'C.UTF-8',
    HOME: config.controlHome,
    GH_CONFIG_DIR: path.posix.join(config.controlHome, 'gh'),
    GIT_TERMINAL_PROMPT: '0',
    GIT_CONFIG_NOSYSTEM: '1',
    GH_PROMPT_DISABLED: '1',
    GH_NO_UPDATE_NOTIFIER: '1',
    NO_COLOR: '1',
  };
  const proxy = egressProxyUrl(config.egressProxyUrl);
  env.HTTPS_PROXY = proxy;
  env.HTTP_PROXY = proxy;
  env.https_proxy = proxy;
  env.http_proxy = proxy;
  env.NO_PROXY = config.noProxy;
  env.no_proxy = config.noProxy;
  for (const [name, value] of Object.entries(extra)) {
    if (typeof value === 'string') env[name] = value;
  }
  return env;
}
