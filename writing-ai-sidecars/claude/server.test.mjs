import assert from 'node:assert/strict';
import test from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { startSidecar } from '../shared/testing.mjs';

delete process.env.WRITING_CLAUDE_MODEL;
delete process.env.WRITING_CLAUDE_EFFORT;
const { base, calls, cli, module: { parseClaudeAuthStatus } } = await startSidecar(new URL('./server.mjs', import.meta.url).href);

// The CLI's `--output-format json` result event (claude-code 2.1.x).
const result = (text, extra = {}) => JSON.stringify({
  type: 'result', subtype: 'success', is_error: false, result: text, usage: { input_tokens: 4291, output_tokens: 429 }, ...extra,
});
const SIGNED_IN = '{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","subscriptionType":"max"}\n';
const messages = (content) => fetch(`${base}/v1/messages`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ messages: [{ role: 'user', content }] }),
});
const readyz = async () => {
  const response = await fetch(`${base}/readyz`);
  return [response.status, await response.json()];
};

test('Claude keeps the hardened CLI invocation, prompt on stdin, and hands the CLI an abort signal', async () => {
  let received;
  cli.reply = (command, args, input) => {
    received = input;
    return { stdout: result('graded') };
  };
  calls.length = 0;
  const response = await messages('letter text');
  assert.equal(response.status, 200);
  assert.equal((await response.json()).content[0].text, 'graded');
  assert.equal(received, 'letter text');
  const [{ command, args, options }] = calls;
  assert.equal(command, 'claude');
  assert.deepEqual(args, [
    '-p', '--output-format', 'json', '--model', 'claude-opus-5-5', '--effort', 'high',
    '--no-session-persistence', '--tools', '', '--allowedTools', '',
  ]);
  assert.equal(options.env.CLAUDE_CODE_DISABLE_AUTO_MEMORY, '1');
  assert.ok(options.signal instanceof AbortSignal);
});

test('a clean grade quoting "429", "rate limit" and "You have reached" in its text succeeds', async () => {
  const text = 'Score 429/500. You have reached your weekly limit of errors; rate limit wording is fine. Limit reached: no.';
  cli.reply = () => ({ stdout: result(text) });
  const response = await messages('grade this letter');
  assert.equal(response.status, 200);
  const body = await response.json();
  assert.equal(body.content[0].text, text);
  assert.deepEqual(body.usage, { input_tokens: 4291, output_tokens: 429 });
});

test('an error result with limit text is a 429 quota_exceeded', async () => {
  cli.reply = () => ({ code: 1, stdout: result('Claude AI usage limit reached|1760000000', { is_error: true }) });
  const response = await messages('grade this letter');
  assert.equal(response.status, 429);
  const { error } = await response.json();
  assert.equal(error.code, 'quota_exceeded');
  assert.match(error.message, /^Claude subscription quota\/rate limit: /);
});

test('GET /readyz probes the login outside the lane, caches it and reports a dead login seen on a request', { timeout: 15_000 }, async () => {
  let probes = 0;
  const answer = (completion) => (command, args) => {
    if (args[0] !== 'auth') return completion;
    probes += 1;
    assert.deepEqual(args, ['auth', 'status']);
    return { stdout: SIGNED_IN };
  };
  let release;
  cli.reply = answer(new Promise((resolve) => { release = resolve; }));

  // A grade holds the lane (its CLI never answers until released) and a second waits behind it:
  // /readyz and its first probe must still answer.
  calls.length = 0;
  const first = messages('first letter');
  for (let i = 0; i < 250 && !calls.some(({ args }) => args[0] === '-p'); i += 1) await delay(20);
  const second = messages('second letter');
  let ready;
  for (let i = 0; i < 100 && ready?.[1].queueDepth !== 1; i += 1) {
    if (i > 0) await delay(20);
    ready = await readyz();
  }
  assert.deepEqual(ready, [200, { ready: true, reason: null, queueDepth: 1, authOk: true, plan: 'max' }]);
  release({ stdout: result('graded') });
  assert.equal((await first).status, 200);
  assert.equal((await second).status, 200);

  cli.reply = answer({ code: 1, stdout: result('Invalid API key · Please run /login', { is_error: true }) });
  const dead = await messages('letter');
  assert.equal(dead.status, 401);
  assert.equal((await dead.json()).error.code, 'auth_expired');
  assert.deepEqual(await readyz(), [503, { ready: false, reason: 'auth_expired', queueDepth: 0, authOk: false, plan: 'max' }]);

  cli.reply = answer({ stdout: result('graded') });
  assert.equal((await messages('letter')).status, 200);
  assert.deepEqual(await readyz(), [200, { ready: true, reason: null, queueDepth: 0, authOk: true, plan: 'max' }]);
  assert.equal(probes, 1, 'the login probe is cached');
});

test('claude auth status: only an explicit loggedIn decides, anything unreadable is unknown', () => {
  assert.deepEqual(parseClaudeAuthStatus({ code: 0, stdout: SIGNED_IN, stderr: '' }), { authOk: true, plan: 'max' });
  assert.deepEqual(parseClaudeAuthStatus({ code: 1, stdout: '{"loggedIn": false}\n', stderr: '' }), { authOk: false, plan: null });
  assert.deepEqual(
    parseClaudeAuthStatus({ code: 0, stdout: 'Checking...\n{"loggedIn":true,"subscriptionType":""}\n', stderr: '' }),
    { authOk: true, plan: null },
  );
  for (const stdout of ['', 'not json', '{"loggedIn":"yes"}', 'null']) {
    assert.deepEqual(parseClaudeAuthStatus({ code: 1, stdout, stderr: '' }), { authOk: null, plan: null }, stdout);
  }
});
