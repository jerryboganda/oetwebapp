import { readFileSync, rmSync } from 'node:fs';
import path from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import {
  buildAgentEnv,
  buildControlEnv,
  dockerConfigJson,
  egressProxyUrl,
  ensureDockerConfig,
  isStrippedEnvName,
  type AgentEnvConfig,
} from '../src/env.js';
import { tempDir } from './helpers.js';

const SESSION = '01J9ZQ4X7V3N8K2M5P6R7S8T9V';

const config: AgentEnvConfig = {
  agentHome: '/home/agent',
  claudeConfigDir: '/home/agent/.claude',
  codexHome: '/home/agent/.codex',
  egressProxyUrl: 'http://oet-agent-egress:3128',
  noProxy: 'oet-agent-dockerproxy,oet-agent-dbproxy,localhost,127.0.0.1',
  dockerHost: 'tcp://oet-agent-dockerproxy:2375',
  dockerConfigRoot: '/run/oet-agent/docker',
  agentDatabaseUrl: 'postgres://oet_owner_agent:placeholder@oet-agent-dbproxy:5432/oet',
};

const parentEnv: NodeJS.ProcessEnv = {
  PATH: '/usr/local/bin:/usr/bin:/bin',
  LANG: 'en_GB.UTF-8',
  HOME: '/root',
  ANTHROPIC_API_KEY: 'x-anthropic',
  ANTHROPIC_BASE_URL: 'https://proxy.example.invalid',
  CLAUDE_CODE_USE_BEDROCK: '1',
  CLAUDE_CODE_USE_VERTEX: '1',
  OPENAI_API_KEY: 'x-openai',
  CODEX_API_KEY: 'x-codex',
  OWNER_AGENT_INTERNAL_TOKEN: 'x-internal',
  OWNER_AGENT_PROXY_TOKEN: 'x-proxy',
  OWNER_AGENT_OWNER_ACCOUNT_IDS: 'owner',
  AGENT_CONSOLE_DATA_DIR: '/var/lib/oet-agent',
  GH_TOKEN: 'x-gh',
  GITHUB_TOKEN: 'x-github',
  GIT_CONFIG_COUNT: '1',
  GIT_AUTHOR_NAME: 'OET Owner Agent',
  NODE_OPTIONS: '--max-old-space-size=512',
  SOME_RANDOM_VAR: 'should-not-pass',
};

describe('buildAgentEnv (allow-list)', () => {
  const env = buildAgentEnv(config, {
    sessionId: SESSION,
    base: parentEnv,
    extra: { ANTHROPIC_AUTH_TOKEN: 'x-extra', OWNER_AGENT_ANYTHING: 'x', ENGINE_FLAG: 'on' },
  });

  it('never leaks OWNER_AGENT_*, ANTHROPIC_*, CLAUDE_CODE_USE_*, OpenAI/Codex keys or GitHub tokens', () => {
    for (const name of Object.keys(env)) {
      expect(name, name).not.toMatch(/^(OWNER_AGENT_|ANTHROPIC_|CLAUDE_CODE_USE_|AGENT_CONSOLE_|GIT_CONFIG_)/);
    }
    for (const name of ['OPENAI_API_KEY', 'CODEX_API_KEY', 'GH_TOKEN', 'GITHUB_TOKEN', 'NODE_OPTIONS', 'SOME_RANDOM_VAR']) {
      expect(env[name], name).toBeUndefined();
    }
    for (const value of Object.values(env)) {
      expect(value).not.toMatch(/^x-(anthropic|openai|codex|internal|proxy|gh|github|extra)$/);
    }
  });

  it('keeps the allow-listed basics and engine homes', () => {
    expect(env.PATH).toBe(parentEnv.PATH);
    expect(env.LANG).toBe('en_GB.UTF-8');
    expect(env.HOME).toBe('/home/agent');
    expect(env.CLAUDE_CONFIG_DIR).toBe('/home/agent/.claude');
    expect(env.CODEX_HOME).toBe('/home/agent/.codex');
    expect(env.GIT_AUTHOR_NAME).toBe('OET Owner Agent');
    expect(env.ENGINE_FLAG).toBe('on');
    expect(env.CLAUDE_CODE_SUBPROCESS_ENV_SCRUB).toBe('1');
    expect(env.OET_AGENT_DATABASE_URL).toBe(config.agentDatabaseUrl);
  });

  it('attributes egress and docker traffic to the session', () => {
    expect(env.HTTPS_PROXY).toBe(`http://${SESSION}:x@oet-agent-egress:3128`);
    expect(env.HTTP_PROXY).toBe(env.HTTPS_PROXY);
    expect(env.https_proxy).toBe(env.HTTPS_PROXY);
    expect(env.NO_PROXY).toBe(config.noProxy);
    expect(env.DOCKER_HOST).toBe('tcp://oet-agent-dockerproxy:2375');
    expect(env.DOCKER_CONFIG).toBe(`/run/oet-agent/docker/${SESSION}`);
  });

  it('uses an unattributed proxy and no DOCKER_CONFIG without a session', () => {
    const plain = buildAgentEnv(config, { base: parentEnv });
    expect(plain.HTTPS_PROXY).toBe('http://oet-agent-egress:3128');
    expect(plain.DOCKER_CONFIG).toBeUndefined();
  });

  it('rejects non-ULID session ids (they end up in URLs and paths)', () => {
    expect(() => egressProxyUrl('http://oet-agent-egress:3128', '../etc')).toThrow();
    expect(() => dockerConfigJson('x"y')).toThrow();
  });

  it('classifies stripped names', () => {
    expect(isStrippedEnvName('ANTHROPIC_MODEL')).toBe(true);
    expect(isStrippedEnvName('OWNER_AGENT_X')).toBe(true);
    expect(isStrippedEnvName('PATH')).toBe(false);
  });
});

describe('per-session DOCKER_CONFIG', () => {
  let root: string | null = null;
  afterEach(() => {
    if (root) rmSync(root, { recursive: true, force: true });
    root = null;
  });

  it('writes HttpHeaders with the session id', async () => {
    root = tempDir();
    const dir = await ensureDockerConfig({ dockerConfigRoot: path.join(root, 'docker') }, SESSION);
    const parsed = JSON.parse(readFileSync(path.join(dir, 'config.json'), 'utf8')) as { HttpHeaders: Record<string, string> };
    expect(parsed.HttpHeaders['X-Oet-Agent-Session']).toBe(SESSION);
  });
});

describe('buildControlEnv', () => {
  it('never inherits the parent environment beyond PATH/LANG', () => {
    const env = buildControlEnv({ controlHome: '/var/lib/oet-agent/home', egressProxyUrl: 'http://oet-agent-egress:3128', noProxy: 'localhost' }, { GH_TOKEN: 'explicit' }, parentEnv);
    expect(env.GH_TOKEN).toBe('explicit');
    expect(env.OWNER_AGENT_INTERNAL_TOKEN).toBeUndefined();
    expect(env.ANTHROPIC_API_KEY).toBeUndefined();
    expect(env.HOME).toBe('/var/lib/oet-agent/home');
    expect(env.HTTPS_PROXY).toBe('http://oet-agent-egress:3128');
  });
});
