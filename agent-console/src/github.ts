import { chmod, mkdir, readFile, rename, rm, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import type { GithubStatus } from './contract.js';
import { buildAgentEnv, buildControlEnv, type AgentEnvConfig, type ControlEnvConfig } from './env.js';
import { badRequest } from './errors.js';
import { CommandError, runChecked, type Runner } from './exec.js';
import type { Logger } from './log.js';
import type { Redactor } from './redact.js';

// GitHub credentials (plan "src/auth/github.ts").
//  - Agent PAT: stored under /home/agent (0600, owned by uid 10002 because it
//    is written *as* the agent), `gh auth login` + `gh auth setup-git` for the
//    agent so git pushes/fetches use the gh credential helper.
//  - Ship PAT: /var/lib/oet-agent/ship-token (0400 root), readable by the
//    control identity only; used by the Ship executor.
// Tokens are write-only through the API and never returned.

export interface GithubConfig extends AgentEnvConfig, ControlEnvConfig {
  agentTokenPath: string;
  shipTokenPath: string;
  gitUserName: string;
  gitUserEmail: string;
}

const TOKEN_RE = /^(github_pat_[A-Za-z0-9_]{20,255}|gh[pousr]_[A-Za-z0-9]{20,255})$/;
const LOGIN_CACHE_MS = 10 * 60_000;

/** Validates a PAT shape; empty string means "clear". Never echoes the value. */
export function validateGithubToken(field: string, value: unknown): string {
  if (typeof value !== 'string') throw badRequest('invalid_token', `${field} must be a string.`);
  const trimmed = value.trim();
  if (trimmed === '') return '';
  if (!TOKEN_RE.test(trimmed)) {
    throw badRequest('invalid_token', `${field} is not a GitHub token (expected github_pat_… or ghp_…).`);
  }
  return trimmed;
}

export class GithubTokens {
  private login: { value: string | undefined; at: number } | null = null;

  constructor(
    private readonly config: GithubConfig,
    private readonly run: Runner,
    private readonly redactor: Redactor,
    private readonly logger: Logger,
    private readonly now: () => number = Date.now,
  ) {}

  private agentEnv(): Record<string, string> {
    return buildAgentEnv(this.config);
  }

  /** Registers existing token values with the redactor (boot). */
  async loadKnownSecrets(): Promise<void> {
    const ship = await this.readShipToken();
    if (ship) this.redactor.addSecret(ship);
    const agent = await this.readAgentToken();
    if (agent) this.redactor.addSecret(agent);
  }

  async readShipToken(): Promise<string | null> {
    try {
      const value = (await readFile(this.config.shipTokenPath, 'utf8')).trim();
      return value || null;
    } catch {
      return null;
    }
  }

  /** Read as the agent uid (the file lives in an agent-writable home). */
  private async readAgentToken(): Promise<string | null> {
    try {
      const result = await this.run('cat', ['--', this.config.agentTokenPath], {
        asAgent: true,
        env: this.agentEnv(),
        timeoutMs: 15_000,
        maxOutputBytes: 4096,
      });
      const value = result.code === 0 ? result.stdout.trim() : '';
      return value || null;
    } catch {
      return null;
    }
  }

  async setTokens(input: { agentToken?: unknown; shipToken?: unknown }): Promise<GithubStatus> {
    const agentToken = input.agentToken === undefined ? undefined : validateGithubToken('agentToken', input.agentToken);
    const shipToken = input.shipToken === undefined ? undefined : validateGithubToken('shipToken', input.shipToken);
    if (agentToken === undefined && shipToken === undefined) {
      throw badRequest('bad_request', 'Provide agentToken and/or shipToken.');
    }
    if (shipToken !== undefined) await this.writeShipToken(shipToken);
    if (agentToken !== undefined) await this.writeAgentToken(agentToken);
    this.login = null;
    return this.status();
  }

  private async writeShipToken(token: string): Promise<void> {
    const file = this.config.shipTokenPath;
    const previous = await this.readShipToken();
    if (token === '') {
      await rm(file, { force: true });
    } else {
      await mkdir(path.dirname(file), { recursive: true, mode: 0o700 });
      const tmp = `${file}.${process.pid}.tmp`;
      await writeFile(tmp, `${token}\n`, { mode: 0o400 });
      await chmod(tmp, 0o400);
      await rename(tmp, file);
      this.redactor.addSecret(token);
    }
    if (previous && previous !== token) this.logger.info('ship token replaced');
  }

  private async writeAgentToken(token: string): Promise<void> {
    const env = this.agentEnv();
    const dir = path.posix.dirname(this.config.agentTokenPath);
    if (token === '') {
      await this.run('rm', ['-f', '--', this.config.agentTokenPath], { asAgent: true, env });
      await this.run('gh', ['auth', 'logout', '--hostname', 'github.com'], { asAgent: true, env, input: 'Y\n', timeoutMs: 30_000 });
      return;
    }
    // Written as the agent uid: ownership 10002 follows, and a planted symlink
    // can never redirect a root-privileged write.
    await runChecked(this.run, 'mkdir', ['-p', '-m', '700', '--', dir], { asAgent: true, env });
    await runChecked(this.run, 'sh', ['-c', 'umask 077 && cat > "$1" && chmod 600 "$1"', 'oet-write-token', this.config.agentTokenPath], {
      asAgent: true,
      env,
      input: `${token}\n`,
    });
    this.redactor.addSecret(token);
    try {
      await runChecked(this.run, 'gh', ['auth', 'login', '--hostname', 'github.com', '--git-protocol', 'https', '--with-token'], {
        asAgent: true,
        env,
        input: `${token}\n`,
        timeoutMs: 60_000,
      });
      await runChecked(this.run, 'gh', ['auth', 'setup-git', '--hostname', 'github.com'], { asAgent: true, env, timeoutMs: 30_000 });
      await runChecked(this.run, 'git', ['config', '--global', 'user.name', this.config.gitUserName], { asAgent: true, env });
      await runChecked(this.run, 'git', ['config', '--global', 'user.email', this.config.gitUserEmail], { asAgent: true, env });
    } catch (error) {
      const detail = error instanceof CommandError ? this.redactor.redact(error.message) : 'gh auth failed';
      this.logger.warn({ detail }, 'agent gh auth setup failed');
      throw badRequest('github_auth_failed', `Stored the agent token, but gh could not sign in: ${detail}`);
    }
  }

  async status(): Promise<GithubStatus> {
    const shipTokenSet = await fileExists(this.config.shipTokenPath);
    const agentProbe = await this.run('test', ['-s', this.config.agentTokenPath], {
      asAgent: true,
      env: this.agentEnv(),
      timeoutMs: 15_000,
    }).catch(() => null);
    const agentTokenSet = agentProbe?.code === 0;
    const status: GithubStatus = { agentTokenSet, shipTokenSet };
    if (agentTokenSet) {
      const login = await this.lookupLogin();
      if (login) status.login = login;
    }
    return status;
  }

  /** `gh api user` as the agent, through the egress proxy; cached for 10 minutes. */
  private async lookupLogin(): Promise<string | undefined> {
    if (this.login && this.now() - this.login.at < LOGIN_CACHE_MS) return this.login.value;
    try {
      const result = await this.run('gh', ['api', 'user', '--jq', '.login'], {
        asAgent: true,
        env: this.agentEnv(),
        timeoutMs: 20_000,
        maxOutputBytes: 1024,
      });
      const value = result.code === 0 ? result.stdout.trim() : '';
      const login = /^[A-Za-z0-9-]{1,39}$/.test(value) ? value : undefined;
      this.login = { value: login, at: this.now() };
      return login;
    } catch {
      return undefined;
    }
  }

  /** Env for control-plane gh/git calls authenticated with the Ship PAT. */
  async shipEnv(extra: Record<string, string> = {}): Promise<Record<string, string> | null> {
    const token = await this.readShipToken();
    if (!token) return null;
    const basic = Buffer.from(`x-access-token:${token}`, 'utf8').toString('base64');
    this.redactor.addSecret(basic);
    return buildControlEnv(this.config, {
      GH_TOKEN: token,
      // git: token only via env (never argv, so it is invisible in /proc/*/cmdline),
      // helpers/hooks disabled for control-plane git.
      GIT_CONFIG_COUNT: '3',
      GIT_CONFIG_KEY_0: 'http.https://github.com/.extraheader',
      GIT_CONFIG_VALUE_0: `AUTHORIZATION: basic ${basic}`,
      GIT_CONFIG_KEY_1: 'credential.helper',
      GIT_CONFIG_VALUE_1: '',
      GIT_CONFIG_KEY_2: 'core.hooksPath',
      GIT_CONFIG_VALUE_2: '/dev/null',
      ...extra,
    });
  }
}

async function fileExists(file: string): Promise<boolean> {
  try {
    const s = await stat(file);
    return s.isFile() && s.size > 0;
  } catch {
    return false;
  }
}
