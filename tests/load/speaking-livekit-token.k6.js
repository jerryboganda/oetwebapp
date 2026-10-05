// LiveKit JWT mint throughput at smoke scale (default peak 100 VUs, K6_VUS to change it).
// SLO: p95 < 200ms, error rate < 1%.
//
// NEEDS a provisioned live room: SPEAKING_LIVE_ROOM_ID must be a live room the signed-in account may
// join. Without one this script FAILS at start. It used to sleep and "pass" with no request made,
// which proved nothing. Identity rules are the same as speaking-session-create.k6.js.

import http from 'k6/http';
import { check, fail, sleep } from 'k6';
import { BASE, authHeaders, peakVus } from './lib/auth-helper.js';

const PEAK = peakVus(100);
const TEST_ROOM_ID = __ENV.SPEAKING_LIVE_ROOM_ID || '';

export const options = {
  stages: [
    { duration: '30s', target: Math.max(1, Math.round(PEAK * 0.2)) },
    { duration: '2m',  target: PEAK },
    { duration: '30s', target: 0 },
  ],
  thresholds: {
    'http_req_duration{endpoint:livekit-token}': ['p(95)<200'],
    'http_req_failed': ['rate<0.01'],
  },
};

export function setup() {
  if (!TEST_ROOM_ID) {
    fail('SPEAKING_LIVE_ROOM_ID is required: this script mints tokens for one existing live room and tests nothing without it');
  }
}

export default function () {
  const res = http.get(`${BASE}/v1/speaking/live-rooms/${TEST_ROOM_ID}/token`, {
    headers: authHeaders(),
    tags: { endpoint: 'livekit-token' },
  });
  check(res, { 'token 200': (r) => r.status === 200 });
  sleep(0.2);
}
