import path from 'node:path';
import type { Runner } from '../exec.js';
import type { Redactor } from '../redact.js';
import type { EngineHooks, SessionEngineOptions, ToolCallRequest } from './types.js';

const text = { type: 'string' };
const integer = { type: 'integer', minimum: 1 };
const tool = (name: string, description: string, properties: Record<string, unknown>, required: string[]) =>
  ({ name, description, parameters: { type: 'object', properties, required, additionalProperties: false } });
export const gatewayTools = [
  tool('Read', 'Read a text file in the session worktree. Output is bounded; offset is a 1-based line number.', { file_path: text, offset: integer, limit: integer }, ['file_path']),
  tool('Write', 'Write a text file in the session worktree. Its parent directory must exist.', { file_path: text, content: text }, ['file_path', 'content']),
  tool('Edit', 'Replace an exact string in a worktree file. A non-unique match requires replace_all.', { file_path: text, old_string: text, new_string: text, replace_all: { type: 'boolean' } }, ['file_path', 'old_string', 'new_string']),
  tool('Bash', 'Run a guarded shell command in the session worktree as UID 10002. Network and Docker policies still apply. No local builds/tests/install.', { command: text, timeout: { ...integer, maximum: 120000 } }, ['command']),
];

// Trusted helper executes through the EXISTING runner/as-agent wrapper. It never
// reads or modifies files as root and refuses symlinks escaping the worktree.
const fileOperation = String.raw`
const fs = require('node:fs/promises');
const path = require('node:path');
(async () => {
  let raw = ''; for await (const part of process.stdin) raw += part;
  const { name, input } = JSON.parse(raw);
  const root = await fs.realpath(process.cwd());
  const target = path.resolve(root, input.file_path);
  const inside = p => p.startsWith(root + path.sep);
  if (!inside(target)) throw new Error('File path is outside the session worktree.');
  const parent = await fs.realpath(path.dirname(target));
  if (parent !== root && !inside(parent)) throw new Error('File parent escapes the session worktree.');
  let exists = true;
  try {
    const actual = await fs.realpath(target);
    if (!inside(actual)) throw new Error('Symlink escapes the session worktree.');
    if ((await fs.lstat(target)).isSymbolicLink()) throw new Error('Tool file target must not be a symlink.');
    if ((await fs.stat(target)).size > 2 * 1024 * 1024) throw new Error('File exceeds the 2 MiB tool limit.');
  } catch (e) { if (e.code !== 'ENOENT') throw e; exists = false; }
  if (name === 'Read') {
    const lines = (await fs.readFile(target, 'utf8')).split('\n');
    const start = (input.offset ?? 1) - 1;
    const limit = Math.min(input.limit ?? 200, 1000);
    process.stdout.write(lines.slice(start, start + limit).map((line, i) => (start + i + 1) + ': ' + line).join('\n'));
  } else if (name === 'Write') {
    await fs.writeFile(target, input.content, { flag: exists ? 'w' : 'wx' });
    process.stdout.write('File written.');
  } else if (name === 'Edit') {
    const original = await fs.readFile(target, 'utf8');
    const count = original.split(input.old_string).length - 1;
    if (count === 0 || (count > 1 && !input.replace_all)) throw new Error('Edit needs an exact, unambiguous match.');
    const updated = input.replace_all ? original.split(input.old_string).join(input.new_string) : original.replace(input.old_string, input.new_string);
    await fs.writeFile(target, updated);
    process.stdout.write('File edited.');
  }
})().catch(e => { process.stderr.write(e.message); process.exitCode = 1; });
`;

function string(input: Record<string, unknown>, key: string, max = 262144): string {
  const value = input[key];
  if (typeof value !== 'string' || value.length > max || value.includes('\0')) throw new Error('Invalid tool argument: ' + key);
  return value;
}

export async function runGatewayTool(id: string, name: string, input: Record<string, unknown>, options: SessionEngineOptions,
  hooks: EngineHooks, run: Runner, redactor: Redactor, signal: AbortSignal): Promise<string> {
  const request: ToolCallRequest = { toolCallId: id, name, input, cwd: options.cwd };
  let command = 'node';
  let args: string[] = ['-e', fileOperation];
  let timeout = 120000;
  try {
    if (name === 'Bash') {
      request.command = string(input, 'command', 32000);
      if (!request.command.trim()) throw new Error('Bash command is empty.');
      if (input['timeout'] !== undefined && (!Number.isInteger(input['timeout']) || Number(input['timeout']) < 1 || Number(input['timeout']) > 120000))
        throw new Error('Tool timeout must be 1..120000 milliseconds.');
      timeout = Number(input['timeout'] ?? 120000);
      command = '/bin/bash'; args = ['-c', request.command];
    } else {
      const file = string(input, 'file_path', 4096);
      const target = path.resolve(options.cwd, file);
      if (!target.startsWith(path.resolve(options.cwd) + path.sep)) throw new Error('File path is outside the session worktree.');
      if (name === 'Write') string(input, 'content');
      if (name === 'Edit') {
        if (!string(input, 'old_string')) throw new Error('Edit old_string is empty.');
        string(input, 'new_string');
        if (input['replace_all'] !== undefined && typeof input['replace_all'] !== 'boolean') throw new Error('replace_all must be boolean.');
      }
      for (const key of ['offset', 'limit']) if (input[key] !== undefined && (!Number.isInteger(input[key]) || Number(input[key]) < 1))
        throw new Error('Read offset/limit must be positive integers.');
      if (name === 'Write' || name === 'Edit') request.writePaths = [target];
    }
    signal.throwIfAborted();
    hooks.emit({ type: 'tool_call', data: { toolCallId: id, name, input: redactor.redactDeep(input),
      ...(request.command ? { command: redactor.redact(request.command) } : {}), cwd: options.cwd } });
    const decision = await hooks.onToolCall(request, signal);
    if (decision.behavior !== 'allow') {
      const output = redactor.redact('Denied: ' + decision.message);
      hooks.emit({ type: 'tool_result', data: { toolCallId: id, ok: false, output } });
      return output;
    }
    signal.throwIfAborted();
    const result = await run(command, args, { asAgent: true, cwd: options.cwd, env: options.env, timeoutMs: timeout,
      maxOutputBytes: 64 * 1024, signal, killProcessGroup: true,
      ...(name === 'Bash' ? {} : { input: JSON.stringify({ name, input }) }) });
    const output = redactor.redact((result.stdout + '\n' + result.stderr).slice(0, 65536)
      + (result.stdoutTruncated ? '\n[Output truncated]' : '') + (result.timedOut ? '\n[Operation timed out; inspect before any retry.]' : ''));
    const ok = result.code === 0 && !result.timedOut;
    hooks.emit({ type: 'tool_result', data: { toolCallId: id, ok, output, ...(result.code !== null ? { exitCode: result.code } : {}) } });
    if (ok && request.writePaths) for (const file of request.writePaths)
      hooks.emit({ type: 'file_change', data: { path: file, changeKind: 'modify' } });
    return JSON.stringify({ ok, output, exitCode: result.code });
  } catch (error) {
    if (signal.aborted) throw error;
    const output = redactor.redact(error instanceof Error ? error.message : 'Tool failed.').slice(0, 1000);
    hooks.emit({ type: 'tool_result', data: { toolCallId: id, ok: false, output } });
    return JSON.stringify({ ok: false, output });
  }
}
