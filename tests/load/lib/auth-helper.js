// Cached learner sign-in. Module-scope cache survives across VUs.
import http from 'k6/http';
import { check } from 'k6';

const BASE = __ENV.K6_TARGET_URL || 'http://localhost:5199';
const EMAIL = __ENV.OET_TEST_LEARNER_EMAIL || 'e2e-learner@example.com';
const PASSWORD = __ENV.OET_TEST_LEARNER_PASSWORD || 'please-change-me';
const DEVICE_ID = __ENV.OET_TEST_DEVICE_ID || '';

let cachedToken = null;

export function getToken() {
  if (cachedToken) return cachedToken;
  const headers = { 'Content-Type': 'application/json' };
  if (DEVICE_ID) headers['X-OET-Device-Id'] = DEVICE_ID;
  const res = http.post(`${BASE}/v1/auth/sign-in`,
    JSON.stringify({ email: EMAIL, password: PASSWORD, rememberMe: true }),
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
  if (DEVICE_ID) headers['X-OET-Device-Id'] = DEVICE_ID;
  return headers;
}

export { BASE };
