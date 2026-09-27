import { describe, expect, it } from 'vitest';
import type { RunOptions, RunResult, Runner } from '../src/exec.js';
import { engineTranscriptDirs, pruneEngineTranscripts, removeEngineTranscripts } from '../src/retention.js';
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

  it('prunes old *.jsonl under both engine homes as the agent uid', async () => {
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
});
