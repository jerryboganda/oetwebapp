import assert from 'node:assert/strict';
import test from 'node:test';
import {
  SUMMARY_SCHEMA, buildSummary, findMetric, listSubmetrics, normalizeThresholds, parseMetricKey, valueOf,
} from './summary-model.mjs';

const data = {
  state: { testRunDurationMs: 4_200_000 },
  metrics: {
    http_req_duration: { type: 'trend', contains: 'time', values: { avg: 120, 'p(95)': 410 } },
    'http_req_duration{class:critical-read,phase:steady}': {
      type: 'trend',
      contains: 'time',
      values: { 'p(95)': 430, 'p(99)': 900 },
      thresholds: { 'p(95)<500': { ok: true }, 'p(99)<1500': { ok: true } },
    },
    'oet_unexpected_failure{phase:steady}': {
      type: 'rate',
      contains: 'default',
      values: { rate: 0.004, passes: 40, fails: 9960 },
      thresholds: { 'rate<0.001': { ok: false } },
    },
    oet_lost_ack_save: { type: 'counter', contains: 'default', values: { count: 0, rate: 0 }, thresholds: { 'count==0': { ok: true } } },
  },
};

test('buildSummary keeps values, normalises thresholds and lists the failed ones', () => {
  const s = buildSummary(data, { profile: 'steady', leg: 0 });
  assert.equal(s.schema, SUMMARY_SCHEMA);
  assert.deepEqual(s.meta, { profile: 'steady', leg: 0 });
  assert.equal(s.durationMs, 4200000);
  assert.equal(s.passed, false);
  assert.deepEqual(s.thresholdsFailed, [{ metric: 'oet_unexpected_failure{phase:steady}', expression: 'rate<0.001' }]);
  assert.equal(s.metrics['oet_lost_ack_save'].thresholds['count==0'], true);
  assert.equal(s.metrics['http_req_duration'].thresholds && Object.keys(s.metrics['http_req_duration'].thresholds).length, 0);
});

test('a run with no failed thresholds passes', () => {
  const clean = { metrics: { a: { type: 'counter', values: { count: 1 }, thresholds: { 'count>0': { ok: true } } } } };
  assert.equal(buildSummary(clean).passed, true);
  assert.equal(buildSummary({}).passed, true);
  assert.equal(buildSummary({}).durationMs, null);
});

test('normalizeThresholds accepts both k6 shapes and ignores junk', () => {
  assert.deepEqual(normalizeThresholds({ a: { ok: false }, b: true, c: 'x' }), { a: false, b: true });
  assert.deepEqual(normalizeThresholds(undefined), {});
});

test('parseMetricKey splits the name from its tag set', () => {
  assert.deepEqual(parseMetricKey('http_req_duration{class:critical-read,phase:steady}'), {
    name: 'http_req_duration', tags: { class: 'critical-read', phase: 'steady' },
  });
  assert.deepEqual(parseMetricKey('oet_lost_ack_save'), { name: 'oet_lost_ack_save', tags: {} });
  assert.deepEqual(parseMetricKey('m{name:GET /v1/x/:id}'), { name: 'm', tags: { name: 'GET /v1/x/:id' } });
});

test('findMetric matches the exact tag set regardless of key order', () => {
  const s = buildSummary(data);
  assert.equal(findMetric(s.metrics, 'http_req_duration', { phase: 'steady', class: 'critical-read' }).values['p(95)'], 430);
  assert.equal(findMetric(s.metrics, 'http_req_duration', {}).values.avg, 120);
  assert.equal(findMetric(s.metrics, 'http_req_duration', { class: 'critical-read' }), null);
  assert.equal(findMetric(s.metrics, 'nope', {}), null);
});

test('listSubmetrics and valueOf', () => {
  const s = buildSummary(data);
  assert.equal(listSubmetrics(s.metrics, 'http_req_duration').length, 1);
  assert.equal(valueOf(s.metrics.oet_lost_ack_save, 'count'), 0);
  assert.equal(valueOf(null, 'count'), null);
  assert.equal(valueOf(s.metrics.oet_lost_ack_save, 'missing', 7), 7);
});
