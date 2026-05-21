// LiveKit JWT mint throughput.
// SLO: p95 < 200ms, error rate < 1%.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE, authHeaders } from './lib/auth-helper.js';

export const options = {
  stages: [
    { duration: '30s', target: 20 },
    { duration: '2m',  target: 100 },
    { duration: '30s', target: 0 },
  ],
  thresholds: {
    'http_req_duration{endpoint:livekit-token}': ['p(95)<200'],
    'http_req_failed': ['rate<0.01'],
  },
};

const TEST_ROOM_ID = __ENV.SPEAKING_LIVE_ROOM_ID || '';

export default function () {
  if (!TEST_ROOM_ID) { sleep(1); return; }
  const res = http.get(`${BASE}/v1/speaking/live-rooms/${TEST_ROOM_ID}/token`, {
    headers: authHeaders(),
    tags: { endpoint: 'livekit-token' },
  });
  check(res, { 'token 200': (r) => r.status === 200 });
  sleep(0.2);
}
