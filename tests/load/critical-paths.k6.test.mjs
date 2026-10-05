import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const read = (name) => readFileSync(new URL(name, import.meta.url), 'utf8');

/**
 * The code without its comments. A guard against `|| true`, a production host or a bad pattern must
 * judge what runs, not the prose that quotes the forbidden text (a comment saying "nothing is swallowed
 * with || true" would otherwise fail it). A `//` after a colon or a quote (a URL) is kept.
 */
const stripComments = (text) => text
  .replace(/\/\*[\s\S]*?\*\//g, '')
  .replace(/(^|[^:'"`\\])\/\/.*$/gm, '$1');

const source = read('./critical-paths.k6.js');

test('stripComments drops prose that quotes a forbidden pattern but keeps code and URLs', () => {
  assert.equal(stripComments('a(); // || true\nb(); /* || true */ c();'), 'a(); \nb();  c();');
  assert.equal(stripComments('// || true'), '');
  assert.match(stripComments("const u = 'https://api.oetwithdrhesham.co.uk'; // fine"), /oetwithdrhesham/);
  assert.match(stripComments('run(a || true);'), /\|\|\s*true/);
});

test('critical k6 load test declares the staging performance thresholds', () => {
  assert.match(source, /http_req_failed/);
  assert.match(source, /http_req_duration\{endpoint-class:critical-read\}/);
  assert.doesNotMatch(stripComments(source), /http_req_duration\{endpoint:critical-read\}/);
  assert.match(source, /p\(95\)<1000/);
  assert.match(source, /p\(99\)<2000/);
  assert.match(source, /K6_INCLUDE_ADMIN/);
});

test('critical k6 load test does not suppress failures or hard-code production targets', () => {
  const code = stripComments(source);
  assert.doesNotMatch(code, /\|\|\s*true/);
  assert.doesNotMatch(code, /continue-on-error/);
  assert.doesNotMatch(code, /app\.oetwithdrhesham\.co\.uk|api\.oetwithdrhesham\.co\.uk|185\.252\.233\.186/iu);
});

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
  assert.doesNotMatch(stripComments(livekit), /if \(!TEST_ROOM_ID\) \{ sleep/);
  assert.match(create, /mode: 'ai_self_practice'/);
});

test('the fleet scenario keeps its gating thresholds and its production guard', () => {
  const fleet = read('./fleet-1000.k6.js');
  assert.match(fleet, /buildThresholds\(/);
  assert.match(fleet, /export function handleSummary/);
  // judged on the code, not on the comment that explains the gate (it names `|| true` on purpose)
  assert.doesNotMatch(stripComments(fleet), /\|\|\s*true/);
  assert.doesNotMatch(stripComments(read('./fleet/config.js')), /oetwithdrhesham/);
  assert.match(read('./fleet/config.js'), /assertNonProduction\('K6_API_URL'/);
});

test('every leg discovers content as its own learner and fails fast when discovery cannot reach the API', () => {
  const fleet = stripComments(read('./fleet-1000.k6.js'));
  // the shared probe login was revoked by the other legs' sign-ins (single active session per account)
  assert.doesNotMatch(fleet, /newSession\('probe'/);
  assert.match(fleet, /newSession\('learner', PARAMS\.legIndex, PARAMS\.legIndex\)/);
  assert.match(fleet, /content discovery failed/);
});
