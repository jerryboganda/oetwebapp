import { rmSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { branchName, capPatch, parseNameStatusZ, parseNumstatZ, Workspace } from '../src/workspace.js';
import type { RunOptions, RunResult, Runner } from '../src/exec.js';
import { silentLogger } from '../src/log.js';
import { testConfig, tempDir } from './helpers.js';

function ok(stdout = '', code = 0): RunResult {
  return { code, signal: null, stdout, stdoutBuffer: Buffer.from(stdout), stderr: '', stdoutTruncated: false, timedOut: false };
}

describe('workspace helpers', () => {
  it('builds agent/<yyyymmdd>-<slug>-<id> branch names', () => {
    const date = new Date('2026-09-27T10:00:00Z');
    expect(branchName('01J9ZQ4X7V3N8K2M5P6R7S8T9V', 'Fix the login bug!', date)).toBe('agent/20260927-fix-the-login-bug-7s8t9v');
    expect(branchName('01J9ZQ4X7V3N8K2M5P6R7S8T9V', '   ', date)).toBe('agent/20260927-session-7s8t9v');
    expect(branchName('01J9ZQ4X7V3N8K2M5P6R7S8T9V', 'Überprüfe Café ☕ endpoints', date)).toBe('agent/20260927-uberprufe-cafe-endpoints-7s8t9v');
    expect(branchName('01J9ZQ4X7V3N8K2M5P6R7S8T9V', 'x'.repeat(200), date).length).toBeLessThan(80);
  });

  it('parses numstat -z including renames and binary files', () => {
    const out = ['3\t1\tsrc/a.ts', '-\t-\tpublic/logo.png', '2\t2\t', 'old/name.ts', 'new/name.ts', ''].join('\0');
    const parsed = parseNumstatZ(out);
    expect(parsed.get('src/a.ts')).toEqual({ additions: 3, deletions: 1 });
    expect(parsed.get('public/logo.png')).toEqual({ additions: 0, deletions: 0 });
    expect(parsed.get('new/name.ts')).toEqual({ additions: 2, deletions: 2 });
  });

  it('parses name-status -z', () => {
    const out = ['M', 'src/a.ts', 'A', 'src/b.ts', 'D', 'src/c.ts', 'R087', 'old/name.ts', 'new/name.ts', ''].join('\0');
    expect(parseNameStatusZ(out)).toEqual([
      { path: 'src/a.ts', status: 'modified' },
      { path: 'src/b.ts', status: 'added' },
      { path: 'src/c.ts', status: 'deleted' },
      { path: 'new/name.ts', status: 'renamed' },
    ]);
  });

  it('caps patches at a line boundary', () => {
    const patch = 'line-1\nline-2\nline-3\n';
    expect(capPatch(patch, 1024)).toEqual({ patch, truncated: false });
    expect(capPatch(patch, 10)).toEqual({ patch: 'line-1\n', truncated: true });
  });
});

describe('Workspace git runs as the agent', () => {
  it('creates worktrees through as-agent with the allow-listed env', async () => {
    const root = tempDir();
    const config = testConfig(root);
    const calls: { command: string; args: readonly string[]; options: RunOptions | undefined }[] = [];
    const run: Runner = async (command, args, options) => {
      calls.push({ command, args, options });
      return ok();
    };
    const ws = new Workspace(config, run, silentLogger());
    const wt = await ws.createWorktree('01J9ZQ4X7V3N8K2M5P6R7S8T9V', 'Docs tweak', new Date('2026-09-27T00:00:00Z'));
    expect(wt.branch).toMatch(/^agent\/20260927-docs-tweak-/);
    expect(wt.path).toBe(`${config.worktreeRoot}/01J9ZQ4X7V3N8K2M5P6R7S8T9V`);
    expect(calls.length).toBeGreaterThan(0);
    for (const call of calls) {
      expect(call.options?.asAgent, `${call.command} ${call.args.join(' ')}`).toBe(true);
      expect(call.options?.env?.HOME).toBe(config.agentHome);
      expect(Object.keys(call.options?.env ?? {}).some((k) => k.startsWith('OWNER_AGENT_'))).toBe(false);
    }
    const add = calls.find((c) => c.args.includes('worktree') && c.args.includes('add'));
    expect(add?.args).toEqual(['-C', config.repoDir, 'worktree', 'add', '-b', wt.branch, wt.path, 'origin/main']);
    rmSync(root, { recursive: true, force: true });
  });

  it('falls back to the last fetched base when GitHub is unreachable (private repo, no token)', async () => {
    const root = tempDir();
    const config = testConfig(root);
    const calls: string[][] = [];
    const run: Runner = async (_command, args) => {
      calls.push([...args]);
      if (args.includes('fetch')) return { ...ok('', 128), stderr: 'fatal: could not read Username for https://github.com' };
      return ok();
    };
    const ws = new Workspace(config, run, silentLogger());
    const wt = await ws.createWorktree('01J9ZQ4X7V3N8K2M5P6R7S8T9V', 'Offline edit', new Date('2026-09-27T00:00:00Z'));
    expect(wt.branch).toMatch(/^agent\/20260927-offline-edit-/);
    expect(calls.some((a) => a.includes('--verify') && a.includes('refs/remotes/origin/main'))).toBe(true);
    expect(calls.some((a) => a.includes('worktree') && a.includes('add'))).toBe(true);
    rmSync(root, { recursive: true, force: true });
  });

  it('still fails when the fetch fails and no base was ever fetched', async () => {
    const root = tempDir();
    const config = testConfig(root);
    const run: Runner = async (_command, args) => {
      if (args.includes('fetch')) return { ...ok('', 128), stderr: 'fatal: could not read Username' };
      if (args.includes('--verify')) return ok('', 1);
      return ok();
    };
    const ws = new Workspace(config, run, silentLogger());
    await expect(ws.createWorktree('01J9ZQ4X7V3N8K2M5P6R7S8T9V', 'No base', new Date('2026-09-27T00:00:00Z'))).rejects.toThrow();
    rmSync(root, { recursive: true, force: true });
  });
});
