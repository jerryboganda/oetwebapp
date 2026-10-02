import assert from 'node:assert/strict';
import test from 'node:test';
import { startSidecar } from '../shared/testing.mjs';

delete process.env.WRITING_CODEX_MODEL;
delete process.env.WRITING_CODEX_EFFORT;
const { base, calls, cli, module: { parseCodexLoginStatus } } = await startSidecar(new URL('./server.mjs', import.meta.url).href);

const agentMessage = (text) => JSON.stringify({ type: 'item.completed', item: { type: 'agent_message', text } });
const turnCompleted = (usage) => JSON.stringify({ type: 'turn.completed', usage });
const complete = (content) => fetch(`${base}/chat/completions`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ messages: [{ role: 'user', content }] }),
});
const failure = async (response) => [response.status, (await response.json()).error];

test('Codex sends large grading prompts through stdin, on GPT-6.1 Sol High by default', async () => {
  const prompt = 'clinical case-note fixture\n'.repeat(12000);
  assert.ok(Buffer.byteLength(prompt) > 131072);
  let received;
  cli.reply = (command, args, input) => {
    received = input;
    return { stdout: `${agentMessage('offline completion fixture')}\n` };
  };
  calls.length = 0;
  const response = await complete(prompt);
  assert.equal(response.status, 200);
  assert.equal((await response.json()).choices[0].message.content, 'offline completion fixture');
  assert.equal(received, prompt);
  assert.equal(calls.length, 1);
  const [{ command, args, options }] = calls;
  assert.equal(command, 'codex');
  assert.equal(args.at(-1), '-', 'Codex must read the prompt from stdin');
  assert.equal(options.stdio[0], 'pipe');
  assert.ok(args.includes('sandbox_mode="read-only"'));
  assert.ok(args.includes('model_reasoning_effort="high"'));
  assert.equal(args[args.indexOf('--model') + 1], 'gpt-6.1-sol');
});

test('a clean grade whose model text quotes "429" and "You have reached" succeeds', async () => {
  const text = 'Score 429/500. You have reached band B; a rate limit of one request per letter applies. Quota exceeded? No.';
  cli.reply = () => ({ stdout: [agentMessage(text), turnCompleted({ input_tokens: 4291, output_tokens: 1429 })].join('\n') });
  const response = await complete('grade this letter');
  assert.equal(response.status, 200);
  const body = await response.json();
  assert.equal(body.choices[0].message.content, text);
  assert.deepEqual(body.usage, { prompt_tokens: 4291, completion_tokens: 1429, total_tokens: 5720 });
});

test('a failed run is judged on stderr and error events only, never on the model text', async () => {
  cli.reply = () => ({ code: 1, stdout: `${agentMessage('You have reached the rate limit of this letter.')}\n`, stderr: 'thread panicked\n' });
  const [status, error] = await failure(await complete('grade this letter'));
  assert.equal(status, 502);
  assert.equal(error.code, 'engine_error');
});

test('quota text on stderr is a 429 quota_exceeded', async () => {
  cli.reply = () => ({ code: 1, stderr: "ERROR: You've hit your usage limit. Try again in 3 days.\n" });
  const [status, error] = await failure(await complete('grade this letter'));
  assert.equal(status, 429);
  assert.equal(error.code, 'quota_exceeded');
  assert.equal(error.type, 'rate_limit_error');
});

test('a dead login in an error event is a 401 auth_expired', async () => {
  cli.reply = () => ({
    code: 1,
    stdout: [
      JSON.stringify({ type: 'error', message: 'unexpected status 401 Unauthorized: Provided authentication token is expired.' }),
      JSON.stringify({ type: 'turn.failed', error: { message: 'unexpected status 401 Unauthorized' } }),
    ].join('\n'),
  });
  const [status, error] = await failure(await complete('grade this letter'));
  assert.equal(status, 401);
  assert.equal(error.code, 'auth_expired');
  assert.equal(error.type, 'authentication_error');
});

test('GET /readyz reports the Codex login, cached, and a dead login seen on a real request', async () => {
  let probes = 0;
  let next = { stdout: `${agentMessage('graded')}\n` };
  cli.reply = (command, args) => {
    if (args[0] === 'login') {
      probes += 1;
      assert.deepEqual(args, ['login', 'status']);
      return { stderr: 'Logged in using ChatGPT\n' };
    }
    return next;
  };
  const readyz = async () => {
    const response = await fetch(`${base}/readyz`);
    return [response.status, await response.json()];
  };

  assert.equal((await complete('letter')).status, 200);
  assert.deepEqual(await readyz(), [200, { ready: true, reason: null, queueDepth: 0, authOk: true, plan: null }]);

  next = { code: 1, stderr: 'Your access token could not be refreshed because your refresh token was already used. Please log out and sign in again.\n' };
  assert.equal((await complete('letter')).status, 401);
  assert.deepEqual(await readyz(), [503, { ready: false, reason: 'auth_expired', queueDepth: 0, authOk: false, plan: null }]);

  next = { stdout: `${agentMessage('graded again')}\n` };
  assert.equal((await complete('letter')).status, 200);
  assert.deepEqual(await readyz(), [200, { ready: true, reason: null, queueDepth: 0, authOk: true, plan: null }]);
  assert.equal(probes, 1, 'the login probe is cached');
});

test('codex login status: only "Logged in" / "Not logged in" decide, anything else is unknown', () => {
  assert.deepEqual(parseCodexLoginStatus({ code: 0, stdout: '', stderr: 'Logged in using ChatGPT\n' }), { authOk: true, plan: null });
  assert.deepEqual(parseCodexLoginStatus({ code: 1, stdout: '', stderr: 'Not logged in\n' }), { authOk: false, plan: null });
  assert.deepEqual(parseCodexLoginStatus({ code: 1, stdout: '', stderr: 'Error checking login status: permission denied' }), { authOk: null, plan: null });
  assert.deepEqual(parseCodexLoginStatus({ code: 0, stdout: '', stderr: '' }), { authOk: null, plan: null });
});
