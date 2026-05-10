# Observability And SLO Checklist

Use this checklist for launch readiness and route scorecards. Health checks are necessary but not enough; provider-backed and paid flows need explicit ownership, thresholds, and alerts.

## Baseline SLOs

Launch SLOs locked 2026-05-10 against RW-022. Threshold profile: **standard
(99.5% / p95 1.5s / err <1% per 5min)**. Stricter ratios may be applied per
route when justified by scorecard evidence.

| Area | Launch SLO | Primary signals |
| --- | --- | --- |
| Web/API availability | **99.5% monthly** | HTTP 5xx rate, uptime checks |
| Auth (sign-in / refresh / MFA / logout) | **p95 < 1.5 s, error rate < 1% per 5 min** | sign-in failures, refresh failures, MFA challenges |
| Billing (checkout / webhook / invoice) | **error rate < 1% per 5 min, no unreviewed payment failures** | webhook failures, checkout failures, invoice errors |
| Writing / Listening / Reading / Speaking | **p95 < 1.5 s, error rate < 1% per 5 min** | grading job age, evaluation failures |
| Conversation (AI session) | **p95 reply < 3 s (advisory; depends on provider), error rate < 1% per 5 min** | reply latency, ASR/TTS provider errors |
| Admin console | **p95 < 1.5 s, error rate < 1% per 5 min** | admin endpoint failures |
| AI / provider gateway | **fail-closed visible, refusal rate tracked** | provider errors, quota exhaustion, refusal counts |
| Background jobs | **oldest critical job < 10 min** | queue depth, oldest job age, worker errors |
| Upload / audio pipelines | **error rate < 1% per 5 min, no silent failures** | upload errors, processing failures, storage errors |


## Required Dashboards

- API latency and error rate.
- Auth and refresh-token failures.
- Billing webhook and checkout health.
- AI usage and provider fallback/refusal trends.
- ASR/TTS/OCR/PDF extraction provider health.
- Upload/storage processing failures.
- Database health and migration state.
- Background job queue age and failure count.

## Alert Ownership

All alerts owned by **Dr Faisal Maqsood** for the v1 launch (single-owner
rotation accepted; reassess at first hire). Runbook: `incident-response-runbook.md`.

| Alert | Threshold | Owner | Runbook |
| --- | --- | --- | --- |
| API 5xx spike | 5xx rate > 2% for 5 min | Dr Faisal Maqsood | incident-response-runbook.md |
| Auth failure spike | sign-in/refresh failures > 1% for 5 min | Dr Faisal Maqsood | incident-response-runbook.md |
| Provider outage | provider route unavailable or refusing for 5 min | Dr Faisal Maqsood | incident-response-runbook.md |
| Upload failure | upload error rate > 1% for 5 min | Dr Faisal Maqsood | incident-response-runbook.md |
| Queue backlog | oldest critical job > 10 min | Dr Faisal Maqsood | incident-response-runbook.md |
| Health-check failure | `/api/health` non-200 for 3 consecutive min | Dr Faisal Maqsood | incident-response-runbook.md |
| Latency regression | p95 > 2 s for 10 min on T0 routes | Dr Faisal Maqsood | incident-response-runbook.md |


## Route Scorecard Evidence

Attach the following to each T0/T1 scorecard:

- Smoke test link.
- `scripts/observability-smoke.sh` artifact path, base URL, optional API base URL, direct health/readiness status, and exit status.
- Error-rate and latency dashboard link.
- Relevant alert names.
- On-call or escalation owner.
- Accepted risk notes.
