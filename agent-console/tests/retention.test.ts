import { describe, expect, it } from 'vitest';
import type { RunOptions, RunResult, Runner } from '../src/exec.js';
import { engineTranscriptDirs, pruneEngineTranscripts, pruneOpenCodeSessions, removeEngineTranscripts } from '../src/retention.js';
import { tempDir, testConfig } from './helpers.js';

function result(stdout = '', code = 0): RunResult {
  return { code, signal: null, stdout, stdoutBuffer: Buffer.from(stdout), stderr: '', stdoutTruncated: false, timedOut: false };
}

function recorder(stdout: string, code = 0) {
  const calls: { command: string; args: readonly string[]; options: RunOptions | undefined }[] = [];
  const run: Runner = async (command, args, options) => {
    calls.push({ command, args, options });
    return result(stdout, code);
  };
  return { calls, run };
}

describe('engine-native transcript retention', () => {
  const config = testConfig(tempDir());

  it('prunes old *.jsonl under the Claude and Codex transcript homes as the agent uid', async () => {
    const { calls, run } = recorder('/home/agent/.claude/projects/x/a.jsonl\n/home/agent/.codex/sessions/2026/01/01/rollout-b.jsonl\n');
    await expect(pruneEngineTranscripts(run, config, 90)).resolves.toBe(2);
    expect(calls).toHaveLength(1);
    expect(calls[0]!.command).toBe('find');
    expect(calls[0]!.args).toEqual([...engineTranscriptDirs(config), '-xdev', '-type', 'f', '-name', '*.jsonl', '-mtime', '+90', '-print', '-delete']);
    expect(calls[0]!.options?.asAgent).toBe(true);
    expect(engineTranscriptDirs(config)).toEqual([`${config.claudeConfigDir}/projects`, `${config.codexHome}/sessions`]);
  });

  it('treats a missing engine directory (find exit 1) as nothing to prune', async () => {
    const { run } = recorder('', 1);
    await expect(pruneEngineTranscripts(run, config, 90)).resolves.toBe(0);
    await expect(pruneEngineTranscripts(run, config, 0)).resolves.toBe(0);
  });

  it('erases only files named after a well-formed engine session id', async () => {
    const { calls, run } = recorder('/home/agent/.claude/projects/x/0a1b2c3d-1111-2222-3333-444455556666.jsonl\n');
    await expect(removeEngineTranscripts(run, config, ['0a1b2c3d-1111-2222-3333-444455556666', '*', '../etc'])).resolves.toBe(1);
    expect(calls).toHaveLength(1);
    expect(calls[0]!.args).toContain('*0a1b2c3d-1111-2222-3333-444455556666*.jsonl');
  });

  it('prunes only expired OpenCode sessions rooted under the console worktree directory', async () => {
    const now = Date.now();
    const listing = JSON.stringify([
      { id: 'ses_old123456', directory: `${config.worktreeRoot}/01JOLD`, updated: now - 120 * 86_400_000 },
      { id: 'ses_new123456', directory: `${config.worktreeRoot}/01JNEW`, updated: now },
      { id: 'ses_other1234', directory: '/home/agent/project', updated: now - 120 * 86_400_000 },
    ]);
    const calls: { command: string; args: readonly string[]; options: RunOptions | undefined }[] = [];
    const run: Runner = async (command, args, options) => {
      calls.push({ command, args, options });
      return result(command === 'find' ? `${config.worktreeRoot}/01JOLD\n` : args.includes('list') ? listing : '');
    };

    await expect(pruneOpenCodeSessions(run, config, 90)).resolves.toBe(1);
    expect(calls).toHaveLength(2);
    expect(calls[0]!.args).toEqual([config.worktreeRoot, '-xdev', '-mindepth', '1', '-maxdepth', '1', '-type', 'd', '-print']);
    expect(calls[1]!.args).toEqual(['--pure', 'session', 'list', '--format', 'json']);
    expect(calls[2]!.args).toEqual(['--pure', 'session', 'delete', 'ses_old123456']);
    expect(calls.every((call) => call.options?.asAgent)).toBe(true);
  });

  it('erases OpenCode native session IDs through the pinned CLI', async () => {
    const { calls, run } = recorder('');
    await expect(removeEngineTranscripts(run, config, ['ses_01JABCDEF123456789012345678'], undefined, 'opencode', `${config.worktreeRoot}/01JABC`)).resolves.toBe(1);
    expect(calls).toHaveLength(1);
    expect(calls[0]!.command).toBe(config.opencodeBinPath);
    expect(calls[0]!.args).toEqual(['--pure', 'session', 'delete', 'ses_01JABCDEF123456789012345678']);
    expect(calls[0]!.options?.asAgent).toBe(true);
  });
});
