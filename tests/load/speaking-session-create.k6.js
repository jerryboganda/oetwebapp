// 100 concurrent learners creating Speaking sessions.
// SLO: p95 < 800ms, p99 < 2000ms, error rate < 2%.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE, authHeaders } from './lib/auth-helper.js';

export const options = {
  stages: [
    { duration: '30s', target: 25 },
    { duration: '1m',  target: 100 },
    { duration: '3m',  target: 100 },
    { duration: '30s', target: 0 },
  ],
  thresholds: {
    'http_req_duration{endpoint:create-session}': ['p(95)<800', 'p(99)<2000'],
    'http_req_failed': ['rate<0.02'],
  },
};

export default function () {
  // Pick first available card for the learner's profession.
  const cardsRes = http.get(`${BASE}/v1/speaking/role-play-cards`, {
    headers: authHeaders(),
    tags: { endpoint: 'list-cards' },
  });
  if (cardsRes.status !== 200) { sleep(1); return; }
  const cards = JSON.parse(cardsRes.body);
  if (!Array.isArray(cards) || cards.length === 0) { sleep(1); return; }

  const body = JSON.stringify({ rolePlayCardId: cards[0].id, mode: 'AiSelfPractice' });
  const res = http.post(`${BASE}/v1/speaking/sessions`, body, {
    headers: authHeaders(),
    tags: { endpoint: 'create-session' },
  });
  check(res, { 'session 201/200': (r) => r.status === 200 || r.status === 201 });
  sleep(0.5);
}
