import assert from 'node:assert/strict';
import childProcess from 'node:child_process';
import { EventEmitter, once } from 'node:events';
import http from 'node:http';
import { syncBuiltinESMExports } from 'node:module';
import { PassThrough } from 'node:stream';
import test from 'node:test';
import { createSidecarServer } from './http.mjs';

for (const completionPath of ['/v1/chat/completions', '/v1/messages']) {
  for (const requestPath of [completionPath, completionPath.replace('/v1', '')]) {
    test(`accepts ${requestPath} for ${completionPath}`, async (context) => {
      let calls = 0;
      const server = createSidecarServer({
        engineName: 'protocol-test',
        completionPath,
        port: 0,
        onUsage: async () => ({}),
        onCompletion: async (body) => {
          calls += 1;
          assert.deepEqual(body, { messages: [{ role: 'user', content: 'protocol fixture' }] });
          return { accepted: true };
        },
      });
      context.after(() => server.close());
      await once(server, 'listening');
      const base = `http://127.0.0.1:${server.address().port}`;
      const response = await fetch(`${base}${requestPath}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ messages: [{ role: 'user', content: 'protocol fixture' }] }),
      });

      assert.equal(response.status, 200);
      assert.deepEqual(await response.json(), { accepted: true });
      assert.equal(calls, 1);

      const unknown = await fetch(`${base}/not-a-completion`, { method: 'POST', body: '{}' });
      assert.equal(unknown.status, 404);
      assert.equal(calls, 1);

      const invalid = await fetch(`${base}${requestPath}`, { method: 'POST', body: '{' });
      assert.equal(invalid.status, 400);
      assert.equal(calls, 1);
    });
  }
}

test('Codex sends large grading prompts through stdin', async (context) => {
  const prompt = 'clinical case-note fixture\n'.repeat(12000);
  assert.ok(Buffer.byteLength(prompt) > 131072);
  let received = '';
  let calls = 0;
  let server;
  const originalCreateServer = http.createServer;
  const previousPort = process.env.PORT;
  process.env.PORT = '0';
  context.after(() => {
    server?.close();
    if (previousPort === undefined) delete process.env.PORT;
    else process.env.PORT = previousPort;
    context.mock.restoreAll();
    syncBuiltinESMExports();
  });
  context.mock.method(http, 'createServer', (...args) => {
    server = originalCreateServer(...args);
    return server;
  });
  context.mock.method(childProcess, 'spawn', (command, args, options) => {
    calls += 1;
    assert.equal(command, 'codex');
    assert.ok(args.at(-1) === '-', 'Codex must read the prompt from stdin');
    assert.equal(options.stdio[0], 'pipe');
    assert.ok(args.includes('sandbox_mode="read-only"'));
    const child = new EventEmitter();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    child.stdin = new PassThrough();
    child.stdin.on('data', chunk => { received += chunk.toString(); });
    child.stdin.on('finish', () => {
      child.stdout.end(JSON.stringify({
        type: 'item.completed', item: { type: 'agent_message', text: 'offline completion fixture' },
      }) + '\n');
      child.emit('close', 0);
    });
    return child;
  });
  syncBuiltinESMExports();
  await import('../codex/server.mjs');
  if (!server.listening) await once(server, 'listening');
  const response = await fetch(`http://127.0.0.1:${server.address().port}/chat/completions`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ messages: [{ role: 'user', content: prompt }] }),
  });
  assert.equal(response.status, 200);
  assert.equal((await response.json()).choices[0].message.content, 'offline completion fixture');
  assert.equal(received, prompt);
  assert.equal(calls, 1);
});