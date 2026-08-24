# Antigravity Integration — Roadmap & Flip-Day Procedure

## Shipped (Phase 0–3a, 2026-08-24)

- Phase 0: SDK verified on Windows amd64 (v0.1.14, bundled localharness
  launches; `examples/smoke_harness.py`).
- Phase 1: `oet-agent-gateway` — FastAPI, OpenAI-compatible
  `/v1/chat/completions` (the .NET OpenAiCompatible dialect consumes this
  directly), native SSE session API, quota governor, response cache,
  internal-token auth, fail-fast startup on missing key, 20 unit tests.
- Phase 2: 8 OET agents with rulebook-grounded personas + SKILL.md packages
  (writing examiner 6-criterion contract mirrors
  `WritingDualAssessmentService.ParseAiCriteria`).
- Phase 3 (DB/CI wiring): `AntigravityGatewaySeeder` provider + 18 route
  rows; compose services in dev/desktop/vps/production; GHCR build job +
  blue/green rollout integration; docs.
- Hardening pass (gateway v0.2, same day): per-route circuit breaker with
  fast-fail fallback handoff, 120s turn timeouts → 504, constant-time token
  compare, request caps (413), SSE keepalives, idle-session reaper,
  lock-safe pool eviction, graceful-shutdown drain, accurate prompt-vs-
  completion usage accounting, opt-in SDK structured outputs, optional
  shared Redis cache, Prometheus `/v1/metrics`, JSON logs; 38 tests.

## Next phases (scheduled, not yet started)

| Phase | Work | Gate |
|---|---|---|
| 3b | Admin route-editor rollout: flip `writing.sample_score` → agent 10% → 100%; Grafana + cost dashboard watch | parity ≥ current provider |
| 3c | Golden-set parity harness: 50-item writing + 30-item speaking patient-turn sets vs Anthropic baseline | parity rubric in `docs/antigravity/` |
| 4 | Desktop bundle: desktop runtime config gains `agentGateway` section; Mode B opt-in end-to-end on owner machine | offline-from-cloud AI works |
| 5 | Mobile validation (Capacitor Android/iOS vs staged backend; SignalR streaming) | device pass |
| 6 | Production hardening leftovers: scrape `/v1/metrics` into Grafana, alert rules from runbook table, OTel export if needed | dashboards live |

Note: the v0.2 hardening closed the code-side Phase 6 items that live in the
gateway itself (shared Redis cache option, metrics surface, alertable
signals). Remaining Phase 6 work is scraping/alerting wiring on the VPS.

## Flip-day (Mode C activation)

When Google ships SDK OAuth (watch: antigravity.google/changelog +
issue #20):

1. Add `SdkOAuthAuth` delegation to the official SDK OAuth client in
   `agent-gateway/src/oet_agent_gateway/auth.py`.
2. `AGENTGATEWAY_AUTH_MODE=sdk-oauth` in all env files.
3. Run `pytest agent-gateway/tests` + golden-set parity.
4. Promote route-by-route at 10% (existing swap procedure in
   `docs/speaking/ai-providers.md`).
5. Archive the Mode B (local-oauth) adapter.
