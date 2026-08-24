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

## Next phases

| Phase | Status | What remains |
|---|---|---|
| 3b | Ready | Admin route-editor rollout: flip `writing.sample_score` → agent 10% → watch Grafana/cost dashboards → 100% (`pnpm ai:parity` gate before each flip) |
| 3c | **Shipped** | Golden-set parity harness: `scripts/antigravity/parity/` (50 writing + 30 speaking items), `run_parity.py` dual-provider runner + gate, rubric in `docs/antigravity/parity-rubric.md`, `pnpm ai:parity` |
| 4 | **Shipped (code)** | Desktop: `DesktopRuntimeConfig.agentGatewayBaseUrl` (+ env `OET_DESKTOP_GATEWAY_URL`) wired through `runtime_info`; compose desktop entry already runs the local gateway. Owner device pass pending. |
| 5 | **Ready** | Mobile validation matrix in `docs/antigravity/mobile-validation.md` (incl. quota-exhaustion + circuit-open drills); owner device pass pending |
| 6 | **Shipped (gateway side)** | Prometheus alert rules `ops/prometheus/alerts-oet-gateway.yml` (breaker, budget-80%, timeouts, error burst); install on VPS monitoring stack |
| 7 | **Ready** | Flip-day checklist `docs/antigravity/flip-day-checklist.md` + live watcher `pnpm ai:flipday-watch` (PyPI pin vs latest, issue #20 state) |

Operational sign-offs that remain owner-side: 3b route flips, Phase 4 desktop
install smoke on a clean machine, Phase 5 physical Android/iOS pass.

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
