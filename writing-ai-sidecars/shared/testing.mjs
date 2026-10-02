// Test-only helper (not a test file): runs a sidecar server module end to end with
// child_process.spawn replaced by a scripted fake CLI, so no claude/codex binary, AI call or
// network is ever involved.

import childProcess from 'node:child_process';
import { EventEmitter, once } from 'node:events';
import http from 'node:http';
import { syncBuiltinESMExports } from 'node:module';
import { PassThrough } from 'node:stream';
import { after, mock } from 'node:test';

/** Imports the server module at `url` on a random port. Every CLI run is recorded in `calls` and
 * answered by `cli.reply(command, args, input)` -> { code = 0, stdout = '', stderr = '' }, or a
 * promise of it (a test can hold a run open to keep the lane busy). */
export async function startSidecar(url) {
  const calls = [];
  const cli = { reply: () => ({}) };
  mock.method(childProcess, 'spawn', (command, args, options) => {
    calls.push({ command, args, options });
    const child = new EventEmitter();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    child.kill = () => true;
    const answer = async (input) => {
      const { code = 0, stdout = '', stderr = '' } = await cli.reply(command, args, input);
      child.stdout.end(stdout);
      child.stderr.end(stderr);
      setImmediate(() => child.emit('close', code));
    };
    if (options.stdio[0] === 'pipe') {
      let input = '';
      child.stdin = new PassThrough();
      child.stdin.on('data', (chunk) => { input += chunk.toString(); });
      child.stdin.on('finish', () => answer(input));
    } else {
      setImmediate(() => answer(null));
    }
    return child;
  });
  syncBuiltinESMExports();
  let server;
  const createServer = http.createServer;
  mock.method(http, 'createServer', (...args) => (server = createServer(...args)));
  process.env.PORT = '0';
  const module = await import(url);
  if (!server.listening) await once(server, 'listening');
  after(() => {
    server.close();
    mock.restoreAll();
    syncBuiltinESMExports();
  });
  return { base: `http://127.0.0.1:${server.address().port}`, calls, cli, module };
}
