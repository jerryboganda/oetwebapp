// Staging-only critical read path load test.
// SLO: P95 < 1s, P99 < 2s, HTTP error rate < 1%.

import http from 'k6/http';
import { check, fail, sleep } from 'k6';
import { BASE, authHeaders, getToken } from './lib/auth-helper.js';

export const options = {
  stages: [
    { duration: '30s', target: 10 },
    { duration: '60s', target: 50 },
    { duration: '120s', target: 100 },
    { duration: '30s', target: 0 },
  ],
  thresholds: {
    'http_req_failed': ['rate<0.01'],
    'http_req_duration{endpoint:critical-read}': ['p(95)<1000', 'p(99)<2000'],
  },
};

const includeAdmin = __ENV.K6_INCLUDE_ADMIN === '1';
const adminAccessToken = __ENV.OET_TEST_ADMIN_ACCESS_TOKEN || '';

function checkRead(res, endpoint) {
  check(res, {
    [`${endpoint} 200`]: (response) => response.status === 200,
  });
}

function adminHeaders() {
  if (!adminAccessToken) {
    fail('K6_INCLUDE_ADMIN=1 requires OET_TEST_ADMIN_ACCESS_TOKEN.');
  }

  return {
    Authorization: `Bearer ${adminAccessToken}`,
    'Content-Type': 'application/json',
  };
}

export default function () {
  if (!getToken()) {
    fail('Learner sign-in did not return an access token.');
  }

  const headers = authHeaders();
  const reads = [
    ['bootstrap', '/v1/me/bootstrap'],
    ['dashboard', '/v1/learner/dashboard'],
    ['subscription', '/v1/subscriptions/me'],
    ['entitlement-snapshot', '/v1/me/entitlement-snapshot'],
  ];

  for (const [endpoint, path] of reads) {
    const response = http.get(`${BASE}${path}`, {
      headers,
      tags: { endpoint, 'endpoint-class': 'critical-read' },
    });
    checkRead(response, endpoint);
  }

  if (includeAdmin) {
    const response = http.get(`${BASE}/v1/admin/dashboard`, {
      headers: adminHeaders(),
      tags: { endpoint: 'admin-dashboard', 'endpoint-class': 'critical-read' },
    });
    checkRead(response, 'admin-dashboard');
  }

  sleep(0.2);
}
