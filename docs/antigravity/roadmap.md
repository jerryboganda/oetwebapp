# Antigravity Integration — Roadmap & Flip-Day Procedure

## Shipped (Phase 0–2, 2026-08-24)

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

## Next phases (scheduled, not yet started)

| Phase | Work | Gate |
|---|---|---|
| 3b | Admin route-editor rollout: flip `writing.sample_score` → agent 10% → 100%; Grafana + cost dashboard watch | parity ≥ current provider |
| 3c | Golden-set parity harness: 50-item writing + 30-item speaking patient-turn sets vs Anthropic baseline | parity rubric in `docs/antigravity/` |
| 4 | Desktop bundle: desktop runtime config gains `agentGateway` section; Mode B opt-in end-to-end on owner machine | offline-from-cloud AI works |
| 5 | Mobile validation (Capacitor Android/iOS vs staged backend; SignalR streaming) | device pass |
| 6 | Production hardening: Redis-backed cache (multi-replica), OTel hooks → existing analytics, alerting at 80% budget | runbook live |

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
