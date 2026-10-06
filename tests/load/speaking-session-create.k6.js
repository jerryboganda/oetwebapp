// MANUAL TOOL, INERT: run manually by the owner from a self-provisioned load generator; no CI runs
// this; agents never run it (AGENTS.md "NO AUTOMATED QA ANYWHERE"). Runbook: docs/ops/LOAD-TESTING.md.
//
// Speaking session creation at smoke scale (default peak 100 VUs, K6_VUS to change it).
// SLO: p95 < 800ms, p99 < 2000ms, error rate < 2%.
//
// IDENTITY: with a shared account (the default) only ONE VU can hold a session, because every
// sign-in revokes the account's other sessions (single-active-session policy). Run it with K6_VUS=1,
// or with OET_LOAD_DISTINCT_VUS=1 against a stack seeded by tests/load/seed/seed-accounts.mjs so each
// VU is its own learner. The 1,000-learner scenario is tests/load/fleet-1000.k6.js.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE, authHeaders, peakVus } from './lib/auth-helper.js';

const PEAK = peakVus(100);
const stage = (fraction) => Math.max(1, Math.round(PEAK * fraction));

export const options = {
  stages: [
    { duration: '30s', target: stage(0.25) },
    { duration: '1m',  target: PEAK },
    { duration: '3m',  target: PEAK },
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

  const body = JSON.stringify({ rolePlayCardId: cards[0].id, mode: 'ai_self_practice' });
  const res = http.post(`${BASE}/v1/speaking/sessions`, body, {
    headers: authHeaders(),
    tags: { endpoint: 'create-session' },
  });
  check(res, { 'session 201/200': (r) => r.status === 200 || r.status === 201 });
  sleep(0.5);
}
