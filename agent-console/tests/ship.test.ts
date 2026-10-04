import { rmSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import type { RunOptions, RunResult, Runner } from '../src/exec.js';
import { silentLogger } from '../src/log.js';
import { Redactor } from '../src/redact.js';
import {
  ShipExecutor,
  blockedShipFiles,
  countGitleaksFindings,
  isShippableBranch,
  parseHolders,
  serializeHolders,
  withHolder,
  withoutHolder,
} from '../src/ship.js';
import { Store } from '../src/store.js';
import { tempDir, testConfig } from './helpers.js';

function ok(stdout = '', code = 0, stderr = ''): RunResult {
  return { code, signal: null, stdout, stdoutBuffer: Buffer.from(stdout), stderr, stdoutTruncated: false, timedOut: false };
}

describe('ship: blocked file types', () => {
  it('blocks .sql .dump .csv .jsonl and every .env* file', () => {
    const changed = [
      'scripts/ops/create-owner-agent-db-role.sql',
      'backups/prod.dump',
      'exports/learners.CSV',
      'docs/canonical-rules/rows.jsonl',
      '.env',
      '.env.production',
      'config/.env.local',
      'src/app.ts',
      'docs/readme.md',
      'data/sample.json',
      'environment.ts',
    ];
    expect(blockedShipFiles(changed)).toEqual([
      'scripts/ops/create-owner-agent-db-role.sql',
      'backups/prod.dump',
      'exports/learners.CSV',
      'docs/canonical-rules/rows.jsonl',
      '.env',
      '.env.production',
      'config/.env.local',
    ]);
  });

  it('allows ordinary source changes', () => {
    expect(blockedShipFiles(['app/page.tsx', 'backend/src/X.cs', 'README.md'])).toEqual([]);
  });
});

describe('ship: branch rules', () => {
  it('only ships agent/* branches', () => {
    expect(isShippableBranch('agent/20260927-fix-login-7s8t9v')).toBe(true);
    expect(isShippableBranch('main')).toBe(false);
    expect(isShippableBranch('agent/../main')).toBe(false);
    expect(isShippableBranch('agent/x.lock')).toBe(false);
    expect(isShippableBranch('feature/x')).toBe(false);
    expect(isShippableBranch('agent/Upper')).toBe(false);
  });
});

describe('ship: PUBLIC_WINDOW_HOLDERS lease', () => {
  it('parses JSON arrays and tolerates comma lists', () => {
    expect(parseHolders('["agent-console:1","owner-pc"]')).toEqual(['agent-console:1', 'owner-pc']);
    expect(parseHolders('owner-pc, agent-console:2')).toEqual(['owner-pc', 'agent-console:2']);
    expect(parseHolders('')).toEqual([]);
    expect(parseHolders(null)).toEqual([]);
    expect(parseHolders('[]')).toEqual([]);
  });

  it('adds/removes holders idempotently and serializes deterministically', () => {
    const holders = withHolder(withHolder([], 'agent-console:b'), 'agent-console:a');
    expect(withHolder(holders, 'agent-console:a')).toEqual(holders);
    expect(serializeHolders(holders)).toBe('["agent-console:a","agent-console:b"]');
    expect(withoutHolder(holders, 'agent-console:a')).toEqual(['agent-console:b']);
    expect(serializeHolders([])).toBe('[]');
  });
});

describe('ship: visibility watchdog', () => {
  it('restores private only after the deadline and verified idle holders/Actions', async () => {
    const root = tempDir();
    const config = testConfig(root, { AGENT_CONSOLE_REPO: 'example-owner/example-repo' });
    const store = new Store({ dbPath: path.join(root, 'index.sqlite'), sessionsDir: path.join(root, 'sessions'), redactor: new Redactor() });
    const calls: { method: string; endpoint: string; body: unknown; env: Record<string, string> | undefined }[] = [];
    let holders = '["agent-console:ship-1","owner-pc"]';
    let queued = 0;
    let running = 0;
    let invalidCount = false;
    let queueError = false;
    let invalidHolders = false;
    let invalidVisibility = false;
    const run: Runner = async (command: string, args: readonly string[], options?: RunOptions) => {
      if (command !== 'gh') return ok();
      const method = args[2] as string;
      const endpoint = args[3] as string;
      calls.push({ method, endpoint, body: options?.input ? JSON.parse(String(options.input)) : undefined, env: options?.env });
      if (method === 'GET' && endpoint === 'repos/example-owner/example-repo') return ok(JSON.stringify(invalidVisibility ? {} : { private: false }));
      if (method === 'GET' && endpoint.endsWith('/actions/variables/PUBLIC_WINDOW_HOLDERS')) {
        return ok(JSON.stringify(invalidHolders ? {} : { name: 'PUBLIC_WINDOW_HOLDERS', value: holders }));
      }
      if (method === 'GET' && endpoint.includes('/actions/runs?')) {
        if (queueError) return ok('', 1, 'queue read unavailable');
        return ok(JSON.stringify(invalidCount ? {} : { total_count: endpoint.includes('status=queued') ? queued : running }));
      }
      return ok('');
    };
    let now = 1_000;
    const emitted: { sessionId: string; level: string }[] = [];
    const executor = new ShipExecutor({
      config,
      store,
      run,
      credentials: { shipEnv: async () => ({ GH_TOKEN: 'placeholder' }) },
      sessions: {
        requireSession: () => {
          throw new Error('not used');
        },
        isRunning: () => false,
        setPullRequest: () => undefined,
      },
      agentEnv: () => ({}),
      emit: (sessionId, data) => emitted.push({ sessionId, level: data.level }),
      redactor: new Redactor(),
      logger: silentLogger(),
      now: () => now,
    });
    store.setKv('public_window', { openedAt: 0, deadline: 5_000, holders: ['agent-console:ship-1'], sessionIds: ['S1'] });

    await expect(executor.watchdogTick()).resolves.toBe(false);
    expect(calls).toHaveLength(0);

    now = 6_000;
    await expect(executor.watchdogTick()).resolves.toBe(false);
    holders = '[]';
    queued = 1;
    await expect(executor.watchdogTick()).resolves.toBe(false);
    queued = 0;
    running = 1;
    await expect(executor.watchdogTick()).resolves.toBe(false);
    running = 0;
    invalidCount = true;
    await expect(executor.watchdogTick()).rejects.toThrow(/Cannot verify queued Actions/);
    invalidCount = false;
    queueError = true;
    await expect(executor.watchdogTick()).rejects.toThrow();
    queueError = false;
    invalidHolders = true;
    await expect(executor.watchdogTick()).rejects.toThrow(/Cannot verify lease holders/);
    invalidHolders = false;
    invalidVisibility = true;
    await expect(executor.watchdogTick()).rejects.toThrow(/Cannot verify repository visibility/);
    invalidVisibility = false;
    expect(calls.some((c) => c.method === 'PATCH')).toBe(false);
    expect(store.getKv('public_window')).not.toBeNull();

    await expect(executor.watchdogTick()).resolves.toBe(true);
    expect(calls).toContainEqual(expect.objectContaining({ method: 'PATCH', endpoint: 'repos/example-owner/example-repo', body: { visibility: 'private' } }));
    expect(calls.some((c) => c.endpoint.endsWith('/actions/variables/PUBLIC_WINDOW_HOLDERS') && c.method === 'PATCH')).toBe(false);
    expect(calls.every((c) => c.env?.GH_TOKEN === 'placeholder')).toBe(true);
    expect(store.getKv('public_window')).toBeNull();
    expect(emitted).toEqual([{ sessionId: 'S1', level: 'warn' }]);
    await expect(executor.watchdogTick()).resolves.toBe(false);

    store.close();
    rmSync(root, { recursive: true, force: true });
  });
});

describe('ship: gitleaks report', () => {
  it('counts findings and flags unreadable reports', () => {
    expect(countGitleaksFindings('[]')).toBe(0);
    expect(countGitleaksFindings('[{"RuleID":"generic-api-key"},{"RuleID":"jwt"}]')).toBe(2);
    expect(countGitleaksFindings('not json')).toBeNull();
    expect(countGitleaksFindings('{}')).toBeNull();
  });
});

describe('ship: apply-update dispatch', () => {
  function executorWith(run: Runner, shipToken: boolean) {
    const root = tempDir();
    const config = testConfig(root, { AGENT_CONSOLE_REPO: 'example-owner/example-repo' });
    const store = new Store({ dbPath: path.join(root, 'index.sqlite'), sessionsDir: path.join(root, 'sessions'), redactor: new Redactor() });
    const executor = new ShipExecutor({
      config,
      store,
      run,
      credentials: { shipEnv: async () => (shipToken ? { GH_TOKEN: 'placeholder' } : null) },
      sessions: {
        requireSession: () => {
          throw new Error('not used');
        },
        isRunning: () => false,
        setPullRequest: () => undefined,
      },
      agentEnv: () => ({}),
      emit: () => undefined,
      redactor: new Redactor(),
      logger: silentLogger(),
    });
    return {
      executor,
      cleanup: () => {
        store.close();
        rmSync(root, { recursive: true, force: true });
      },
    };
  }

  it('dispatches agent-console.yml on main with apply=true using the Ship token', async () => {
    const calls: { method: string; endpoint: string; body: unknown; env: Record<string, string> | undefined }[] = [];
    const run: Runner = async (command: string, args: readonly string[], options?: RunOptions) => {
      if (command === 'gh') {
        calls.push({ method: args[2] as string, endpoint: args[3] as string, body: options?.input ? JSON.parse(String(options.input)) : undefined, env: options?.env });
      }
      return ok('');
    };
    const { executor, cleanup } = executorWith(run, true);
    await expect(executor.dispatchConsoleUpdate()).resolves.toEqual({ dispatched: true });
    expect(calls).toEqual([
      {
        method: 'POST',
        endpoint: 'repos/example-owner/example-repo/actions/workflows/agent-console.yml/dispatches',
        body: { ref: 'main', inputs: { apply: 'true' } },
        env: { GH_TOKEN: 'placeholder' },
      },
    ]);
    cleanup();
  });

  it('reports (never throws) when the Ship token is missing or GitHub refuses', async () => {
    let ghCalls = 0;
    const refuse: Runner = async (command: string) => {
      if (command === 'gh') ghCalls += 1;
      return ok('', 1, 'gh: Resource not accessible by personal access token (HTTP 403)');
    };
    const missing = executorWith(refuse, false);
    const noToken = await missing.executor.dispatchConsoleUpdate();
    expect(noToken.dispatched).toBe(false);
    expect(ghCalls).toBe(0);
    missing.cleanup();

    const refused = executorWith(refuse, true);
    const denied = await refused.executor.dispatchConsoleUpdate();
    expect(denied.dispatched).toBe(false);
    expect(denied.detail).toMatch(/HTTP 403/);
    refused.cleanup();
  });
});
