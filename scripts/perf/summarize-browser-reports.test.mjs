import assert from 'node:assert/strict';
import test from 'node:test';
import { buildBrowserPerformanceSummary } from './summarize-browser-reports.mjs';

const fixture = {
  project: 'perf-learner-pixel',
  route: '/',
  webVitals: { lcpMs: 2400, fcpMs: 1500, inpMs: null, cls: 0.02 },
  resources: {
    totals: {
      javascript: { encodedBytes: 471640, decodedBytes: 1710955 },
      css: { encodedBytes: 58232, decodedBytes: 414622 },
    },
  },
  errors: { pageErrors: 0, requestFailures: 0 },
  violations: [],
};

test('summary keeps encoded and decoded sizes visible as separate columns', () => {
  const summary = buildBrowserPerformanceSummary([fixture]);

  assert.match(summary, /471,640 B/);
  assert.match(summary, /1,710,955 B/);
  assert.match(summary, /58,232 B/);
  assert.match(summary, /414,622 B/);
  assert.match(summary, /n\/a/);
});

test('summary escapes cells and exposes violations without raw request data', () => {
  const summary = buildBrowserPerformanceSummary([{
    ...fixture,
    route: '/dashboard?token=should-not-be-logged',
    violations: ['LCP 3000ms exceeds 2500ms'],
  }]);

  assert.match(summary, /\| \/dashboard \|/);
  assert.doesNotMatch(summary, /should-not-be-logged/);
  assert.match(summary, /LCP 3000ms exceeds 2500ms/);
});
