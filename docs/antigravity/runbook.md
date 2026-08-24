# Antigravity Gateway — Operations Runbook

## Health & diagnostics

```bash
# on the VPS
docker exec oet-agent-gateway wget -qO- http://127.0.0.1:8305/v1/healthz
docker exec oet-agent-gateway wget -qO- http://127.0.0.1:8305/v1/quota
docker logs --tail=100 oet-agent-gateway
```

`/v1/healthz` reports auth mode, live session pool, and quota (global +
per-route remaining). `status != ok` means the auth adapter failed at
startup — check `GEMINI_API_KEY` / mode env vars and restart.

`/v1/readyz` is stricter than liveness: Docker and the blue/green rollout use
it, and it returns HTTP 503 while the configured auth adapter is unavailable.

## Error classes

| Symptom | Meaning | Action |
|---|---|---|
| 503 `quota: ...` | gateway daily/route budget exhausted | raise `AGENTGATEWAY_BUDGET_*`, or let resolver fallback absorb |
| 503 `upstream quota exhausted` | Gemini/Antigravity quota hit (429) | wait for the 5h refresh; consider Mode C flip (roadmap.md) |
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
