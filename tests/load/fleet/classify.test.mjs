import assert from 'node:assert/strict';
import test from 'node:test';
import { classifyResponse, errorCodeOf, headerOf, retryAfterSeconds } from './classify.mjs';

const read = { ok: [200] };

test('an expected status is ok', () => {
  assert.deepEqual(classifyResponse({ status: 200 }, read), {
    status: 200, kind: 'ok', unexpected: false, collapse: false, shed: false,
  });
  assert.equal(classifyResponse({ status: 204 }, { ok: [200, 204] }).kind, 'ok');
});

test('a documented business refusal is a domain result, not a failure', () => {
  const spec = { ok: [204], domain: [400], domainCodes: ['part_bc_not_open', 'attempt_deadline_passed'] };
  assert.equal(classifyResponse({ status: 400, errorCode: 'part_bc_not_open' }, spec).kind, 'domain');
  assert.equal(classifyResponse({ status: 400, errorCode: 'something_new' }, spec).unexpected, true);
  assert.equal(classifyResponse({ status: 400, errorCode: null }, spec).unexpected, true);
  // no code list means any code of a listed status is acceptable
  assert.equal(classifyResponse({ status: 409 }, { ok: [200], domain: [409] }).kind, 'domain');
});

test('401 is never counted on its own: the caller refreshes and retries', () => {
  const r = classifyResponse({ status: 401 }, read);
  assert.equal(r.kind, 'unauthorized');
  assert.equal(r.unexpected, false);
});

test('429 / 503 with Retry-After is graceful shedding only where shedding is allowed', () => {
  const steady = classifyResponse({ status: 429, retryAfter: '10' }, read, { phase: 'steady' });
  assert.equal(steady.unexpected, true);
  assert.equal(steady.shed, false);
  const overload = classifyResponse({ status: 429, retryAfter: '10' }, read, { phase: 'overload' });
  assert.deepEqual([overload.kind, overload.unexpected, overload.shed, overload.collapse], ['shed', false, true, false]);
  const queued = classifyResponse({ status: 503, retryAfter: '3' }, { ok: [200], shedOk: true }, { phase: 'steady' });
  assert.equal(queued.kind, 'shed');
  // shedding without Retry-After is a failure even during overload
  assert.equal(classifyResponse({ status: 503 }, read, { phase: 'overload' }).collapse, true);
  assert.equal(classifyResponse({ status: 429 }, read, { phase: 'overload' }).unexpected, true);
});

test('5xx and no-response are collapse signals', () => {
  for (const status of [500, 502, 504]) {
    const r = classifyResponse({ status }, read, { phase: 'overload' });
    assert.deepEqual([r.kind, r.unexpected, r.collapse], ['collapse', true, true]);
  }
  const net = classifyResponse({ status: 0 }, read, { phase: 'steady' });
  assert.deepEqual([net.kind, net.unexpected, net.collapse], ['collapse', true, true]);
  // an unexpected 4xx is a failure but not a collapse
  const notFound = classifyResponse({ status: 404 }, read);
  assert.deepEqual([notFound.kind, notFound.unexpected, notFound.collapse], ['unexpected', true, false]);
});

test('errorCodeOf reads the code shapes the API uses', () => {
  assert.equal(errorCodeOf({ code: 'rate_limited' }), 'rate_limited');
  assert.equal(errorCodeOf({ errorCode: 'writing_scenario_not_found' }), 'writing_scenario_not_found');
  assert.equal(errorCodeOf({ error: { code: 'x' } }), 'x');
  assert.equal(errorCodeOf({ error: 'plain' }), 'plain');
  assert.equal(errorCodeOf({}), null);
  assert.equal(errorCodeOf(null), null);
  assert.equal(errorCodeOf('text'), null);
});

test('retryAfterSeconds clamps and falls back', () => {
  assert.equal(retryAfterSeconds('7'), 7);
  assert.equal(retryAfterSeconds('0'), 1);
  assert.equal(retryAfterSeconds('9999'), 120);
  assert.equal(retryAfterSeconds('Wed, 21 Oct 2026 07:28:00 GMT'), 5);
  assert.equal(retryAfterSeconds(null), 5);
  assert.equal(retryAfterSeconds(undefined, 9), 9);
});

test('headerOf is case-insensitive', () => {
  assert.equal(headerOf({ 'Retry-After': '4' }, 'retry-after'), '4');
  assert.equal(headerOf({ 'retry-after': '4' }, 'Retry-After'), '4');
  assert.equal(headerOf({}, 'x'), null);
  assert.equal(headerOf(null, 'x'), null);
});
