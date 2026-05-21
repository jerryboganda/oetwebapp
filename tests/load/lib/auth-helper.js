// Cached learner sign-in. Module-scope cache survives across VUs.
import http from 'k6/http';
import { check } from 'k6';

const BASE = __ENV.K6_TARGET_URL || 'http://localhost:5199';
const EMAIL = __ENV.OET_TEST_LEARNER_EMAIL || 'e2e-learner@example.com';
const PASSWORD = __ENV.OET_TEST_LEARNER_PASSWORD || 'please-change-me';

let cachedToken = null;

export function getToken() {
  if (cachedToken) return cachedToken;
  const res = http.post(`${BASE}/v1/auth/sign-in`,
    JSON.stringify({ email: EMAIL, password: PASSWORD, rememberMe: true }),
    { headers: { 'Content-Type': 'application/json' }, tags: { endpoint: 'sign-in' } });
  check(res, { 'sign-in 200': (r) => r.status === 200 });
  if (res.status === 200) {
    cachedToken = JSON.parse(res.body).accessToken;
  }
  return cachedToken;
}

export function authHeaders() {
  return { Authorization: `Bearer ${getToken()}`, 'Content-Type': 'application/json' };
}

export { BASE };
