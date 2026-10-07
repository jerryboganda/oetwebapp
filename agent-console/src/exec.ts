import { spawn } from 'node:child_process';

// Child-process runner used by the control plane. Never uses a shell, never
// inherits process.env (callers pass an explicit env), caps captured output,
// and can run the command as the agent uid through the as-agent wrapper.

export interface RunOptions {
  cwd?: string;
  /** Full environment of the child. Defaults to a minimal PATH/LANG env — never process.env. */
  env?: Record<string, string>;
  input?: string | Buffer;
  timeoutMs?: number;
  /** stdout capture cap in bytes (default 8 MiB). */
  maxOutputBytes?: number;
  /** Run through the as-agent wrapper (uid 10002). */
  asAgent?: boolean;
  /** Kill the whole tool process group on cancellation/timeout (Linux console). */
  killProcessGroup?: boolean;
  signal?: AbortSignal;
}

export interface RunResult {
  code: number | null;
  signal: NodeJS.Signals | null;
  stdout: string;
  stdoutBuffer: Buffer;
  stderr: string;
  stdoutTruncated: boolean;
  timedOut: boolean;
}

export type Runner = (command: string, args: readonly string[], options?: RunOptions) => Promise<RunResult>;

const DEFAULT_MAX_OUTPUT = 8 * 1024 * 1024;
const MAX_STDERR = 256 * 1024;
const DEFAULT_TIMEOUT = 120_000;

export function minimalEnv(): Record<string, string> {
  return {
    PATH: process.env.PATH || '/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin',
    LANG: 'C.UTF-8',
  };
}

export class CommandError extends Error {
  readonly result: RunResult;
  readonly command: string;

  constructor(command: string, result: RunResult, message?: string) {
    const detail = result.timedOut
      ? 'timed out'
      : `exited with ${result.code ?? result.signal ?? 'unknown status'}`;
    super(message ?? `${command} ${detail}: ${firstLine(result.stderr) || firstLine(result.stdout)}`.trim());
    this.name = 'CommandError';
    this.command = command;
    this.result = result;
  }
}

function firstLine(text: string): string {
  const line = text.split('\n').find((l) => l.trim().length > 0) ?? '';
  return line.length > 300 ? `${line.slice(0, 300)}…` : line;
}

export function createRunner(asAgentPath: string): Runner {
  return (command, args, options = {}) =>
    new Promise<RunResult>((resolve, reject) => {
      const file = options.asAgent ? asAgentPath : command;
      const argv = options.asAgent ? [command, ...args] : [...args];
      const maxOut = options.maxOutputBytes ?? DEFAULT_MAX_OUTPUT;
      const child = spawn(file, argv, {
        cwd: options.cwd,
        env: options.env ?? minimalEnv(),
        windowsHide: true,
        detached: options.killProcessGroup && process.platform !== 'win32',
      });

      const outChunks: Buffer[] = [];
      let outBytes = 0;
      let truncated = false;
      let stderr = '';
      let timedOut = false;
      let settled = false;

      child.stdout.on('data', (chunk: Buffer) => {
        if (outBytes >= maxOut) {
          truncated = true;
          return;
        }
        const room = maxOut - outBytes;
        if (chunk.length > room) {
          outChunks.push(chunk.subarray(0, room));
          outBytes += room;
          truncated = true;
        } else {
          outChunks.push(chunk);
          outBytes += chunk.length;
        }
      });
      child.stderr.on('data', (chunk: Buffer) => {
        if (stderr.length < MAX_STDERR) stderr += chunk.toString('utf8');
      });
      // A child that exits before reading its stdin must not crash us.
      child.stdin.on('error', () => undefined);

      const kill = (): void => {
        if (options.killProcessGroup && process.platform !== 'win32' && child.pid) {
          try { process.kill(-child.pid, 'SIGKILL'); } catch { child.kill('SIGKILL'); }
        } else if (child.exitCode === null && child.signalCode === null) child.kill('SIGKILL');
      };
      const timer = setTimeout(() => {
        timedOut = true;
        kill();
      }, options.timeoutMs ?? DEFAULT_TIMEOUT);
      timer.unref();
      const onAbort = (): void => kill();
      options.signal?.addEventListener('abort', onAbort, { once: true });
      if (options.signal?.aborted) kill();

      child.on('error', (error) => {
        clearTimeout(timer);
        options.signal?.removeEventListener('abort', onAbort);
        if (!settled) {
          settled = true;
          reject(error);
        }
      });
      child.on('close', (code, signal) => {
        clearTimeout(timer);
        options.signal?.removeEventListener('abort', onAbort);
        if (settled) return;
        settled = true;
        const stdoutBuffer = Buffer.concat(outChunks);
        resolve({
          code,
          signal,
          stdout: stdoutBuffer.toString('utf8'),
          stdoutBuffer,
          stderr: stderr.length > MAX_STDERR ? stderr.slice(0, MAX_STDERR) : stderr,
          stdoutTruncated: truncated,
          timedOut,
        });
      });

      if (options.input !== undefined) child.stdin.end(options.input);
      else child.stdin.end();
    });
}

/** Runs and throws CommandError unless the exit code is one of `okCodes` (default [0]). */
export async function runChecked(
  run: Runner,
  command: string,
  args: readonly string[],
  options: RunOptions = {},
  okCodes: readonly number[] = [0],
): Promise<RunResult> {
  const result = await run(command, args, options);
  if (result.timedOut || result.code === null || !okCodes.includes(result.code)) {
    throw new CommandError(command, result);
  }
  return result;
}
