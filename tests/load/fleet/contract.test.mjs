import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { classifyResponse } from './classify.mjs';

// contract.js is a k6 module (ES syntax in a .js file, no imports). It is loaded from a data URL so the
// test does not depend on how this node version decides the module type of a .js file.
const contractSource = readFileSync(new URL('./contract.js', import.meta.url));
const {
  ACTIONS, CSRF_EXEMPT_HUBS, HUBS, isCsrfExemptHub,
} = await import(`data:text/javascript;base64,${contractSource.toString('base64')}`);

const read = (name) => readFileSync(new URL(name, import.meta.url), 'utf8');

// What a freshly seeded learner gets back. tests/load/seed/seed-lib.mjs creates the account and its credit
// pools and nothing else (no Subscription row), so GET /v1/subscriptions/me answers 404 (NotFound in
// BillingSubscriptionEndpoints). Every other critical read answers 200.
const SEEDED_LEARNER_STATUS = { subscription: 404 };

test('endpoint ids are unique (the status matrix is keyed by them)', () => {
  const ids = Object.values(ACTIONS).map((spec) => spec.id);
  assert.equal(new Set(ids).size, ids.length);
});

test('every critical-read action is satisfiable for a freshly seeded learner', () => {
  const reads = Object.values(ACTIONS).filter((spec) => spec.class === 'critical-read');
  assert.ok(reads.length >= 8, 'the critical reads were not found');
  for (const spec of reads) {
    const status = SEEDED_LEARNER_STATUS[spec.id] ?? 200;
    const result = classifyResponse({ status, errorCode: null }, spec, { phase: 'steady' });
    assert.equal(
      result.unexpected,
      false,
      `${spec.id} (${spec.method} ${spec.path}) would count as an unexpected failure for a seeded learner (HTTP ${status})`,
    );
  }
});

test('a learner with no subscription gets a 404 that is the documented answer, and only that', () => {
  assert.equal(classifyResponse({ status: 404 }, ACTIONS.subscription).kind, 'domain');
  assert.equal(classifyResponse({ status: 403 }, ACTIONS.subscription).unexpected, true);
  assert.equal(classifyResponse({ status: 500 }, ACTIONS.subscription).unexpected, true);
});

test('the CSRF exemption list mirrors the web proxy (lib/backend-proxy.ts)', () => {
  const proxy = readFileSync(new URL('../../../lib/backend-proxy.ts', import.meta.url), 'utf8');
  const literal = /const SIGNALR_HUB_PATH_PATTERN = \/(.+)\/([a-z]*);/.exec(proxy);
  assert.ok(literal, 'SIGNALR_HUB_PATH_PATTERN was not found in lib/backend-proxy.ts');
  const pattern = new RegExp(literal[1], literal[2]);
  for (const hubPath of Object.values(HUBS)) {
    for (const suffix of ['', '/negotiate']) {
      assert.equal(
        pattern.test(`/api/backend${hubPath}${suffix}`),
        isCsrfExemptHub(hubPath),
        `${hubPath}${suffix}: the proxy and contract.js disagree on whether this hub is CSRF-exempt`,
      );
    }
  }
  assert.deepEqual([...CSRF_EXEMPT_HUBS], [HUBS.notifications]);
});

test('http.js decides the CSRF header per hub, and every mutating hub request names its hub', () => {
  const http = read('./http.js');
  assert.match(http, /isCsrfExemptHub\(options\.hubPath\)/);
  assert.doesNotMatch(http, /!spec\.auth && !spec\.hub/);
  const hubCalls = read('./signalr.js').split('\n')
    .filter((line) => /ACTIONS\.hub(?:Negotiate|Send|Close)\b/.test(line));
  assert.ok(hubCalls.length >= 5, 'the hub calls were not found');
  for (const line of hubCalls) assert.match(line, /hubPath/, line.trim());
});
