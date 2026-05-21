# Speaking Module — SLOs + Error Budgets

Service Level Objectives (SLOs) define the user-visible quality bar. Errors above budget trigger Sev3 → Sev2 escalations and a release freeze on the affected surface.

## Latency

| Surface | Metric | SLO |
|---------|--------|-----|
| Session create | `POST /v1/speaking/sessions` | p95 < 800 ms, p99 < 2000 ms |
| Warm-up start | `POST /sessions/{id}/start-warmup` | p95 < 600 ms |
| Role-play turn (AI patient) | ASR + LLM + TTS round-trip | p95 < 2500 ms |
| AI assessment delivery | session end → `AssessmentReady` event | p95 < 12 s, p99 < 25 s |
| Drill score | `POST /drills/.../score` | p95 < 6 s |
| LiveKit room connect | learner sees tutor video | p95 < 3 s |
| Tutor assessment submit | `POST /tutor-assessments/.../submit` | p95 < 1000 ms |

## Availability

- Speaking module monthly uptime: **99.5%** (≈ 3.6 hours allowed downtime/month).
- LiveKit live-tutor flow: **99.0%** (LiveKit Cloud SLA caps us).
- Admin surfaces: **99.0%**.

## Quality

| Metric | Budget |
|--------|--------|
| AI assessment mean absolute error vs gold | ≤ 0.3 per criterion |
| Anthropic prompt-cache hit rate | ≥ 80% on multi-turn sessions |
| Calibration drift (tutor MAE) | ≤ 0.4 per criterion across last 5 samples |
| Recording loss rate | ≤ 0.1% of finished sessions |
| Originality guard false-positive rate | ≤ 5% of AI-drafted cards rejected |

## Compliance

| Metric | Budget |
|--------|--------|
| Consent capture rate | 100% before any recording is persisted |
| Retention worker drift | ≤ 24 hours past `RetentionExpiresAt` |
| Audit event coverage | 100% of admin recording access |

## Operational

| Metric | Budget |
|--------|--------|
| Tutor queue median time-to-claim | < 30 minutes |
| Tutor queue idle-claim TTL | 15 minutes (auto-release) |
| LiveKit webhook delivery latency | p95 < 15 s after `room_finished` |

## Error-budget policy

- Each metric has a rolling 30-day budget.
- Budget burn ≥ 50% → release-freeze the affected surface; only fixes ship.
- Budget burn ≥ 100% → page on-call, post-mortem within 5 business days.
