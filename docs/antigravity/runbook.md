# Antigravity Gateway — Operations Runbook

## Health & diagnostics

```bash
# on the VPS
docker exec oet-agent-gateway wget -qO- http://127.0.0.1:8305/v1/healthz
docker exec oet-agent-gateway wget -qO- http://127.0.0.1:8305/v1/quota
docker logs --tail=100 oet-agent-gateway
```

`/v1/healthz` reports auth mode, live session pool, quota (global + per-route
remaining), circuit states, cache backend, and in-flight turns. `status !=
ok` means the auth adapter failed at startup — check `GEMINI_API_KEY` /
mode env vars and restart. A `degraded` status with `auth_ready: true`
means the optional Redis cache is unreachable (scoring continues from the
in-process fallback cache).

`/v1/readyz` is stricter than liveness: Docker and the blue/green rollout use
it, and it returns HTTP 503 while the configured auth adapter is unavailable.

## Metrics & alerting (v0.2)

`GET /v1/metrics` exposes Prometheus counters/gauges (token-guarded):

| Metric | Meaning |
|---|---|
| `oetgw_http_requests_total{method,path,status}` | traffic + error rates per endpoint |
| `oetgw_turns_total{agent,outcome}` | ok / timeout / quota / error / circuit_open / cancelled |
| `oetgw_cache_hits_total{agent}` | response-cache savings |
| `oetgw_retries_total{route}` | 429/hang retries after backoff |
| `oetgw_circuit_opened_total{route}` | breaker trips (fallback storms) |
| `oetgw_auth_failures_total` | internal-token mismatches |
| `oetgw_inflight_turns` / `oetgw_sessions_alive` | live load gauges |
| `oetgw_quota_global_remaining_tokens` | daily budget headroom |

Suggested alerts: `rate(oetgw_turns_total{outcome="circuit_open"}[5m]) > 0`
(fallback engaged), `oetgw_quota_global_remaining_tokens < 20% of budget at
80% burn`, `increase(oetgw_turns_total{outcome="timeout"}[15m]) > 5`.

## Circuit breaker & timeouts (v0.2)

Each budget route has a breaker: after `AGENTGATEWAY_CIRCUIT_FAILURE_THRESHOLD`
(8) consecutive upstream failures (429, harness crash, turn timeout) it opens
and fast-fails 503 for `AGENTGATEWAY_CIRCUIT_COOLDOWN_SECONDS` (90s). This is
deliberate: the .NET `AiFeatureRouteResolver` treats gateway 503 as its
fallback signal, so students are served by Anthropic/OpenAI-compatible within
milliseconds instead of waiting on a dead upstream. One probe after cooldown;
success closes the breaker. `circuits` in `/v1/healthz` shows live state.

Every harness call is capped by `AGENTGATEWAY_TURN_TIMEOUT_SECONDS` (120);
timeouts map to HTTP 504 and count toward the breaker.

## Graceful shutdown

On SIGTERM (blue/green swap) the gateway drains in-flight turns for
`AGENTGATEWAY_DRAIN_TIMEOUT_SECONDS` before exiting; compose grants a
45s `stop_grace_period`. Idle harness sessions are reaped every 60s after
900s unused (`AGENTGATEWAY_SESSION_IDLE_TTL_SECONDS`).

## Error classes

| Symptom | Meaning | Action |
|---|---|---|
| 503 `quota: ...` | gateway daily/route budget exhausted | raise `AGENTGATEWAY_BUDGET_*`, or let resolver fallback absorb |
| 503 `upstream quota exhausted` | Gemini/Antigravity quota hit (429) | wait for the 5h refresh; consider Mode C flip (roadmap.md) |
| 503 `circuit open for '<route>'` | breaker open: upstream failing repeatedly | check Gemini status/quota; auto half-open probe follows the cooldown |
| 504 `upstream turn timed out` | harness call exceeded turn timeout | inspect `docker logs oet-agent-gateway`; if frequent raise timeout or check VPS resources |
| 413 payload too large | request exceeded message/char caps | expected guard; backend should not send such payloads |
| 400 `Unknown agent` | route Model column points at a missing agent | check `AiFeatureRoutes` row vs `GET /v1/agents` |
| 401 `invalid internal token` | token mismatch between .NET row and gateway env | re-paste token in /admin/ai-providers |
| slow first turn | harness cold start per new session | `AGENTGATEWAY_MAX_SESSIONS` pool keeps warm sessions; raise on VPS |

## Credential rotation

1. Rotate in AI Studio (key restriction: VPS egress IP).
2. Update `GEMINI_API_KEY` in `.env.production` on the VPS.
3. `docker compose --env-file .env.production -f docker-compose.production.yml up -d --no-build --force-recreate agent-gateway`
4. Confirm `/v1/healthz` ok and run one writing grade.

## Budget tuning

```bash
AGENTGATEWAY_BUDGET_TOKENS_PER_DAY=4000000            # global
AGENTGATEWAY_BUDGET_BY_ROUTE='{"writing-examiner":800000,"speaking-interlocutor":600000}'
```

Route names = agent names. Budget resets daily (UTC date). The .NET layer's
existing per-feature fallback chain engages automatically on gateway 503s.
