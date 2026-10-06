# ops/

Operational artefacts that are not application code.

| Path | What it is | Status |
| --- | --- | --- |
| `prometheus/alerts-oet-gateway.yml` | Alert rules for the agent gateway's `oetgw_*` metrics | Live: the gateway exposes `/v1/metrics`. Needs a Prometheus to load it (none ships in this repository; see below). |

## Grafana dashboards: none ship, on purpose

`ops/dashboards/` held three Speaking dashboards (funnel, quality, LiveKit health). They were deleted on
2026-10-06 because every query in them was dead:

- `speaking-funnel.json` queried `events{name="..."}`, which is not a Prometheus metric (those events go to
  the product-analytics destination, see `docs/analytics/speaking-events.md`);
- `speaking-livekit.json` and `speaking-quality.json` queried `speaking_live_rooms_active`,
  `speaking_livekit_*`, `speaking_assessment_delta_absolute`, `speaking_tutor_mae` and similar. A search of
  every source file (C#, TypeScript, Python) finds no code that emits any of them.

A dashboard over metrics nothing emits is worse than no dashboard: it reads as "all quiet". Add a dashboard
only together with the code that emits its metrics.

## What exists for monitoring today

- Sentry errors; performance traces and profiles are sampled at 0 by default, so there is no latency telemetry unless that is changed.
- `/health/live`, `/health/ready`, the deploy-time `scripts/observability-smoke.sh`, `docker stats`.
- The admin JSON surfaces under `/v1/admin/**` (AI usage, `/v1/admin/ai/live-voice/health`, readiness).
- The agent gateway's Prometheus endpoint (`oetgw_*`).
- Load evidence on demand: the k6 harness under `tests/load/`, run manually by the owner from a self-provisioned
  load generator (no CI runs it, agents never run it), see `docs/ops/LOAD-TESTING.md`.

There is no Prometheus, Grafana, Alertmanager, node-exporter or cAdvisor service in any compose file in this
repository. Whether the "VPS monitoring stack" that the alert-rule header mentions exists outside the repo
cannot be verified from source. How scrape targets for the primary VPS and for enrolled helper nodes should
be wired, and which source each helper metric comes from, is specified in
`docs/ops/PROMETHEUS-SCRAPE-TARGETS.md`.
