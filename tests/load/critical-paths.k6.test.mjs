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

const read = (name) => readFileSync(new URL(name, import.meta.url), 'utf8');

test('the auth helper no longer claims its cache is shared between VUs, and offers one account per VU', () => {
  const helper = read('./lib/auth-helper.js');
  assert.doesNotMatch(helper, /survives across VUs/i);
  assert.match(helper, /OET_LOAD_DISTINCT_VUS/);
  assert.match(helper, /accountFor\('learner'/);
  assert.match(helper, /ONE VU/);
});

test('the smoke speaking scripts scale with K6_VUS and never "pass" without making requests', () => {
  const create = read('./speaking-session-create.k6.js');
  const livekit = read('./speaking-livekit-token.k6.js');
  assert.match(create, /peakVus\(100\)/);
  assert.match(livekit, /peakVus\(100\)/);
  // the LiveKit script must fail at start when it has no room, not sleep through the run
  assert.match(livekit, /export function setup\(\)/);
  assert.match(livekit, /fail\('SPEAKING_LIVE_ROOM_ID is required/);
  assert.doesNotMatch(livekit, /if \(!TEST_ROOM_ID\) \{ sleep/);
  assert.match(create, /mode: 'ai_self_practice'/);
});

test('the fleet scenario keeps its gating thresholds and its production guard', () => {
  const fleet = read('./fleet-1000.k6.js');
  assert.match(fleet, /buildThresholds\(/);
  assert.match(fleet, /export function handleSummary/);
  assert.doesNotMatch(fleet, /\|\|\s*true/);
  assert.doesNotMatch(read('./fleet/config.js'), /oetwithdrhesham/);
  assert.match(read('./fleet/config.js'), /assertNonProduction\('K6_API_URL'/);
});
