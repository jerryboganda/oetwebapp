# OET Agent Gateway

Python 3.12 service hosting the **Google Antigravity SDK** agent runtime for
the OET with Dr. Hesham platform (web + desktop + mobile AI features).

## Quickstart (Windows host)

```powershell
py -3.10 -m venv .venv
.venv\Scripts\python.exe -m pip install -e ".[dev]"
copy .env.example .env      # then set GEMINI_API_KEY + internal token
.venv\Scripts\python.exe -m oet_agent_gateway.main
```

Check `http://127.0.0.1:8305/v1/healthz` and `/v1/agents`.

## API surface

| Endpoint | Purpose |
|---|---|
| `GET /v1/healthz` | readiness: auth mode, pool, quota, circuits, cache, in-flight turns |
| `GET /v1/metrics` | Prometheus exposition (counters/gauges for turns, retries, cache hits, breaker trips) |
| `GET /v1/agents` | enabled agent specs (+ JSON schema when structured outputs are opted in) |
| `GET /v1/quota` | budget governor state |
| `POST /v1/chat/completions` | OpenAI-compatible (`.NET AiProviderRegistry` dialect calls this with `stream:false`); `model` = `agent:<name>` |
| `POST /v1/sessions` + `POST /v1/sessions/{id}/messages` | native SSE stream: thought / tool_call / content / usage events, keepalive pings on silence |

Internal auth: when `AGENTGATEWAY_INTERNAL_SERVICE_TOKEN` is set, every
request must send `X-Oet-Internal-Token` (constant-time compare; matches the
encrypted API key of the `antigravity-gateway` row in `/admin/ai-providers`).

## Hardening & performance (v0.2)

- **Circuit breaker** per budget route: 8 consecutive upstream failures →
  fast-fail 503 for 90s so the .NET resolver falls back instantly.
- **Turn timeout** 120s (`AGENTGATEWAY_TURN_TIMEOUT_SECONDS`) → HTTP 504;
  session locks are always released.
- **Request caps**: max 32 messages / 100k chars → 413.
- **SSE keepalives** every 15s of stream silence (nginx/LB idle safety).
- **Session hygiene**: idle harness sessions reaped after 15min; eviction
  never drops a session mid-turn.
- **Graceful shutdown**: bounded drain window before exit
  (`stop_grace_period: 45s` in compose).
- **Accurate usage**: prompt/completion token estimates measure the exact
  upstream prompt and feed both quota spend and OpenAI-shaped usage.
- **Optional shared Redis cache** (`AGENTGATEWAY_REDIS_URL`) for multi-replica
  deployments — falls back to the in-process LRU on any Redis problem.
- **Opt-in structured outputs**: `AGENTGATEWAY_STRUCTURED_OUTPUT_AGENTS`
  promotes the Pydantic schema (`schemas.py`) onto listed agents.
- **Observability**: `/v1/metrics` + optional JSON logs
  (`AGENTGATEWAY_LOG_JSON=true`). See `docs/antigravity/runbook.md`.

## Auth modes

See `docs/antigravity/auth.md` (repo root):

- `gemini-key` (default, production) — official Gemini API key, hardened.
- `local-oauth` (opt-in, personal) — reuses the `agy` CLI session for the
  owner's Google AI Pro Antigravity quota. Risk notice applies.
- `sdk-oauth` — flip-day stub (when Google ships SDK OAuth).

## Tests

```powershell
.venv\Scripts\python.exe -m pytest tests -q   # no network/harness required
```

`examples/smoke_harness.py` proves the bundled localharness binary on this
platform (expect the key-required validation error without a key; a real
`PONG` turn with one).
