import assert from 'node:assert/strict';
import { readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import {
  AuthExpiredError,
  LaneBusyError,
  Mutex,
  QuotaExceededError,
  cliError,
  errorText,
  loginProbe,
  looksLikeAuthExpired,
  looksLikeQuotaExceeded,
  runCli,
} from './engine.mjs';

const deferred = () => {
  let resolve;
  const promise = new Promise((r) => { resolve = r; });
  return { promise, resolve };
};

test('quota patterns match real limit messages, never a bare status code or a number', () => {
  for (const text of [
    "You've hit your usage limit. Upgrade to Pro or try again in 4 days 3 hours.",
    'You’ve hit your limit · resets 3pm (UTC)',
    'Claude AI usage limit reached|1760000000',
    '5-hour limit reached ∙ resets 3pm',
    'API Error: 429 {"type":"error","error":{"type":"rate_limit_error","message":"Rate limited"}}',
    'unexpected status 429 Too Many Requests: slow down',
    'You have reached your weekly limit',
  ]) assert.equal(looksLikeQuotaExceeded(text), true, text);
  for (const text of [
    '',
    undefined,
    'HTTP 429',
    'input_tokens: 4291, output_tokens: 1429',
    'an accurate limitation',
    'claude exited 1: segmentation fault',
  ]) assert.equal(looksLikeQuotaExceeded(text), false, String(text));
});

test('auth patterns match dead-login messages only', () => {
  for (const text of [
    'Invalid API key · Please run /login',
    'API Error: 401 {"type":"error","error":{"type":"authentication_error","message":"Invalid bearer token"}}',
    'OAuth token has expired. Please obtain a new token or refresh your existing token.',
    'unexpected status 401 Unauthorized: Missing bearer or basic authentication in header',
    'Your access token could not be refreshed because your refresh token was already used. Please log out and sign in again.',
    'Not logged in',
  ]) assert.equal(looksLikeAuthExpired(text), true, text);
  for (const text of [
    '',
    undefined,
    'Logged in using ChatGPT',
    'HTTP 401',
    'output_tokens: 401',
    'claude exited 1: segmentation fault',
  ]) assert.equal(looksLikeAuthExpired(text), false, String(text));
});

test('cliError types a failed run from its error text, quota before auth', () => {
  const quota = cliError('Codex', 'codex exited 1', "You've hit your usage limit. Please run /login");
  assert.ok(quota instanceof QuotaExceededError);
  assert.match(quota.message, /^Codex subscription quota\/rate limit: You've hit/);

  const auth = cliError('Claude', 'claude exited 1', 'Invalid API key · Please run /login');
  assert.ok(auth instanceof AuthExpiredError);
  assert.match(auth.message, /^Claude login expired or invalid: Invalid API key/);

  const tail = `${'x'.repeat(500)} segfault`;
  const other = cliError('Claude', 'claude exited 2', tail);
  assert.ok(!(other instanceof QuotaExceededError) && !(other instanceof AuthExpiredError));
  assert.equal(other.message, `claude exited 2: ${tail.slice(-400)}`);
});

test('errorText joins strings and JSON error values, skipping empty parts', () => {
  assert.equal(
    errorText('stderr line', null, '', undefined, { message: 'boom' }, ['a']),
    'stderr line\n{"message":"boom"}\n["a"]',
  );
});

test('Mutex runs one job at a time by default, in arrival order, and survives a failed job', async () => {
  const lane = new Mutex({ concurrency: 1, maxQueue: 5, queueWaitMs: 10_000 });
  const order = [];
  const gate = deferred();
  const a = lane.run(async () => { order.push('a:start'); await gate.promise; order.push('a:end'); return 'a'; });
  const b = lane.run(async () => { order.push('b'); return 'b'; });
  const cFailed = assert.rejects(lane.run(async () => { throw new Error('c failed'); }), /c failed/);
  const d = lane.run(async () => 'd');
  assert.equal(lane.queueDepth, 3);
  gate.resolve();
  assert.deepEqual(await Promise.all([a, b]), ['a', 'b']);
  await cFailed;
  assert.equal(await d, 'd');
  assert.deepEqual(order, ['a:start', 'a:end', 'b']);
  assert.equal(lane.queueDepth, 0);
});

test('Mutex with concurrency 2 runs two jobs together and queues the third', async () => {
  const lane = new Mutex({ concurrency: 2, maxQueue: 5, queueWaitMs: 10_000 });
  let running = 0;
  let peak = 0;
  const gates = [deferred(), deferred(), deferred()];
  const jobs = gates.map((gate) => lane.run(async () => {
    running += 1;
    peak = Math.max(peak, running);
    await gate.promise;
    running -= 1;
  }));
  assert.equal(running, 2);
  assert.equal(lane.queueDepth, 1);
  gates[0].resolve();
  await jobs[0];
  assert.equal(running, 2, 'the third job took the freed slot');
  gates[1].resolve();
  gates[2].resolve();
  await Promise.all(jobs);
  assert.equal(peak, 2);
});

test('Mutex refuses with LaneBusyError once the queue is full', async () => {
  const lane = new Mutex({ concurrency: 1, maxQueue: 1, queueWaitMs: 10_000 });
  const gate = deferred();
  const a = lane.run(() => gate.promise);
  const b = lane.run(async () => 'b');
  assert.equal(lane.full, true);
  await assert.rejects(lane.run(async () => 'c'), LaneBusyError);
  gate.resolve();
  await a;
  assert.equal(await b, 'b');
  assert.equal(lane.full, false);
});

test('Mutex drops a job that waited longer than queueWaitMs, without running it', async () => {
  const lane = new Mutex({ concurrency: 1, maxQueue: 5, queueWaitMs: 30 });
  const gate = deferred();
  const a = lane.run(() => gate.promise);
  let ranB = false;
  const b = lane.run(async () => { ranB = true; });
  await assert.rejects(b, LaneBusyError);
  assert.equal(lane.queueDepth, 0);
  gate.resolve();
  await a;
  assert.equal(ranB, false);
});

test('Mutex drops a queued job whose signal aborts, before it runs, and frees its slot', async () => {
  const lane = new Mutex({ concurrency: 1, maxQueue: 5, queueWaitMs: 10_000 });
  const gate = deferred();
  const a = lane.run(() => gate.promise);
  const controller = new AbortController();
  let ranB = false;
  const b = lane.run(async () => { ranB = true; }, { signal: controller.signal });
  const c = lane.run(async () => 'c');
  assert.equal(lane.queueDepth, 2);
  controller.abort();
  await assert.rejects(b, { name: 'AbortError' });
  assert.equal(lane.queueDepth, 1);
  gate.resolve();
  await a;
  assert.equal(await c, 'c');
  assert.equal(ranB, false);

  // An already-aborted signal never queues or runs.
  await assert.rejects(lane.run(async () => { ranB = true; }, { signal: AbortSignal.abort() }), { name: 'AbortError' });
  assert.equal(ranB, false);
});

test('Mutex bounds come from the environment and junk values fall back to the defaults', (t) => {
  const keys = ['WRITING_LANE_CONCURRENCY', 'WRITING_QUEUE_MAX', 'WRITING_QUEUE_WAIT_MS'];
  const saved = keys.map((key) => process.env[key]);
  t.after(() => keys.forEach((key, i) => {
    if (saved[i] === undefined) delete process.env[key];
    else process.env[key] = saved[i];
  }));
  const bounds = ({ concurrency, maxQueue, queueWaitMs }) => ({ concurrency, maxQueue, queueWaitMs });

  keys.forEach((key) => delete process.env[key]);
  assert.deepEqual(bounds(new Mutex()), { concurrency: 1, maxQueue: 40, queueWaitMs: 420000 });
  Object.assign(process.env, { WRITING_LANE_CONCURRENCY: '2', WRITING_QUEUE_MAX: '3', WRITING_QUEUE_WAIT_MS: '1000' });
  assert.deepEqual(bounds(new Mutex()), { concurrency: 2, maxQueue: 3, queueWaitMs: 1000 });
  Object.assign(process.env, { WRITING_LANE_CONCURRENCY: '0', WRITING_QUEUE_MAX: 'abc', WRITING_QUEUE_WAIT_MS: '-5' });
  assert.deepEqual(bounds(new Mutex()), { concurrency: 1, maxQueue: 40, queueWaitMs: 420000 });
});

test('runCli captures stdout, stderr and the exit code and feeds input on stdin', async () => {
  const script = 'process.stdin.pipe(process.stdout); process.stderr.write("err"); process.exitCode = 3;';
  assert.deepEqual(
    await runCli(process.execPath, ['-e', script], { input: 'hello', timeoutMs: 10_000 }),
    { code: 3, stdout: 'hello', stderr: 'err' },
  );
});

test('runCli SIGKILLs the child when the signal aborts', { timeout: 20_000 }, async (t) => {
  const pidFile = join(tmpdir(), `writing-ai-runcli-${process.pid}-${Date.now()}.pid`);
  // Ignores SIGTERM, so only a SIGKILL ends it; the pid is written last, once the handler is in place.
  const script = [
    "process.on('SIGTERM', () => {});",
    'setInterval(() => {}, 1000);',
    `require('fs').writeFileSync(${JSON.stringify(pidFile)}, String(process.pid));`,
  ].join(' ');
  const controller = new AbortController();
  const run = runCli(process.execPath, ['-e', script], { timeoutMs: 60_000, signal: controller.signal });
  run.catch(() => {});
  let pid = 0;
  t.after(() => {
    rmSync(pidFile, { force: true });
    try { if (pid > 0) process.kill(pid, 'SIGKILL'); } catch { /* already gone */ }
  });
  while (!(pid > 0)) {
    try { pid = Number(readFileSync(pidFile, 'utf8')); } catch { /* not written yet */ }
    if (!(pid > 0)) await delay(20);
  }

  controller.abort();
  await assert.rejects(run, { name: 'AbortError' });
  for (let i = 0; ; i += 1) {
    try {
      process.kill(pid, 0);
    } catch (err) {
      assert.equal(err.code, 'ESRCH');
      break;
    }
    assert.ok(i < 200, 'the child outlived the abort');
    await delay(25);
  }
});

test('loginProbe caches 60 s, treats an unreadable run as unknown and expires a request failure', async (t) => {
  t.mock.timers.enable({ apis: ['Date'], now: 1_000_000 });
  let calls = 0;
  let next = { code: 0, stdout: 'in', stderr: '' };
  const run = async (cmd, args, options) => {
    calls += 1;
    assert.deepEqual([cmd, args, options], ['cli', ['status'], { timeoutMs: 5000 }]);
    if (next instanceof Error) throw next;
    return next;
  };
  const parse = ({ stdout }) => {
    if (stdout === 'junk') throw new Error('unparseable');
    return { authOk: stdout === 'in', plan: 'max' };
  };
  const probe = loginProbe('cli', ['status'], parse, run);

  assert.deepEqual(await probe.status(), { authOk: true, plan: 'max' });
  assert.deepEqual(await probe.status(), { authOk: true, plan: 'max' });
  assert.equal(calls, 1, 'cached');

  probe.failed();
  assert.deepEqual(await probe.status(), { authOk: false, plan: 'max' });
  probe.succeeded();
  assert.deepEqual(await probe.status(), { authOk: true, plan: 'max' });

  probe.failed();
  t.mock.timers.tick(60_000);
  assert.deepEqual(await probe.status(), { authOk: true, plan: 'max' }, 'a request failure expires with the cache');
  assert.equal(calls, 2);

  next = new Error('claude timed out after 5000ms');
  t.mock.timers.tick(60_000);
  assert.deepEqual(await probe.status(), { authOk: null, plan: null });
  next = { code: 1, stdout: 'junk', stderr: '' };
  t.mock.timers.tick(60_000);
  assert.deepEqual(await probe.status(), { authOk: null, plan: null });
  assert.equal(calls, 4);
});
