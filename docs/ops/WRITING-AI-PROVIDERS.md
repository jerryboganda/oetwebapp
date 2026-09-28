# Writing AI Provider Architecture — Runbook

**Status:** authoritative operations runbook for the final Writing AI provider setup.
**Owner directive:** 2026-09-29 — Claude Opus 5.5 (effort `high`) on the **dedicated Claude Max 5x subscription** is the primary Writing grader; the **Codex subscription** (`gpt-6-sol`, high thinking) is the automatic fallback. No further benchmark spend.

---

## 0. TL;DR

```
writing.grade (+ coach / rewrite / ask / appeal / model-answer pregen)
        │
        ▼
AiWritingSubscriptionSelector  (auto | claude | codex; warn 80% / failover 90%)
        │
   ┌────┴─────────────────────┐
   ▼                           ▼
oet-writing-claude :8080   oet-writing-codex :8080
(claude CLI, Max 5x)       (codex CLI, ChatGPT)
   │                           │
   └─► via oet-agent-egress allowlist proxy ─► api.anthropic.com / chatgpt.com
```

Both sidecars are registered as ordinary `AiProvider` rows, so routing, usage
logging, budgets, and the admin AI board all work unchanged. Failover happens
**inside one coordinated `AiOperation`**, so a candidate is never double-graded
or double-charged when Claude hits its limit.

---

## 1. Credentials & separation

| Provider | Subscription | Credential home | Notes |
|---|---|---|---|
| Claude (primary) | **Dedicated Claude Max 5x** (`drahmedhesham222@gmail.com`) | `oet_agent_home` volume — **shared with the agent console** | One credential copy only. Sharing avoids OAuth refresh-token rotation conflicts. The agent console must be re-logged-in with the personal account to fully free the 5x allowance. |
| Codex (fallback) | Codex / ChatGPT subscription | `oet_writing_codex_home` volume — **dedicated, NOT shared** | The agent console's `/etc/codex/requirements.toml` forces `approval_policy=UnlessTrusted`, which hangs headless `codex exec`; this sidecar uses its own login so it can run `Never`. |
| Live Agent Console | Owner's **personal** Claude account | `oet_agent_home` (unchanged) | **Untouched by this setup.** Currently shares the 5x login until re-auth. |

**Why no API keys:** consumer Claude/ChatGPT subscriptions include no API access
(verified 21 Sep 2026 benchmark §4.2). The sidecars run the vendor CLIs headless
— the only supported way to consume a subscription programmatically.

---

## 2. First-time deploy

1. **Build & push images** (GitHub Actions `writing-ai.yml`; never build on the VPS).
   ```bash
   # produced tags, e.g.
   # ghcr.io/jerryboganda/oetwebapp-writing-claude:<sha>
   # ghcr.io/jerryboganda/oetwebapp-writing-codex:<sha>
   ```
2. **Env file** `/opt/oetwebapp/.deploy/writing-ai.env`:
   ```bash
   WRITING_CLAUDE_IMAGE=ghcr.io/jerryboganda/oetwebapp-writing-claude:<sha>
   WRITING_CODEX_IMAGE=ghcr.io/jerryboganda/oetwebapp-writing-codex:<sha>
   WRITING_EGRESS_PROXY=http://oet-agent-egress:3128
   WRITING_CLAUDE_MODEL=claude-opus-5-5
   WRITING_CLAUDE_EFFORT=high
   WRITING_CLAUDE_WEEKLY_TOKEN_CAP=0   # set a conservative cap once baseline usage is known
   WRITING_CODEX_MODEL=gpt-6-sol
   WRITING_CODEX_EFFORT=high
   ```
3. **Pull + up** on the VPS:
   ```bash
   docker compose --env-file /opt/oetwebapp/.deploy/writing-ai.env \
     -f /opt/oetwebapp/docker-compose.writing-ai.yml pull
   docker compose --env-file /opt/oetwebapp/.deploy/writing-ai.env \
     -f /opt/oetwebapp/docker-compose.writing-ai.yml up -d --no-build --wait
   ```
4. **Codex one-time login** (Claude reuses the existing 5x login — no step needed):
   ```bash
   docker exec -it -u 10002 oet-writing-codex codex login --device-auth
   # complete the device-code flow in a browser as the Codex/ChatGPT account
   docker exec -u 10002 oet-writing-codex codex login status   # expect "Logged in using ChatGPT"
   ```
5. **Smoke both engines:**
   ```bash
   docker exec -u 10002 oet-writing-claude claude -p "Reply with exactly: OK"
   docker exec -u 10002 oet-writing-codex sh -lc 'cd /tmp && codex exec --skip-git-repo-check "Reply with exactly: OK"'
   ```
6. **Register + route** (§4). Until the admin flips the routes, writing stays on the existing Anthropic API key.

---

## 3. Backend wiring

- **Provider rows** are seeded idempotently by `WritingSubscriptionProviderSeeder`
  (codes `writing-claude-sub` and `writing-codex-sub`), pointing at
  `http://oet-writing-claude:8080` and `http://oet-writing-codex:8080`.
- **Internal-host allowlist:** add both hostnames to `OET_INTERNAL_AI_HOSTS` on the
  `oet-api` containers (comma-separated, alongside `ubag-vps-gateway-1`):
  ```
  OET_INTERNAL_AI_HOSTS=ubag-vps-gateway-1,oet-writing-claude,oet-writing-codex
  ```
  Without this the SSRF guard refuses the plain-HTTP internal base URLs.
- **Feature routes** for the six writing codes are seeded to
  primary `writing-claude-sub` / fallback `writing-codex-sub`. The selector
  (§5) consults quota + mode before each call.

---

## 4. Admin control

`/admin/writing-ai` (requires `AdminAiConfig`):

- **Mode selector:** `auto` (Claude→Codex) · `claude` (force) · `codex` (force).
- **Thresholds:** warn % (default 80), failover % (default 90).
- **KPIs:** letters graded today · this week · Claude weekly utilisation + reset
  time · estimated allowance remaining · current primary provider/model ·
  fallback-routed count · Codex tokens + estimated API-equivalent cost.
- **Banners:** warning at ≥ warn %, failover-active at ≥ failover %, sidecar-down.

---

## 5. Failover semantics

- **Auto mode** uses Claude until the weekly utilisation reaches the failover
  threshold **or** Claude returns a quota/rate-limit signal, then routes new
  requests to Codex. It returns to Claude automatically once the weekly window
  resets and the Claude sidecar is healthy (hysteresis: utilisation must fall
  below the warn threshold).
- A candidate submission **never fails** solely because Claude is exhausted —
  it transparently retries on Codex within the same operation.
- Every call records provider, model, outcome, and `FailoverTrace` /
  `failoverReason` in `AiUsageRecord`.

---

## 6. Safety / billing guarantees

- Failover runs **inside one coordinated `AiOperation`** → no duplicate grade row,
  and the learner's AI credit is debited **exactly once** (the budget reservation
  spans the whole call, not each provider attempt).
- Subscription sidecars are metered at **$0.00** in the provider rows; the admin
  panel additionally shows an **estimated API-equivalent cost** (informational).
- Sidecars spawn one CLI process per request, tools disabled, no FS writes for
  candidate content; the per-engine mutex serialises calls (subscription CLIs are
  not safe to run concurrently on one credential set).

---

## 7. Troubleshooting

| Symptom | Check |
|---|---|
| Claude sidecar 502s / empty | `docker logs oet-writing-claude`; probe `docker exec -u 10002 oet-writing-claude claude -p "OK"`; re-run §2.4 if auth lapsed |
| Codex sidecar 502s | `docker logs oet-writing-codex`; `docker exec -u 10002 oet-writing-codex codex login status`; re-login if needed |
| Backend "BaseUrl must use https://" | `OET_INTERNAL_AI_HOSTS` missing the sidecar hostnames (§3) |
| All writing failing over constantly | Claude weekly cap exhausted — check `/admin/writing-ai` utilisation; raise `WRITING_CLAUDE_WEEKLY_TOKEN_CAP` only if the real allowance is larger |
| OAuth refresh contention | Two Claude processes sharing `oet_agent_home` — ensure only ONE claude sidecar + the console use it, never a second copy |

---

## 8. Re-auth

- **Claude:** the credential lives in `oet_agent_home`; re-auth via the agent
  console connect flow (it owns that volume), or `docker exec -it -u 10002
  oet-writing-claude claude auth login`.
- **Codex:** `docker exec -it -u 10002 oet-writing-codex codex login --device-auth`.
