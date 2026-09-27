import { readFileSync } from 'node:fs';

// Sidecar configuration. Secrets come from files under /run/secrets (compose
// secrets, mode 0400 root) with an env fallback; everything else from env with
// production defaults matching CONTRACT.md §2. The server refuses to start
// (ConfigError) when the internal token is missing or shorter than 32 chars.

export const MIN_TOKEN_LENGTH = 32;

export class ConfigError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'ConfigError';
  }
}

export interface SnapshotConfig {
  /** Container that owns the backup script (CONTRACT.md §6 control-plane exec). */
  container: string;
  script: string;
  /** Full-database dumps are rate limited to one per this many ms. */
  fullDumpMinIntervalMs: number;
  timeoutMs: number;
}

export interface ShipConfig {
  /** `owner/repo` of the GitHub repository. */
  repo: string;
  baseBranch: string;
  deployWorkflowFile: string;
  deployWorkflowName: string;
  /** The console's own build/deploy workflow, dispatched with apply=true by "Apply update". */
  consoleWorkflowFile: string;
  holdersVariable: string;
  healthUrls: string[];
  /** Watchdog forces the repo private this long after the ship opened a public window. */
  publicWindowMaxMs: number;
  mirrorDir: string;
  workDir: string;
  gitleaksPath: string;
}

export interface AppConfig {
  version: string;
  host: string;
  port: number;
  logLevel: string;

  internalToken: string;
  /** Shared secret with the egress/docker proxies; null disables /internal/approvals (fail closed). */
  proxyToken: string | null;
  /** Lower-cased X-Oet-Owner-Account allow-list. */
  ownerAccountIds: ReadonlySet<string>;

  /** Control-only state root (/var/lib/oet-agent, 0700 root). */
  dataDir: string;
  sessionsDir: string;
  dbPath: string;
  shipTokenPath: string;
  /** HOME for control-plane child processes (gh/git/curl as root). */
  controlHome: string;

  agentUid: number;
  agentGid: number;
  agentHome: string;
  claudeConfigDir: string;
  codexHome: string;
  agentTokenPath: string;
  asAgentPath: string;
  manualPath: string;
  gitUserName: string;
  gitUserEmail: string;

  workspaceRoot: string;
  repoDir: string;
  worktreeRoot: string;
  repoCloneUrl: string;
  /** Root-owned, world-readable parent of the per-session DOCKER_CONFIG dirs. */
  dockerConfigRoot: string;
  minFreeDiskBytes: number;

  egressProxyUrl: string;
  noProxy: string;
  dockerHost: string;
  /** postgres URL of the oet_owner_agent role (via oet-agent-dbproxy); secret. */
  agentDatabaseUrl: string | null;

  maxConcurrentTurns: number;
  idleCloseMs: number;
  approvalTtlMs: number;
  proxyApprovalWaitMs: number;
  leaseMaxMs: number;
  /** A running turn paused at a tool boundary (lease lapsed) is denied after this long. */
  leasePauseMaxMs: number;
  retentionDays: number;
  diffCapBytes: number;
  handoffEventCount: number;
  updatePendingFile: string | null;

  snapshot: SnapshotConfig;
  ship: ShipConfig;
}

export type SecretReader = (path: string) => string | null;

export interface LoadConfigOptions {
  env?: NodeJS.ProcessEnv;
  readFile?: SecretReader;
}

function defaultReadFile(path: string): string | null {
  try {
    return readFileSync(path, 'utf8');
  } catch (error) {
    const code = (error as NodeJS.ErrnoException).code;
    if (code === 'ENOENT' || code === 'EACCES' || code === 'EISDIR' || code === 'ENOTDIR') return null;
    throw error;
  }
}

/**
 * Reads a secret from `<fileVar>` (default path) first, then from the env var `<name>`.
 * Values are trimmed; empty means absent.
 */
export function readSecret(
  env: NodeJS.ProcessEnv,
  readFile: SecretReader,
  name: string,
  fileVar: string,
  defaultFile: string,
): string | null {
  const file = env[fileVar]?.trim() || defaultFile;
  const fromFile = readFile(file);
  if (fromFile !== null && fromFile.trim() !== '') return fromFile.trim();
  const fromEnv = env[name]?.trim();
  return fromEnv ? fromEnv : null;
}

export function parseOwnerAccountIds(raw: string | undefined): Set<string> {
  const ids = new Set<string>();
  for (const part of (raw ?? '').split(/[,;\s]+/)) {
    const id = part.trim().toLowerCase();
    if (id) ids.add(id);
  }
  return ids;
}

function intFromEnv(env: NodeJS.ProcessEnv, name: string, fallback: number, min = 0): number {
  const raw = env[name]?.trim();
  if (!raw) return fallback;
  const value = Number(raw);
  if (!Number.isFinite(value) || !Number.isInteger(value) || value < min) {
    throw new ConfigError(`${name} must be an integer >= ${min}.`);
  }
  return value;
}

function str(env: NodeJS.ProcessEnv, name: string, fallback: string): string {
  const raw = env[name]?.trim();
  return raw ? raw : fallback;
}

function readPackageVersion(): string {
  try {
    const raw = readFileSync(new URL('../package.json', import.meta.url), 'utf8');
    const parsed = JSON.parse(raw) as { version?: unknown };
    return typeof parsed.version === 'string' ? parsed.version : '0.0.0';
  } catch {
    return '0.0.0';
  }
}

const DEFAULT_HEALTH_URLS = [
  'https://app.oetwithdrhesham.co.uk/api/health',
  'https://api.oetwithdrhesham.co.uk/health/ready',
  'https://api.oetwithdrhesham.co.uk/health/live',
];

export function loadConfig(options: LoadConfigOptions = {}): AppConfig {
  const env = options.env ?? process.env;
  const readFile = options.readFile ?? defaultReadFile;

  const internalToken = readSecret(
    env,
    readFile,
    'OWNER_AGENT_INTERNAL_TOKEN',
    'OWNER_AGENT_INTERNAL_TOKEN_FILE',
    '/run/secrets/owner_agent_internal_token',
  );
  if (!internalToken) {
    throw new ConfigError(
      'Internal token missing: set OWNER_AGENT_INTERNAL_TOKEN_FILE (default /run/secrets/owner_agent_internal_token) or OWNER_AGENT_INTERNAL_TOKEN.',
    );
  }
  if (internalToken.length < MIN_TOKEN_LENGTH) {
    throw new ConfigError(`Internal token must be at least ${MIN_TOKEN_LENGTH} characters.`);
  }

  const proxyToken = readSecret(
    env,
    readFile,
    'OWNER_AGENT_PROXY_TOKEN',
    'OWNER_AGENT_PROXY_TOKEN_FILE',
    '/run/secrets/owner_agent_proxy_token',
  );
  if (proxyToken !== null && proxyToken.length < MIN_TOKEN_LENGTH) {
    throw new ConfigError(`Proxy token must be at least ${MIN_TOKEN_LENGTH} characters.`);
  }
  if (proxyToken !== null && proxyToken === internalToken) {
    throw new ConfigError('Proxy token must differ from the internal token.');
  }

  const ownerAccountIds = parseOwnerAccountIds(env.OWNER_AGENT_OWNER_ACCOUNT_IDS);
  if (ownerAccountIds.size === 0) {
    throw new ConfigError('OWNER_AGENT_OWNER_ACCOUNT_IDS must list at least one owner account id.');
  }

  const agentDatabaseUrl = readSecret(
    env,
    readFile,
    'OET_AGENT_DATABASE_URL',
    'OET_AGENT_DATABASE_URL_FILE',
    '/run/secrets/oet_agent_database_url',
  );

  const dataDir = str(env, 'AGENT_CONSOLE_DATA_DIR', '/var/lib/oet-agent');
  const agentHome = str(env, 'AGENT_CONSOLE_AGENT_HOME', '/home/agent');
  const workspaceRoot = str(env, 'AGENT_CONSOLE_WORKSPACE_ROOT', '/workspace');
  const repo = str(env, 'AGENT_CONSOLE_REPO', 'jerryboganda/oetwebapp');
  if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repo)) {
    throw new ConfigError('AGENT_CONSOLE_REPO must be "owner/repo".');
  }
  const rawHealthUrls = env.AGENT_CONSOLE_HEALTH_URLS?.trim() ?? '';
  const healthUrls = (rawHealthUrls ? rawHealthUrls.split(',') : DEFAULT_HEALTH_URLS)
    .map((url) => url.trim())
    .filter((url) => url.length > 0);
  for (const url of healthUrls) {
    if (!/^https:\/\/[^\s]+$/.test(url)) throw new ConfigError('AGENT_CONSOLE_HEALTH_URLS entries must be https URLs.');
  }

  // Image tag (the commit SHA from agent-console.yml) is the most useful version string.
  const imageTag = /:([A-Za-z0-9_.-]{1,128})$/.exec((env.OET_AGENT_IMAGE ?? '').trim().replace(/@.*$/, ''))?.[1];

  return {
    version: str(env, 'AGENT_CONSOLE_VERSION', imageTag ?? readPackageVersion()),
    host: str(env, 'AGENT_CONSOLE_HOST', '0.0.0.0'),
    port: intFromEnv(env, 'AGENT_CONSOLE_PORT', 8410, 1),
    logLevel: str(env, 'AGENT_CONSOLE_LOG_LEVEL', 'info'),

    internalToken,
    proxyToken,
    ownerAccountIds,

    dataDir,
    sessionsDir: str(env, 'AGENT_CONSOLE_SESSIONS_DIR', `${dataDir}/sessions`),
    dbPath: str(env, 'AGENT_CONSOLE_DB_PATH', `${dataDir}/sessions/index.sqlite`),
    shipTokenPath: `${dataDir}/ship-token`,
    controlHome: `${dataDir}/home`,

    agentUid: intFromEnv(env, 'AGENT_CONSOLE_AGENT_UID', 10002, 1),
    agentGid: intFromEnv(env, 'AGENT_CONSOLE_AGENT_GID', 10002, 1),
    agentHome,
    claudeConfigDir: str(env, 'AGENT_CONSOLE_CLAUDE_CONFIG_DIR', `${agentHome}/.claude`),
    codexHome: str(env, 'AGENT_CONSOLE_CODEX_HOME', `${agentHome}/.codex`),
    agentTokenPath: `${agentHome}/.config/oet-agent/github-token`,
    asAgentPath: str(env, 'AGENT_CONSOLE_AS_AGENT', '/usr/local/bin/as-agent'),
    manualPath: str(env, 'AGENT_CONSOLE_MANUAL_PATH', '/app/etc/MANUAL.md'),
    gitUserName: str(env, 'AGENT_CONSOLE_GIT_NAME', 'OET Owner Agent'),
    gitUserEmail: str(env, 'AGENT_CONSOLE_GIT_EMAIL', 'owner-agent@oet-agent-console.invalid'),

    workspaceRoot,
    repoDir: `${workspaceRoot}/oetwebapp`,
    worktreeRoot: `${workspaceRoot}/sessions`,
    repoCloneUrl: `https://github.com/${repo}.git`,
    dockerConfigRoot: str(env, 'AGENT_CONSOLE_DOCKER_CONFIG_ROOT', '/run/oet-agent/docker'),
    minFreeDiskBytes: intFromEnv(env, 'AGENT_CONSOLE_MIN_FREE_DISK_BYTES', 2 * 1024 * 1024 * 1024),

    egressProxyUrl: str(env, 'AGENT_CONSOLE_EGRESS_PROXY', 'http://oet-agent-egress:3128'),
    noProxy: str(env, 'AGENT_CONSOLE_NO_PROXY', 'oet-agent-dockerproxy,oet-agent-dbproxy,localhost,127.0.0.1'),
    dockerHost: str(env, 'DOCKER_HOST', 'tcp://oet-agent-dockerproxy:2375'),
    agentDatabaseUrl,

    maxConcurrentTurns: intFromEnv(env, 'AGENT_CONSOLE_MAX_CONCURRENT_TURNS', 2, 1),
    idleCloseMs: intFromEnv(env, 'AGENT_CONSOLE_IDLE_CLOSE_MS', 10 * 60_000, 1),
    approvalTtlMs: intFromEnv(env, 'AGENT_CONSOLE_APPROVAL_TTL_MS', 30 * 60_000, 1),
    proxyApprovalWaitMs: intFromEnv(env, 'AGENT_CONSOLE_PROXY_APPROVAL_WAIT_MS', 600_000, 1),
    leaseMaxMs: intFromEnv(env, 'AGENT_CONSOLE_LEASE_MAX_MS', 3 * 60_000, 1),
    leasePauseMaxMs: intFromEnv(env, 'AGENT_CONSOLE_LEASE_PAUSE_MAX_MS', 30 * 60_000, 1),
    retentionDays: intFromEnv(env, 'AGENT_CONSOLE_RETENTION_DAYS', 90, 1),
    diffCapBytes: intFromEnv(env, 'AGENT_CONSOLE_DIFF_CAP_BYTES', 2 * 1024 * 1024, 1024),
    handoffEventCount: intFromEnv(env, 'AGENT_CONSOLE_HANDOFF_EVENTS', 60, 1),
    updatePendingFile: env.AGENT_CONSOLE_UPDATE_PENDING_FILE?.trim() || null,

    snapshot: {
      container: str(env, 'AGENT_CONSOLE_SNAPSHOT_CONTAINER', 'oet-db-backup'),
      script: str(env, 'AGENT_CONSOLE_SNAPSHOT_SCRIPT', '/usr/local/bin/postgres-backup.sh'),
      fullDumpMinIntervalMs: intFromEnv(env, 'AGENT_CONSOLE_FULL_SNAPSHOT_INTERVAL_MS', 10 * 60_000),
      timeoutMs: intFromEnv(env, 'AGENT_CONSOLE_SNAPSHOT_TIMEOUT_MS', 15 * 60_000, 1),
    },
    ship: {
      repo,
      baseBranch: 'main',
      deployWorkflowFile: str(env, 'AGENT_CONSOLE_DEPLOY_WORKFLOW', 'deploy.yml'),
      deployWorkflowName: 'Build & Deploy (web + API)',
      consoleWorkflowFile: str(env, 'AGENT_CONSOLE_UPDATE_WORKFLOW', 'agent-console.yml'),
      holdersVariable: 'PUBLIC_WINDOW_HOLDERS',
      healthUrls,
      publicWindowMaxMs: intFromEnv(env, 'AGENT_CONSOLE_PUBLIC_WINDOW_MAX_MS', 90 * 60_000, 1),
      mirrorDir: `${dataDir}/ship-mirror.git`,
      workDir: `${dataDir}/ship`,
      gitleaksPath: str(env, 'AGENT_CONSOLE_GITLEAKS', 'gitleaks'),
    },
  };
}
