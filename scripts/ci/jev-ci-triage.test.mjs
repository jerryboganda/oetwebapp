// Unit tests for scripts/ci/jev-ci-triage.mjs. No network, no secrets: GitHub is a
// fake fetch and Jev is a fake judge. Run with: node --test scripts/ci/jev-ci-triage.test.mjs
// Secret-shaped fixtures are assembled at runtime so no literal token sits in the repo.
import assert from 'node:assert/strict';
import test from 'node:test';
import { cleanLog, ERROR_CLASSES, formatSummary, LOG_TAIL_BYTES, scrubText, tailLines, triage } from './jev-ci-triage.mjs';

const GH_TOKEN = `gh${'p_'}${'a1B2c3D4'.repeat(5)}`;
const FAKE_KEY = `fake-${'k'.repeat(24)}-for-tests`;
const ENV = { TYPESAFE_API_KEY: FAKE_KEY, GITHUB_TOKEN: 'fake-github-token-for-tests' };
const INPUT = { runId: '42', workflow: 'QA Smoke', sha: 'abcdef1234567', repo: 'owner/repo' };

const RAW_LOG = [
  '2026-10-03T10:00:00.0000000Z ##[group]Run dotnet build',
  '2026-10-03T10:00:01.1111111Z \u001b[31merror CS1002: ; expected [backend/src/OetLearner.Api/Foo.cs]\u001b[0m',
  '2026-10-03T10:00:01.2222222Z notify dev@example.com password=hunter2value',
  `2026-10-03T10:00:01.3333333Z Authorization: Bearer ${GH_TOKEN}`,
  '2026-10-03T10:00:01.4444444Z retrying connection',
  '2026-10-03T10:00:01.5555555Z retrying connection',
  '2026-10-03T10:00:01.6666666Z retrying connection',
  '2026-10-03T10:00:02.0000000Z ##[error]Process completed with exit code 1.',
  '2026-10-03T10:00:03.0000000Z Post job cleanup.',
  '2026-10-03T10:00:03.1000000Z Cleaning up orphan processes',
].join('\n');

function jsonRes(body, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

/** Fake GitHub: opts.jobs / opts.files / opts.log, or opts.status to fail every call. */
function fakeGithub(opts = {}) {
  const calls = [];
  const fetchImpl = async (url) => {
    calls.push(String(url));
    if (opts.status) return new Response('boom', { status: opts.status });
    if (/\/actions\/jobs\/\d+\/logs/.test(url)) return new Response(opts.log ?? RAW_LOG, { status: 200 });
    if (url.includes('/jobs?')) {
      return jsonRes({
        jobs: opts.jobs ?? [
          { id: 1001, name: 'backend-tests', conclusion: 'failure', steps: [{ name: 'dotnet test', conclusion: 'failure' }] },
          { id: 1002, name: 'frontend-unit', conclusion: 'success', steps: [] },
        ],
      });
    }
    if (url.includes('/commits/')) return jsonRes({ files: opts.files ?? [{ filename: 'backend/src/OetLearner.Api/Foo.cs' }] });
    return new Response('{}', { status: 404 });
  };
  return { fetchImpl, calls };
}

function fakeJudge(answers) {
  const seen = [];
  const judge = async (site, state, questions) => {
    seen.push({ site, state, questions });
    if (answers instanceof Error) throw answers;
    return answers;
  };
  return { judge, seen };
}

const GOOD_ANSWERS = {
  error_class: { choice: 'compile', probabilities: { compile: 0.91, test_failure: 0.05 }, confidence: 0.91 },
  touches_change: { probability: 0.93 },
};

test('scrubText redacts emails, tokens, secret key=values, JWTs, long hex and mixed base64', () => {
  const jwt = `eyJ${'a'.repeat(12)}.${'b'.repeat(12)}.${'c'.repeat(12)}`;
  const b64 = 'Zm9vYmFyQmF6MTIzNDU2Nzg5MEFCQ0RFRkdISUpLTE1OTw';
  const hex = 'a1b2c3d4'.repeat(8);
  const pk = `-----BEGIN ${'RSA PRIVATE KEY'}-----\nMIIabc\n-----END ${'RSA PRIVATE KEY'}-----`;
  const out = scrubText(
    [
      'mail dev@example.com now',
      `token ${GH_TOKEN}`,
      'GITHUB_TOKEN=abcd1234efgh',
      "password: 'hunter2value'",
      'Authorization: Basic dXNlcjpwYXNzd29yZA==',
      jwt,
      `sha256:${hex}`,
      b64,
      pk,
    ].join('\n'),
  );
  for (const leaked of ['dev@example.com', GH_TOKEN, 'abcd1234efgh', 'hunter2value', 'dXNlcjpwYXNz', jwt, hex, b64, 'MIIabc']) {
    assert.ok(!out.includes(leaked), `leaked: ${leaked.slice(0, 12)}`);
  }
  assert.match(out, /<email>/);
  assert.match(out, /<private-key>/);
});

test('scrubText keeps ordinary compiler errors and long lowercase paths readable', () => {
  const line = "error CS1002: ; expected [backend/src/OetLearner.Api/Foo.cs] Unexpected end of file";
  assert.equal(scrubText(line), line);
  const path = 'backend/tests/oetlearnerapitests/writing/somethingverylongbutharmless';
  assert.equal(scrubText(path), path);
});

test('cleanLog strips ANSI and timestamps, drops post-error cleanup, collapses repeats', () => {
  const lines = cleanLog(RAW_LOG);
  const text = lines.join('\n');
  assert.match(text, /error CS1002/);
  assert.match(text, /##\[error\]Process completed with exit code 1\./);
  assert.match(text, /retrying connection \(x3\)/);
  assert.ok(!text.includes('Post job cleanup'));
  assert.ok(!text.includes('2026-10-03T'));
  assert.ok(!text.includes('\u001b'));
  assert.ok(!text.includes('dev@example.com'));
  assert.ok(!text.includes(GH_TOKEN));
});

test('tailLines keeps the last whole lines within the byte budget, deterministically', () => {
  const lines = Array.from({ length: 2000 }, (_, i) => `line-${String(i).padStart(4, '0')} ${'x'.repeat(50)}`);
  const a = tailLines(lines, LOG_TAIL_BYTES);
  const b = tailLines(lines, LOG_TAIL_BYTES);
  assert.deepEqual(a, b);
  assert.ok(Buffer.byteLength(a.join('\n')) <= LOG_TAIL_BYTES);
  assert.equal(a.at(-1), lines.at(-1));
  assert.match(a[0], /^line-\d{4} x+$/);
  assert.ok(a.length > 50 && a.length < lines.length);
});

test('triage: one batched Jev call over scrubbed state returns the verdict', async () => {
  const gh = fakeGithub();
  const jev = fakeJudge(GOOD_ANSWERS);
  const { verdict, detail } = await triage(INPUT, { fetch: gh.fetchImpl, judge: jev.judge, env: ENV });

  assert.equal(verdict.errorClass, 'compile');
  assert.equal(verdict.confidence, 0.91);
  assert.equal(verdict.touchesChange, true);
  assert.ok(verdict.logTailBytes > 0 && verdict.logTailBytes <= LOG_TAIL_BYTES + 200);
  assert.deepEqual(detail.failedJobs, ['backend-tests > dotnet test']);

  assert.equal(jev.seen.length, 1, 'exactly one Jev call');
  const { state, questions } = jev.seen[0];
  assert.deepEqual(Object.keys(state).sort(), ['changed_paths', 'failed_jobs', 'log_tail', 'workflow']);
  assert.deepEqual(state.changed_paths, ['backend/src/OetLearner.Api/Foo.cs']);
  assert.match(state.log_tail, /error CS1002/);
  for (const secret of ['dev@example.com', GH_TOKEN, 'hunter2value', FAKE_KEY]) assert.ok(!JSON.stringify(state).includes(secret));
  assert.deepEqual(Object.keys(questions.error_class.criteria).sort(), Object.keys(ERROR_CLASSES).sort());
  assert.equal(Object.keys(ERROR_CLASSES).length, 9);
  assert.equal(questions.touches_change.type, 'noul');
  assert.match(questions.error_class.instructions, /untrusted data/);
  assert.match(questions.touches_change.instructions, /untrusted data/);

  // Labels only: neither the verdict nor the summary may carry log text or any key.
  const printed = JSON.stringify(verdict) + formatSummary(INPUT, verdict, detail);
  for (const leaked of ['CS1002', 'dev@example.com', FAKE_KEY, ENV.GITHUB_TOKEN]) assert.ok(!printed.includes(leaked));
  assert.match(printed, /compile/);
});

test('triage: confidence below 0.6 degrades to unknown', async () => {
  const jev = fakeJudge({ ...GOOD_ANSWERS, error_class: { choice: 'compile', probabilities: { compile: 0.45 }, confidence: 0.45 } });
  const { verdict } = await triage(INPUT, { fetch: fakeGithub().fetchImpl, judge: jev.judge, env: ENV });
  assert.equal(verdict.errorClass, 'unknown');
  assert.equal(verdict.confidence, 0.45);
  assert.equal(verdict.reason, 'low_confidence');
});

test('triage: confidence falls back to the chosen option probability; unknown label stays unknown', async () => {
  const fromProbs = fakeJudge({ error_class: { choice: 'lint', probabilities: { lint: 0.8 }, confidence: null }, touches_change: { probability: 0.5 } });
  const a = await triage(INPUT, { fetch: fakeGithub().fetchImpl, judge: fromProbs.judge, env: ENV });
  assert.equal(a.verdict.errorClass, 'lint');
  assert.equal(a.verdict.touchesChange, null, 'a middling Noul is unsure, not a guess');

  const junk = fakeJudge({ error_class: { choice: 'banana', confidence: 0.99 } });
  const b = await triage(INPUT, { fetch: fakeGithub().fetchImpl, judge: junk.judge, env: ENV });
  assert.equal(b.verdict.errorClass, 'unknown');
  assert.equal(b.verdict.reason, 'unrecognised_label');
});

test('triage never throws: Jev failure, missing key, GitHub failure, bad input', async () => {
  const down = fakeJudge(new Error('gateway HTTP 503'));
  const a = await triage(INPUT, { fetch: fakeGithub().fetchImpl, judge: down.judge, env: ENV });
  assert.equal(a.verdict.errorClass, 'unknown');
  assert.equal(a.verdict.reason, 'jev_unavailable');

  const noKey = fakeJudge(GOOD_ANSWERS);
  const gh = fakeGithub();
  const b = await triage(INPUT, { fetch: gh.fetchImpl, judge: noKey.judge, env: { GITHUB_TOKEN: 'x' } });
  assert.equal(b.verdict.errorClass, 'unknown');
  assert.equal(b.verdict.reason, 'no_key');
  assert.equal(noKey.seen.length, 0);
  assert.equal(gh.calls.length, 0, 'no key: do not even spend GitHub API calls');

  const spent = fakeJudge(GOOD_ANSWERS);
  const c = await triage(INPUT, { fetch: fakeGithub({ status: 500 }).fetchImpl, judge: spent.judge, env: ENV });
  assert.equal(c.verdict.errorClass, 'unknown');
  assert.equal(c.verdict.reason, 'github_unavailable');
  assert.equal(spent.seen.length, 0, 'no evidence: do not spend Jev quota');

  const exploding = async () => { throw new Error('network down'); };
  const d = await triage(INPUT, { fetch: exploding, judge: spent.judge, env: ENV });
  assert.equal(d.verdict.errorClass, 'unknown');

  const e = await triage({ ...INPUT, runId: '42; rm -rf /' }, { fetch: gh.fetchImpl, judge: spent.judge, env: ENV });
  assert.equal(e.verdict.reason, 'bad_input');
  const f = await triage(undefined, { fetch: gh.fetchImpl, judge: spent.judge, env: ENV });
  assert.equal(f.verdict.errorClass, 'unknown');
});

test('triage: no changed paths means no touches_change question and a null answer', async () => {
  const jev = fakeJudge({ error_class: GOOD_ANSWERS.error_class });
  const { verdict } = await triage(INPUT, { fetch: fakeGithub({ files: [] }).fetchImpl, judge: jev.judge, env: ENV });
  assert.equal(jev.seen[0].questions.touches_change, undefined);
  assert.equal(verdict.errorClass, 'compile');
  assert.equal(verdict.touchesChange, null);
});

test('triage: an unreadable job log still classifies from the failed job and step names', async () => {
  const gh = fakeGithub({ log: '' });
  const jev = fakeJudge(GOOD_ANSWERS);
  const { verdict, detail } = await triage(INPUT, { fetch: gh.fetchImpl, judge: jev.judge, env: ENV });
  assert.equal(verdict.logTailBytes, 0);
  assert.equal(detail.tailLines, 0);
  assert.equal(jev.seen.length, 1);
  assert.deepEqual(jev.seen[0].state.failed_jobs, ['backend-tests > dotnet test']);
});
