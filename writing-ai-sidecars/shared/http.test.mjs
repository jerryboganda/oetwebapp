import assert from 'node:assert/strict';
import { once } from 'node:events';
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