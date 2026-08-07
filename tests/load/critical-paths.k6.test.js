import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const source = readFileSync(new URL('./critical-paths.k6.js', import.meta.url), 'utf8');

test('critical k6 load test declares the staging performance thresholds', () => {
  assert.match(source, /http_req_failed/);
  assert.match(source, /http_req_duration\{endpoint-class:critical-read\}/);
  assert.doesNotMatch(source, /http_req_duration\{endpoint:critical-read\}/);
  assert.match(source, /p\(95\)<1000/);
  assert.match(source, /p\(99\)<2000/);
  assert.match(source, /K6_INCLUDE_ADMIN/);
});

test('critical k6 load test does not suppress failures or hard-code production targets', () => {
  assert.doesNotMatch(source, /\|\|\s*true/);
  assert.doesNotMatch(source, /continue-on-error/);
  assert.doesNotMatch(source, /app\.oetwithdrhesham\.co\.uk|api\.oetwithdrhesham\.co\.uk|185\.252\.233\.186/iu);
});
