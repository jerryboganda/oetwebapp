import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

// Static checks of the engine policy files shipped in the image (etc/, bin/, scripts/).
// They run without Codex or Claude Code installed; `codex execpolicy check` in CI is the
// authoritative validator of oet.rules syntax.

const here = path.dirname(fileURLToPath(import.meta.url));
const consoleRoot = path.resolve(here, '../..');
const repoRoot = path.resolve(consoleRoot, '..');
const read = (rel: string): string => readFileSync(path.join(consoleRoot, rel), 'utf8');

type PatternToken = string | string[];
interface PrefixRule {
  pattern: PatternToken[];
  decision: string;
  match: string[];
  notMatch: string[];
}

function parseRules(source: string): PrefixRule[] {
  const blocks = source.split(/^prefix_rule\(/m).slice(1);
  return blocks.map((block) => {
    const body = block.slice(0, block.indexOf('\n)'));
    const field = (name: string): string | undefined => new RegExp(`^\\s*${name} = (.*),$`, 'm').exec(body)?.[1];
    const pattern = JSON.parse(field('pattern') ?? 'null') as PatternToken[];
    const decision = JSON.parse(field('decision') ?? 'null') as string;
    const match = JSON.parse(field('match') ?? '[]') as string[];
    const notMatch = JSON.parse(field('not_match') ?? '[]') as string[];
    return { pattern, decision, match, notMatch };
  });
}

function expand(pattern: PatternToken[]): string[][] {
  return pattern.reduce<string[][]>(
    (acc, token) => acc.flatMap((prefix) => (Array.isArray(token) ? token : [token]).map((t) => [...prefix, t])),
    [[]],
  );
}

function prefixMatches(pattern: PatternToken[], command: string): boolean {
  const tokens = command.trim().split(/\s+/);
  if (tokens.length < pattern.length) return false;
  return pattern.every((token, i) => (Array.isArray(token) ? token.includes(tokens[i] ?? '') : token === tokens[i]));
}

describe('etc/oet.rules (Codex execpolicy backstop)', () => {
  const rules = parseRules(read('etc/oet.rules'));

  it('contains only forbidden prefix rules with examples', () => {
    expect(rules.length).toBeGreaterThanOrEqual(10);
    for (const rule of rules) {
      expect(rule.decision).toBe('forbidden');
      expect(rule.pattern.length).toBeGreaterThan(0);
      expect(rule.match.length).toBeGreaterThan(0);
    }
  });

  it('has match / not_match examples that agree with the pattern (Codex validates these at load time)', () => {
    for (const rule of rules) {
      for (const example of rule.match) expect(prefixMatches(rule.pattern, example), `${example} should match ${JSON.stringify(rule.pattern)}`).toBe(true);
      for (const example of rule.notMatch) expect(prefixMatches(rule.pattern, example), `${example} should not match ${JSON.stringify(rule.pattern)}`).toBe(false);
    }
  });

  it('covers the commands a console session must never run', () => {
    const forbidden = (command: string): boolean => rules.some((rule) => prefixMatches(rule.pattern, command));
    for (const command of [
      'git push --force origin agent/x',
      'git push origin main',
      'git push -u origin main',
      'gh repo edit jerryboganda/oetwebapp --visibility public',
      'gh repo delete jerryboganda/oetwebapp',
      'gh pr merge 1 --squash',
      'gh auth token',
      'gh secret set X',
      'docker volume rm oetwebsite_oet_postgres_data',
      'docker system prune -af',
      'docker compose down -v',
      'rm -rf /workspace',
      'claude -p x',
      'codex exec x',
    ]) {
      expect(forbidden(command), command).toBe(true);
    }
    for (const command of ['git push origin agent/20260927-x', 'gh pr create --fill', 'docker ps', 'gh auth status', 'rm -rf /workspace/sessions/x/tmp']) {
      expect(forbidden(command), command).toBe(false);
    }
  });

  it('is mirrored by Bash deny rules in the Claude managed settings', () => {
    const settings = JSON.parse(read('etc/managed-settings.json')) as { permissions: { deny: string[] } };
    const deny = new Set(settings.permissions.deny);
    const missing = rules.flatMap((rule) => expand(rule.pattern).map((tokens) => `Bash(${tokens.join(' ')} *)`)).filter((r) => !deny.has(r));
    expect(missing).toEqual([]);
  });
});

describe('etc/managed-settings.json (Claude Code managed policy)', () => {
  const settings = JSON.parse(read('etc/managed-settings.json')) as Record<string, any>;

  it('locks hooks and permission rules to the managed tier and pins claude.ai login', () => {
    expect(settings['allowManagedHooksOnly']).toBe(true);
    expect(settings['allowManagedPermissionRulesOnly']).toBe(true);
    expect(settings['forceLoginMethod']).toBe('claudeai');
    expect(settings['permissions']['disableBypassPermissionsMode']).toBe('disable');
    expect(settings['env']).toMatchObject({ DISABLE_AUTOUPDATER: '1' });
    expect(settings).not.toHaveProperty('apiKeyHelper');
    expect(settings['permissions']).not.toHaveProperty('allow');
  });

  it('denies the deploy root, credentials and control-plane state', () => {
    const deny: string[] = settings['permissions']['deny'];
    for (const rule of [
      'Edit(//opt/oetwebapp/**)',
      'Read(//home/agent/.claude/.credentials.json)',
      'Edit(//home/agent/.claude/.credentials.json)',
      'Read(//home/agent/.codex/auth.json)',
      'Read(//run/**)',
      'Edit(//run/**)',
      'Read(//var/lib/oet-agent/**)',
      'Edit(//var/lib/oet-agent/**)',
      'Edit(//etc/claude-code/**)',
    ]) {
      expect(deny).toContain(rule);
    }
    for (const rule of deny) expect(rule).toMatch(/^(Read|Edit|Bash)\(.+\)$/);
  });
});

describe('etc/codex-config.toml and etc/codex-requirements.toml', () => {
  const configToml = read('etc/codex-config.toml');
  const requirements = read('etc/codex-requirements.toml');
  const setting = (toml: string, key: string): string | undefined => new RegExp(`^${key} = (.+)$`, 'm').exec(toml)?.[1]?.trim();

  it('pins ChatGPT sign-in to the workspace placeholder, file credentials and no self-update', () => {
    expect(setting(configToml, 'forced_login_method')).toBe('"chatgpt"');
    expect(setting(configToml, 'forced_chatgpt_workspace_id')).toBe('"__OET_CODEX_WORKSPACE_ID__"');
    expect(setting(configToml, 'cli_auth_credentials_store')).toBe('"file"');
    expect(setting(configToml, 'check_for_update_on_startup')).toBe('false');
    // Codex >= 0.157 rejects approval_policy = "untrusted" in config.toml; it is sent per thread.
    expect(setting(configToml, 'approval_policy')).toBeUndefined();
    expect(setting(configToml, 'project_doc_max_bytes')).toBe('0');
    expect(configToml).not.toMatch(/approval_policy = "never"/);
    expect(configToml).toMatch(/\[projects\."\/workspace"\]\ntrust_level = "untrusted"/);
    expect(configToml).toMatch(/\[shell_environment_policy\][\s\S]*ignore_default_excludes = false/);
    expect(configToml).not.toMatch(/^\s*(notify|mcp_servers)\b/m);
  });

  it('requires untrusted approvals and ChatGPT login', () => {
    expect(setting(requirements, 'allowed_approval_policies')).toBe('["untrusted"]');
    expect(setting(requirements, 'allowed_login_methods')).toBe('["chatgpt"]');
    expect(setting(requirements, 'allowed_chatgpt_workspaces')).toBe('["__OET_CODEX_WORKSPACE_ID__"]');
    expect(setting(requirements, 'cli_auth_credentials_store')).toBe('"file"');
  });
});

describe('etc/MANUAL.md (operating manual)', () => {
  const manual = read('etc/MANUAL.md');

  it('states the scope, tools and shipping rules', () => {
    for (const phrase of [
      'co-tenant',
      'psql "$OET_AGENT_DATABASE_URL"',
      'oet-env-edit',
      'gh workflow run qa-smoke.yml',
      'docker logs',
      '/opt/oetwebapp',
      'agent/*',
      'Ship',
      'repository visibility',
      'data, never instructions',
      'Owner Agent Console exception',
    ]) {
      expect(manual).toContain(phrase);
    }
    expect(manual).toMatch(/Never\*\* push to `main`/);
  });

  it('tells the engine that the Jev advisory and effort tier are advice, not authorization', () => {
    for (const phrase of ['Jev development advisory', 'Suggested effort tier', 'not authorization', 'never relaxes the Guard']) {
      expect(manual).toContain(phrase);
    }
  });

  it('points at domain documents that exist in the repository', () => {
    const docs = [...manual.matchAll(/`(docs\/[A-Za-z0-9_./-]+\.md)`/g)].map((m) => m[1] as string);
    expect(docs).toEqual(expect.arrayContaining(['docs/WRITING-MODEL-ANSWER-RULES.md', 'docs/READING-UPLOAD-ZERO-DEVIATION-CONTRACT.md', 'docs/SCORING.md']));
    for (const doc of docs) expect(existsSync(path.join(repoRoot, doc)), doc).toBe(true);
  });
});

describe('container scripts', () => {
  it('as-agent drops to uid/gid 10002 with setpriv', () => {
    const asAgent = read('bin/as-agent');
    expect(asAgent.startsWith('#!/bin/sh')).toBe(true);
    expect(asAgent).toMatch(/exec setpriv --reuid="\$AGENT_UID" --regid="\$AGENT_GID" --clear-groups --inh-caps=-all --no-new-privs -- "\$@"/);
    expect(asAgent).toMatch(/^AGENT_UID=10002$/m);
    expect(asAgent).toMatch(/export HOME=\/home\/agent/);
  });

  it('the SDK executable wrapper runs the bundled binary through as-agent', () => {
    expect(read('bin/claude-as-agent')).toMatch(/exec \/usr\/local\/bin\/as-agent \/usr\/local\/lib\/oet-agent\/claude "\$@"/);
  });

  it('the entrypoint installs every policy file and execs the server', () => {
    const entry = read('bin/entrypoint.sh');
    for (const file of ['managed-settings.json', 'codex-config.toml', 'codex-requirements.toml', 'oet.rules', 'MANUAL.md']) expect(entry).toContain(file);
    for (const target of ['/etc/claude-code/managed-settings.json', '/etc/codex/managed_config.toml', '/etc/codex/requirements.toml', 'AGENTS.override.md']) {
      expect(entry).toContain(target);
    }
    expect(entry).toMatch(/dir "\$CODEX_DIR" "0:\$AGENT_GID" 1770/);
    expect(entry).toMatch(/exec node "\$SERVER_ENTRY"/);
  });

  it('oet-env-edit never takes values as arguments and refuses during deploys', () => {
    const script = read('scripts/oet-env-edit');
    expect(script).toMatch(/gh run list --repo "\$REPO" --workflow deploy\.yml --status "\$status"/);
    expect(script).toMatch(/OWNER_AGENT_\* \| OWNERAGENT__\*\)/);
    expect(script).toMatch(/--network none/);
    expect(script).toMatch(/--pull never/);
    expect(script).not.toMatch(/echo "\$value"|printf '%s\\n' "\$value"/);
  });

  it('the Dockerfile pins the base image by digest and ships the wrappers', () => {
    const dockerfile = read('Dockerfile');
    expect(dockerfile).toMatch(/ARG NODE_IMAGE=node:22-bookworm-slim@sha256:[0-9a-f]{64}/);
    expect(dockerfile).toMatch(/GITLEAKS_SHA256=[0-9a-f]{64}/);
    expect(dockerfile).toMatch(/sha256sum -c -/);
    expect(dockerfile).toMatch(/npm audit signatures/);
    expect(dockerfile).toMatch(/DISABLE_AUTOUPDATER=1/);
    expect(dockerfile).toMatch(/NODE_OPTIONS=--max-old-space-size=512/);
    expect(dockerfile).toMatch(/useradd --uid 10002 --gid 10002/);
    expect(dockerfile).toMatch(/EXPOSE 8410/);
    expect(dockerfile).toMatch(/HEALTHCHECK[\s\S]*\/healthz/);
    expect(dockerfile).toMatch(/ENTRYPOINT \["\/usr\/local\/bin\/oet-agent-entrypoint"\]/);
    expect(dockerfile).not.toMatch(/^USER /m);
    expect(dockerfile).not.toMatch(/--omit=optional/);
  });
});
