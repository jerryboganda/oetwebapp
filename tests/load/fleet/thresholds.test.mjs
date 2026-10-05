import assert from 'node:assert/strict';
import test from 'node:test';
import {
  FLOW_NAMES, MATRIX_STATUSES, OWNER_TARGETS, REQUEST_CLASSES, buildThresholds, mergeThresholds, statusMatrixThresholds, sub,
} from './thresholds.mjs';

test("the owner's targets are encoded exactly", () => {
  assert.equal(OWNER_TARGETS.criticalReadP95Ms, 500);
  assert.equal(OWNER_TARGETS.criticalReadP99Ms, 1500);
  assert.equal(OWNER_TARGETS.examSaveP95Ms, 500);
  assert.equal(OWNER_TARGETS.submissionP95Ms, 500);
  assert.equal(OWNER_TARGETS.liveSetupP95Ms, 1000);
  assert.equal(OWNER_TARGETS.unexpectedFailureRate, 0.001);
});

test('sub() sorts tag keys so the name is stable', () => {
  assert.equal(sub('m', { phase: 'steady', class: 'x' }), 'm{class:x,phase:steady}');
});

test('steady gates the owner targets on the steady phase only', () => {
  const t = buildThresholds('steady');
  assert.deepEqual(t['http_req_duration{class:critical-read,phase:steady}'], ['p(95)<500', 'p(99)<1500']);
  assert.deepEqual(t['http_req_duration{class:exam-save,phase:steady}'], ['p(95)<500']);
  assert.deepEqual(t['http_req_duration{class:submission,phase:steady}'], ['p(95)<500']);
  assert.deepEqual(t['http_req_duration{class:live-setup,phase:steady}'], ['p(95)<1000']);
  assert.deepEqual(t['oet_unexpected_failure{phase:steady}'], ['rate<0.001']);
  assert.deepEqual(t['oet_established_ok{phase:steady}'], ['rate>0.999']);
  assert.deepEqual(t.oet_lost_ack_save, ['count==0']);
  assert.deepEqual(t.oet_idempotency_violation, ['count==0']);
  assert.deepEqual(t.oet_credit_consumed_while_queued, ['count==0']);
  assert.deepEqual(t.oet_timer_started_while_queued, ['count==0']);
  assert.ok(!Object.keys(t).some((k) => k.includes('phase:overload')));
});

test('no gating threshold is swallowed: nothing here uses abortOnFail except the circuit breaker', () => {
  const t = buildThresholds('overload');
  const aborting = Object.entries(t).filter(([, rules]) => rules.some((r) => typeof r === 'object' && r.abortOnFail));
  assert.deepEqual(aborting.map(([name]) => name), ['oet_unexpected_failure']);
  assert.equal(aborting[0][1][0].threshold, 'rate<0.10');
});

test('overload adds queue-not-collapse and recovery gates', () => {
  const t = buildThresholds('overload');
  assert.deepEqual(t['oet_established_ok{phase:overload}'], ['rate>0.99']);
  assert.deepEqual(t['oet_collapse{phase:overload}'], ['rate<0.01']);
  assert.deepEqual(t['http_req_duration{class:critical-read,phase:recovery}'], ['p(95)<500', 'p(99)<1500']);
  assert.deepEqual(t['oet_unexpected_failure{phase:recovery}'], ['rate<0.001']);
  assert.ok('oet_graceful_shed{phase:overload}' in t);
});

test('non-overload profiles never reference overload-only metrics (a tag with no samples would fail)', () => {
  for (const profile of ['smoke', 'steady', 'capacity']) {
    const t = buildThresholds(profile, { stages: [100, 300] });
    assert.ok(!Object.keys(t).some((k) => k.includes('phase:overload') || k.includes('phase:recovery')), profile);
  }
});

test('capacity publishes per-stage latency as always-true info thresholds', () => {
  const t = buildThresholds('capacity', { stages: [100, 300, 600, 1000] });
  for (const stage of [100, 300, 600, 1000]) {
    assert.deepEqual(t[`http_req_duration{class:critical-read,phase:steady,stage:${stage}}`], ['p(95)>=0']);
    assert.deepEqual(t[`oet_unexpected_failure{phase:steady,stage:${stage}}`], ['rate>=0']);
  }
});

test('required flows are only required of a leg that runs them', () => {
  const t = buildThresholds('steady', {
    requiredFlows: ['browse', 'speaking', 'room'],
    expect: { browse: 200, speaking: 25, room: 0 },
  });
  assert.deepEqual(t['oet_flow_completed{flow:browse}'], ['count>0']);
  assert.deepEqual(t['oet_flow_completed{flow:speaking}'], ['count>0']);
  assert.deepEqual(t['oet_flow_completed{flow:room}'], ['count>=0']);
});

test('every flow publishes started / completed / skipped counts', () => {
  const t = buildThresholds('steady');
  for (const flow of FLOW_NAMES) {
    for (const metric of ['oet_flow_started', 'oet_flow_completed', 'oet_flow_skipped']) {
      assert.ok(`${metric}{flow:${flow}}` in t, `${metric} ${flow}`);
    }
  }
});

test('every threshold expression is a valid k6 aggregation', () => {
  const pattern = /^(?:p\(\d+(?:\.\d+)?\)|avg|min|max|med|rate|count|value)(?:<|<=|>|>=|==|!=)\d+(?:\.\d+)?$/;
  for (const profile of ['smoke', 'capacity', 'steady', 'overload']) {
    for (const [name, rules] of Object.entries(buildThresholds(profile, { stages: [10], requiredFlows: ['browse'], expect: { browse: 1 } }))) {
      for (const rule of rules) {
        const text = typeof rule === 'string' ? rule : rule.threshold;
        assert.match(text, pattern, `${profile}: ${name}`);
      }
    }
  }
});

test('the status matrix is an always-true contract probe per class and per endpoint', () => {
  const classOnly = statusMatrixThresholds();
  assert.equal(Object.keys(classOnly).length, REQUEST_CLASSES.length * MATRIX_STATUSES.length);
  assert.deepEqual(classOnly['oet_status_total{class:critical-read,status:200}'], ['count>=0']);
  const withEndpoints = statusMatrixThresholds({ endpoints: ['reading-save-answer', 'dashboard'] });
  assert.equal(Object.keys(withEndpoints).length, (REQUEST_CLASSES.length + 2) * MATRIX_STATUSES.length);
  assert.deepEqual(withEndpoints['oet_status_total{ep:dashboard,status:503}'], ['count>=0']);
  // 'ep' ids must be plain tokens: a ':' or ',' in a tag value would break k6's sub-metric parser
  for (const key of Object.keys(withEndpoints)) assert.match(key, /^oet_status_total\{[a-z]+:[a-z0-9-]+,status:\d+\}$/);
});

test('mergeThresholds concatenates rules for the same metric', () => {
  assert.deepEqual(mergeThresholds({ a: ['x'] }, { a: ['y'], b: ['z'] }), { a: ['x', 'y'], b: ['z'] });
});
