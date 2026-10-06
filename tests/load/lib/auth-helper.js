// Learner sign-in for the smoke-scale k6 scripts (critical-paths, speaking-*). The 1,000-learner
// harness does NOT use this file: it has per-learner sessions, token refresh and pacing of its own
// (tests/load/fleet/).
//
// k6 gives every VU its own JavaScript runtime, so the module-level cache below lives for ONE VU: it
// avoids re-signing-in on every iteration of that VU, nothing more. Two modes:
//
//   shared account (default)  every VU signs in as OET_TEST_LEARNER_EMAIL. The platform allows ONE
//                             active session per account (SingleActiveSessionEnabled, on by default),
//                             so each new sign-in revokes the previous VU's session. Use a single VU
//                             (K6_VUS=1), or share one token (critical-paths.k6.js signs in once in setup()).
//   distinct accounts         OET_LOAD_DISTINCT_VUS=1: VU n signs in as the deterministic disposable
//                             account learner-(n-1) that tests/load/seed/seed-accounts.mjs creates
//                             (password OET_LOAD_PASSWORD), each with its own device id. This is the
//                             mode for more than one VU.
import http from 'k6/http';
import { check } from 'k6';
import { accountFor } from '../fleet/accounts.mjs';

const BASE = __ENV.K6_TARGET_URL || 'http://localhost:5199';
const DISTINCT = __ENV.OET_LOAD_DISTINCT_VUS === '1';

function identity() {
  if (DISTINCT) {
    const account = accountFor('learner', Math.max(0, __VU - 1), {
      prefix: __ENV.OET_LOAD_ACCOUNT_PREFIX || 'loadtest',
      domain: __ENV.OET_LOAD_EMAIL_DOMAIN || 'load.oet.test',
    });
    return { email: account.email, password: __ENV.OET_LOAD_PASSWORD || '', deviceId: account.deviceId };
  }
  return {
    email: __ENV.OET_TEST_LEARNER_EMAIL || 'e2e-learner@example.com',
    password: __ENV.OET_TEST_LEARNER_PASSWORD || 'please-change-me',
    deviceId: __ENV.OET_TEST_DEVICE_ID || '',
  };
}

let cachedToken = null;

export function getToken() {
  if (cachedToken) return cachedToken;
  const who = identity();
  const headers = { 'Content-Type': 'application/json' };
  if (who.deviceId) headers['X-OET-Device-Id'] = who.deviceId;
  const res = http.post(`${BASE}/v1/auth/sign-in`,
    JSON.stringify({ email: who.email, password: who.password, rememberMe: true }),
    { headers, tags: { endpoint: 'sign-in' } });
  check(res, { 'sign-in 200': (r) => r.status === 200 });
  if (res.status === 200) {
    cachedToken = JSON.parse(res.body).accessToken;
  }
  return cachedToken;
}

export function authHeaders(accessToken = null) {
  const headers = {
    Authorization: `Bearer ${accessToken || getToken()}`,
    'Content-Type': 'application/json',
  };
  const deviceId = DISTINCT ? identity().deviceId : (__ENV.OET_TEST_DEVICE_ID || '');
  if (deviceId) headers['X-OET-Device-Id'] = deviceId;
  return headers;
}

/** Peak VUs for a smoke script: K6_VUS, default 100. Shared-account runs must set K6_VUS=1. */
export function peakVus(fallback = 100) {
  const value = Number(__ENV.K6_VUS);
  return Number.isFinite(value) && value >= 1 ? Math.floor(value) : fallback;
}

export { BASE };
