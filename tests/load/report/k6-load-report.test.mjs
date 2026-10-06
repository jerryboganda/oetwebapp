import assert from 'node:assert/strict';
import test from 'node:test';
import {
  SCOPE_STATEMENT, buildModel, buildTargetRows, main, parseArgs, pooledRate, renderReport, sumCounter, validateSummary,
  worstTrend,
} from './k6-load-report.mjs';

const trend = (p95, p99 = p95 * 2) => ({ type: 'trend', contains: 'time', values: { 'p(95)': p95, 'p(99)': p99 }, thresholds: {} });
const counter = (count) => ({ type: 'counter', contains: 'default', values: { count }, thresholds: {} });
const rate = (passes, fails) => ({ type: 'rate', contains: 'default', values: { rate: passes / Math.max(1, passes + fails), passes, fails }, thresholds: {} });

function leg(overrides = {}) {
  const {
    profile = 'steady', index = 0, read = 300, save = 200, submit = 250, live = 600, fail = [2, 9998], established = [9995, 5],
    lost = 0, idempotency = 0, drop = [], extra = {}, thresholdsFailed = [], meta = {},
  } = overrides;
  const metrics = {
    'http_req_duration{class:critical-read,phase:steady}': trend(read, read * 2),
    'http_req_duration{class:exam-save,phase:steady}': trend(save),
    'http_req_duration{class:submission,phase:steady}': trend(submit),
    'http_req_duration{class:live-setup,phase:steady}': trend(live),
    'oet_class_total{class:critical-read,phase:steady}': counter(5000),
    'oet_class_total{class:exam-save,phase:steady}': counter(900),
    'oet_class_total{class:submission,phase:steady}': counter(120),
    'oet_class_total{class:live-setup,phase:steady}': counter(300),
    'oet_unexpected_failure{phase:steady}': rate(fail[0], fail[1]),
    'oet_established_ok{phase:steady}': rate(established[0], established[1]),
    oet_lost_ack_save: counter(lost),
    oet_idempotency_violation: counter(idempotency),
    oet_credit_consumed_while_queued: counter(0),
    oet_timer_started_while_queued: counter(0),
    'oet_flow_started{flow:browse}': counter(200),
    'oet_flow_completed{flow:browse}': counter(200),
    'oet_flow_skipped{flow:browse}': counter(0),
    ...extra,
  };
  for (const key of drop) delete metrics[key];
  return {
    schema: 'oet-load-summary/1',
    meta: { profile, leg: index, legCount: 1, totalLearners: 1000, k6Version: 'k6 v1.x', runId: '42', sha: 'abc123', ...meta },
    durationMs: 90 * 60 * 1000,
    metrics,
    thresholdsFailed,
    passed: thresholdsFailed.length === 0,
  };
}

test('a clean run passes and states its scope on the first lines', () => {
  const model = buildModel([leg()]);
  assert.equal(model.verdict, 'PASS');
  const md = renderReport(model, { generatedAt: '2026-10-06T00:00:00Z' });
  assert.match(md, /^# OET fleet load test report/m);
  assert.match(md, /\*\*Verdict: PASS\*\*/);
  assert.ok(md.indexOf(SCOPE_STATEMENT) < md.indexOf('## Owner targets'));
  assert.match(md, /provider capacity|External provider capacity/i);
  assert.match(md, /owner-supplied assumption/);
});

test('a latency breach on the critical reads fails the run', () => {
  const model = buildModel([leg({ read: 620 })]);
  assert.equal(model.verdict, 'FAIL');
  const row = model.targetRows.find((r) => r.id === 'critical-read-p95-steady');
  assert.equal(row.status, 'FAIL');
  assert.match(renderReport(model), /\*\*FAIL\*\*/);
});

test('a lost acknowledged save or an idempotency violation fails the run', () => {
  assert.equal(buildModel([leg({ lost: 1 })]).verdict, 'FAIL');
  assert.equal(buildModel([leg({ idempotency: 2 })]).verdict, 'FAIL');
});

test('0.1 % unexpected failures is the boundary: exactly 0.1 % fails, below passes', () => {
  assert.equal(buildModel([leg({ fail: [10, 9990] })]).verdict, 'FAIL'); // exactly 0.1 %
  assert.equal(buildModel([leg({ fail: [9, 9991] })]).verdict, 'PASS'); // 0.09 %
});

test('a failed k6 threshold on any leg fails the run even when the rows look fine', () => {
  const failed = [{ metric: 'oet_unexpected_failure{phase:steady}', expression: 'rate<0.001' }];
  const model = buildModel([leg(), leg({ index: 1, thresholdsFailed: failed })]);
  assert.equal(model.verdict, 'FAIL');
  assert.equal(model.thresholdFailures[0].leg, 1);
  assert.match(renderReport(model), /oet_unexpected_failure/);
});

test('an unexercised target is INCOMPLETE, not a pass, unless no-data is allowed', () => {
  const noLive = leg({ drop: ['http_req_duration{class:live-setup,phase:steady}', 'oet_class_total{class:live-setup,phase:steady}'] });
  assert.equal(buildModel([noLive]).verdict, 'INCOMPLETE');
  assert.equal(buildModel([noLive], { allowNoData: true }).verdict, 'PASS');
  assert.match(renderReport(buildModel([noLive])), /INCOMPLETE/);
});

test('legs merge conservatively: worst latency, summed counters, pooled rates', () => {
  const legs = [leg({ read: 300, fail: [1, 4999] }), leg({ index: 1, read: 450, fail: [3, 4997], lost: 0 })];
  assert.equal(worstTrend(legs, 'http_req_duration', { class: 'critical-read', phase: 'steady' }, 'p(95)').value, 450);
  assert.equal(sumCounter(legs, 'oet_class_total', { class: 'critical-read', phase: 'steady' }), 10000);
  const pooled = pooledRate(legs, 'oet_unexpected_failure', { phase: 'steady' });
  assert.equal(pooled.samples, 10000);
  assert.equal(pooled.rate, 0.0004);
  assert.equal(pooledRate([], 'x', {}), null);
});

test('capacity profile reports the highest passing stage', () => {
  const stageMetrics = {};
  for (const [stage, read, fails] of [[100, 120, 0], [300, 240, 1], [600, 480, 2], [1000, 910, 80]]) {
    stageMetrics[`http_req_duration{class:critical-read,phase:steady,stage:${stage}}`] = trend(read);
    stageMetrics[`oet_unexpected_failure{phase:steady,stage:${stage}}`] = rate(fails, 100000 - fails);
  }
  const model = buildModel([leg({ profile: 'capacity', extra: stageMetrics })]);
  assert.deepEqual(model.stages.map((s) => [s.target, s.pass]), [[100, true], [300, true], [600, true], [1000, false]]);
  const md = renderReport(model);
  assert.match(md, /## Capacity stages/);
  assert.match(md, /Highest passing stage: 600 learners; first failing stage: 1000/);
});

test('overload profile reports sheds, collapse rate and recovery rows', () => {
  const extra = {
    'http_req_duration{class:critical-read,phase:recovery}': trend(350),
    'http_req_duration{class:exam-save,phase:recovery}': trend(300),
    'http_req_duration{class:submission,phase:recovery}': trend(320),
    'http_req_duration{class:live-setup,phase:recovery}': trend(700),
    'oet_class_total{class:critical-read,phase:recovery}': counter(900),
    'oet_class_total{class:exam-save,phase:recovery}': counter(300),
    'oet_class_total{class:submission,phase:recovery}': counter(50),
    'oet_class_total{class:live-setup,phase:recovery}': counter(80),
    'oet_unexpected_failure{phase:recovery}': rate(0, 5000),
    'oet_established_ok{phase:recovery}': rate(5000, 0),
    'oet_established_ok{phase:overload}': rate(9950, 50),
    'oet_collapse{phase:overload}': rate(5, 9995),
    'oet_graceful_shed{phase:overload}': counter(240),
  };
  const model = buildModel([leg({ profile: 'overload', extra })]);
  assert.equal(model.verdict, 'PASS');
  assert.ok(model.targetRows.some((r) => r.id === 'overload-established' && r.status === 'PASS'));
  assert.ok(model.targetRows.some((r) => r.id === 'critical-read-p95-recovery' && r.status === 'PASS'));
  const md = renderReport(model);
  assert.match(md, /## Overload behaviour/);
  assert.match(md, /Graceful sheds during the surge .*\*\*240\*\*/);
  // sessions that did not survive the surge fail the run
  const bad = buildModel([leg({ profile: 'overload', extra: { ...extra, 'oet_established_ok{phase:overload}': rate(900, 100) } })]);
  assert.equal(bad.verdict, 'FAIL');
});

test('the endpoint status matrix is rendered when the smoke probe published it', () => {
  const extra = {
    'oet_status_total{ep:dashboard,status:200}': counter(40),
    'oet_status_total{ep:dashboard,status:500}': counter(0),
    'oet_status_total{ep:reading-save-answer,status:400}': counter(3),
  };
  const md = renderReport(buildModel([leg({ extra })]));
  assert.match(md, /### Endpoint status matrix/);
  assert.match(md, /`dashboard`/);
  assert.match(md, /`reading-save-answer`/);
  assert.ok(!md.includes('| 500 |'), 'zero-count statuses are not columns');
});

test('validateSummary rejects other documents', () => {
  assert.throws(() => validateSummary({ schema: 'other' }, 'x.json'), /x\.json: not an oet-load-summary\/1/);
  assert.throws(() => validateSummary(null), /not an/);
  assert.throws(() => validateSummary({ schema: 'oet-load-summary/1' }), /missing metrics/);
});

test('buildTargetRows only lists recovery rows for the overload profile', () => {
  assert.ok(!buildTargetRows([leg()], 'steady').some((r) => r.id.endsWith('recovery')));
  assert.ok(buildTargetRows([leg()], 'overload').some((r) => r.id.endsWith('recovery')));
});

test('parseArgs and main wire files, the verdict and the exit code', () => {
  assert.throws(() => parseArgs([]), /--input/);
  assert.throws(() => parseArgs(['--nope']), /unknown argument/);
  const written = {};
  const files = { 'a.json': JSON.stringify(leg()), 'b.json': JSON.stringify(leg({ index: 1, read: 900 })) };
  const io = { read: (p) => files[p], write: (p, c) => { written[p] = c; } };
  const ok = main(['--input', 'a.json', '--out', 'r.md', '--json', 'v.json'], io);
  assert.equal(ok.model.verdict, 'PASS');
  assert.equal(ok.exitCode, 0);
  assert.match(written['r.md'], /Verdict: PASS/);
  assert.equal(JSON.parse(written['v.json']).verdict, 'PASS');
  const bad = main(['--input', 'a.json', '--input', 'b.json', '--out', 'r2.md', '--require-pass'], io);
  assert.equal(bad.model.verdict, 'FAIL');
  assert.equal(bad.exitCode, 1);
  // without --require-pass the report is still produced but the exit code stays 0
  assert.equal(main(['--input', 'b.json', '--out', 'r3.md'], io).exitCode, 0);
});

test('the report says hub polling stretches think time, and prints the effective cadence when the leg recorded it', () => {
  const plain = renderReport(buildModel([leg()]));
  assert.match(plain, /returns on the server's 15 s keep-alive/);
  assert.doesNotMatch(plain, /effective mean pause/);
  const rows = [{ activity: 'browse tick', nominalS: 16.5, effectiveS: 23.8, slowdown: 1.44 }];
  const withRows = renderReport(buildModel([leg({ meta: { hubCadence: rows } })]));
  assert.match(withRows, /\| activity \| nominal mean pause \(s\) \| effective mean pause \(s\) \| slowdown \|/);
  assert.match(withRows, /\| browse tick \| 16\.5 \| 23\.8 \| 1\.44 \|/);
});
