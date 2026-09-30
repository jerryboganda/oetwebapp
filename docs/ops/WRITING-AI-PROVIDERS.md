# Writing AI Provider Architecture — Runbook

**Status:** authoritative operations runbook for the final Writing AI provider setup.
**Owner directive:** 2026-09-29 (revised) — Writing grading uses a 3-level automatic failover chain: **Claude Opus 5.5 (high) on the dedicated Max 5x subscription** (primary, retried once on any transient failure) → **Claude Opus 5.5 via the Anthropic API** (pay-as-you-go key) → **Codex subscription** (`gpt-6-sol`, high). No further benchmark spend.
**Owner directive 2026-09-30:** Speaking grading (`speaking.grade`) now also runs on the Claude sidecar, with the previous route as automatic fallback — see [§9](#9-speaking-grading-on-the-same-sidecar). Writing and Speaking share **one serial lane** on this sidecar.

---

## 0. TL;DR

```
writing.grade (+ coach / rewrite / ask / appeal / model-answer pregen)
        │
        ▼
WritingSubscriptionSelector  (auto | claude | codex; warn 80% / failover 90%)
        │
        ▼  failover chain (one AiOperation, one credit debit, one grade)
  L1 oet-writing-claude :8080   (claude CLI, Max 5x subscription)
     └─ retry once on transient failure
  L2 anthropic API row           (pay-as-you-go Anthropic key)
  L3 oet-writing-codex :8080    (codex CLI, ChatGPT subscription)
```

The two sidecars are registered as ordinary `AiProvider` rows (plus the existing
`anthropic` API row for level 2), so routing, usage logging, budgets, and the
admin AI board all work unchanged. Failover happens **inside one coordinated
`AiOperation`**, so a candidate is never double-graded or double-charged when
Claude hits its limit.

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
  `oet-api` containers **and `oet-ai-worker`** (comma-separated, alongside
  `ubag-vps-gateway-1`); `docker-compose.production.yml` now defaults to exactly this:
  ```
  OET_INTERNAL_AI_HOSTS=ubag-vps-gateway-1,oet-writing-claude,oet-writing-codex
  ```
  Without this the SSRF guard refuses the plain-HTTP internal base URLs.
- **Keyless rows:** the seeded rows store the literal marker `subscription-sidecar` (not
  ciphertext) as their key. The provider registry hands that marker back as the key, so a
  row works as seeded once it is active: no re-keying in `/admin/ai-providers` is needed.
  The marker only counts as a key while the row's `BaseUrl` host is on `OET_INTERNAL_AI_HOSTS`:
  a row an admin re-pointed at a public URL stops looking credentialed ("platform key missing")
  instead of sending the marker to a vendor. Calls to these rows do not take one of the API
  process's five platform-key concurrency permits (the sidecar's own serial lane is their limiter).
  The admin **Test** button on such a row calls the sidecar's `GET /healthz` (network path,
  SSRF allowlist and container liveness), not a completion, so it burns no subscription quota
  and never waits on the serial lane; CLI login state is checked with the smoke commands in §2.
- **Never an implicit default:** the gateway's "no pin, no route" fallthrough (the lowest
  `FailoverPriority` active keyed text-chat row) skips marker-key rows. The seeded priorities 1 and
  2 sit below every other row, so without that exclusion activating `writing-claude-sub` would make
  the Claude Max lane the default for every feature that has no route. The rows are reached only by
  an explicit pin (`SpeakingGradeChain`, the Writing pipeline, an admin calling
  `POST /v1/ai/complete` with a `provider`) or by a feature route set on purpose.
- **Network:** the sidecars are on `oet_agent_ctl`; `oet-api-blue`/`green` **and `oet-ai-worker`**
  join it (the worker executes queued grades). See `OWNER-AGENT-CONSOLE.md`.
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

- **Per-request chain (auto mode):** the selector picks the primary. The pipeline
  then walks **L1 Claude subscription → (retry once) → L2 Claude API → L3 Codex**,
  escalating only on transient/provider errors (timeout, network, 5xx, rate-limit,
  quota). Policy/quota/budget/duplicate refusals bubble up unchanged.
- **Weekly cap (proactive):** when Claude 5x weekly utilisation reaches the
  failover threshold (default 90%) **or** a quota signal is recorded, new requests
  start at **L2 (Claude API)** until the weekly window resets and utilisation drops
  below the warn threshold (hysteresis).
- A candidate submission **never fails** solely because the subscription is
  exhausted — it transparently escalates within the same operation.
- Every call records provider, model, outcome, and `FailoverTrace` /
  `failoverReason` in `AiUsageRecord`, so the admin can see exactly which level
  produced each grade and why it fell back.

---

## 6. Safety / billing guarantees

- Failover runs **inside one coordinated `AiOperation`** → no duplicate grade row,
  and the learner's AI credit is debited **exactly once** (the budget reservation
  spans the whole call, not each provider attempt).
- Subscription sidecars are metered at **$0.00** in the provider rows; the admin
  panel additionally shows an **estimated API-equivalent cost** (informational).
- Sidecars spawn one CLI process per request; the per-engine mutex serialises calls
  (subscription CLIs are not safe to run concurrently on one credential set). The
  **Claude** sidecar runs the CLI with every built-in tool removed (`--tools ""`), session
  persistence off (`--no-session-persistence`) and auto-memory off
  (`CLAUDE_CODE_DISABLE_AUTO_MEMORY=1`), with the prompt on stdin, so a graded prompt and
  reply are not written to `$CLAUDE_CONFIG_DIR/projects/-tmp` on the `oet_agent_home` volume
  it shares with the owner console. Other CLI bookkeeping under `CLAUDE_CONFIG_DIR`
  (credential refresh, caches) is outside those switches. Older images ran without them, so
  see §9 for the flag check before deploying the new image and the purge of the legacy
  files. The **Codex** sidecar has none of these switches (prompt on argv, read-only
  sandbox); whether `codex exec` keeps session files on disk is unverified. It carries
  Writing letters only (Speaking has no Codex leg).

---

## 7. Troubleshooting

| Symptom | Check |
|---|---|
| Claude sidecar 502s / empty | `docker logs oet-writing-claude`; probe `docker exec -u 10002 oet-writing-claude claude -p "OK"`; re-run §2.4 if auth lapsed |
| Codex sidecar 502s | `docker logs oet-writing-codex`; `docker exec -u 10002 oet-writing-codex codex login status`; re-login if needed |
| Backend "BaseUrl must use https://", or "Platform API key missing for ... writing-claude-sub / writing-codex-sub" | `OET_INTERNAL_AI_HOSTS` missing the sidecar hostnames (§3). The seeded marker key is only honoured for a row whose host is on that list, so a missing host now shows up as a missing key |
| All writing failing over constantly | Claude weekly cap exhausted — check `/admin/writing-ai` utilisation; raise `WRITING_CLAUDE_WEEKLY_TOKEN_CAP` only if the real allowance is larger |
| OAuth refresh contention | Two Claude processes sharing `oet_agent_home` — ensure only ONE claude sidecar + the console use it, never a second copy |
| Speaking grades all fall back to the API route | Gateway log line `AI provider call failed: ... provider=writing-claude-sub ... class=...` says why (§9); check the row is active, `OET_INTERNAL_AI_HOSTS` on **both** the API slots and `oet-ai-worker`, and `docker exec oet-ai-worker curl -sS -m 5 http://oet-writing-claude:8080/healthz` |
| `docker logs oet-writing-claude` empty on a 502 | Older sidecar image. Current images log one line per failure: `[writing-ai:claude] POST /v1/messages -> 502 engine_error in <ms>` (status + code + duration only, never CLI output). A duration near 300000 ms is the CLI timeout |

---

## 8. Re-auth

- **Claude:** the credential lives in `oet_agent_home`; re-auth via the agent
  console connect flow (it owns that volume), or `docker exec -it -u 10002
  oet-writing-claude claude auth login`.
- **Codex:** `docker exec -it -u 10002 oet-writing-codex codex login --device-auth`.

---

## 9. Speaking grading on the same sidecar

**Owner directive 2026-09-30.** `speaking.grade` (classic and v1.1 assessors) now runs through
`SpeakingGradeChain`:

```
speaking.grade
  L1  writing-claude-sub / claude-opus-5-5   (this sidecar; effort = WRITING_CLAUDE_EFFORT, high)
  L2  the default route (Anthropic API today) -- the request exactly as it was before this change
```

- **Config:** `Speaking__Grading__PinnedProviderCode` (default `writing-claude-sub`),
  `Speaking__Grading__PinnedModel` (default `claude-opus-5-5`) and
  `Speaking__Grading__PinnedTimeoutSeconds` (default `900`), from compose env
  `SPEAKING_GRADING_PINNED_PROVIDER` / `SPEAKING_GRADING_PINNED_MODEL` /
  `SPEAKING_GRADING_PINNED_TIMEOUT_SECONDS`. An empty provider turns the chain off (one plain
  call on the default route); an empty (set but blank) model means the provider row's default
  model. See `docs/speaking/ai-providers.md`.
- **Fallback trigger:** any provider-side failure of L1 (HTTP error, timeout or the L1 time
  budget below, circuit open, provider row inactive or missing) and a duplicate refusal of the
  pinned operation while it is `Indeterminate` (a dropped connection or timeout left its outcome
  unknown; it is never re-run, but L2 is a different provider under a different operation, so
  without this one transient sidecar failure would lock the session out of both routes).
  Quota/budget/policy refusals, every other duplicate or conflicting operation (for example an
  in-window `Completed` twin) and caller cancellation are **not** failed over. If both levels
  fail the learner sees the same generic `409 speaking_ai_unavailable` as before, and can retry.
- **Not steered by the Writing selector, but sharing its circuit.** Speaking pins
  `writing-claude-sub` directly in `SpeakingGradeChain`; it bypasses `WritingSubscriptionSelector`,
  so the Writing mode (`auto` / `claude` / `codex`) and the weekly-utilisation bands do not move
  Speaking: forcing Writing to `codex` leaves Speaking on the Claude lane (revert with the env key
  below). The provider circuit breaker is keyed per provider row, so Speaking and Writing share the
  `writing-claude-sub` circuit: `class=quota_exhausted` or `class=auth` from either opens it for
  both, and a burst of Speaking failures can push Writing to L2 (and the reverse). Speaking
  grades also count toward the sidecar's weekly usage counter, which drives Writing's 80% / 90%
  bands.
- **Requirements (all three, or every grade silently falls back to L2):** the
  `writing-claude-sub` row is **active**; `OET_INTERNAL_AI_HOSTS` lists `oet-writing-claude`
  on the API slots **and `oet-ai-worker`**; `oet-ai-worker` is on `oet_agent_ctl`
  (`docker-compose.production.yml`). Queued grades run **only** in the worker.
- **One serial lane.** The sidecar serialises every request with one mutex on one credential
  set, and it shares the Claude Max allowance with Writing (and the agent console's login).
  Speaking and Writing grades queue behind each other: capacity is roughly
  `3600 / seconds-per-grade` grades per hour for ALL subscription traffic, and a two-card
  mock is two serial grades. Measure real Speaking latency before relying on it at volume.
- **Timeouts:** the CLI is killed after **300 s** per attempt (`WRITING_CLI_TIMEOUT_MS`,
  default 300000, not exposed through `docker-compose.writing-ai.yml`); that clock starts only
  when the request reaches the front of the sidecar's queue. Queue wait is unbounded inside the
  sidecar, and the .NET client for this row (Anthropic dialect) keeps the 30-minute
  `AiRegistryClient` default (only OpenAI-compatible rows, such as the Codex sidecar, get 300 s).
  The gateway retries 5xx and rate-limit failures up to 3 times (a quota-exhausted 429 is
  quarantined, no retry), so one hung grade can hold the lane for up to ~15 minutes. To keep
  level 1 plus level 2 (about 12 minutes at maximum reasoning) inside the 30-minute operation
  lease, `SpeakingGradeChain` cancels the whole pinned attempt (queue wait and retries included)
  after `Speaking__Grading__PinnedTimeoutSeconds` (default `900`; `0` or less = no cap; values
  above `1500` are clamped) and falls back to L2. The budget ends with a cancellation the caller
  did not request, which the coordinator records as a cancelled (replayable) operation, not an
  indeterminate one. The sidecar cannot see that the client gave up, so an abandoned request still
  runs to completion and spends subscription allowance.
- **Deploying the persistence-off sidecar image (owner-run, in this order):**
  1. Validate the flags in the running container, which has the same CLI as the new image:
     `printf OK | docker exec -i -u 10002 oet-writing-claude claude -p --output-format json --no-session-persistence --tools ""`.
     A rejected flag would fail every subscription grade (Speaking would then fall back to the API
     route, which is the route that may be down), so if the CLI rejects one, drop only that flag from
     `writing-ai-sidecars/claude/server.mjs` before building.
  2. Deploy the image (`writing-ai.yml` builds it on push to main; bumping `WRITING_CLAUDE_IMAGE` and
     recreating `oet-writing-claude` is the manual step).
  3. Run one grade and confirm `docker exec -u 10002 oet-writing-claude sh -c 'ls /home/agent/.claude/projects/-tmp | wc -l'`
     does not grow.
  4. List `/home/agent/.claude/projects/-tmp/memory` for any auto-memory files, then purge the legacy
     copies written by older images:
     `docker exec -u 10002 oet-writing-claude rm -rf /home/agent/.claude/projects/-tmp`. `-tmp` is only
     this sidecar's working-directory slug; owner-console sessions use worktree paths (and its probe
     `/home/agent`), so they are unaffected. Until purged, those files stay for the console's 90-day
     retention sweep.
- **Same effort/model semantics as Writing:** the gateway still sends adaptive thinking,
  effort `max` and a large `max_tokens` for `speaking.grade`; the sidecar ignores all three and
  runs Opus 5.5 at its own `WRITING_CLAUDE_EFFORT` (high).
- **Diagnostics:** every failed provider call writes one structured API log line
  (`AI provider call failed: feature=... provider=... http=... class=... type=... code=...
  requestId=... providerError=...`, provider text redacted and capped at 300 characters; only
  ever in that log line). `class=quota_exhausted` or `class=auth` open the provider circuit at
  once (`GET /v1/admin/ai/circuits`, reset with
  `POST /v1/admin/ai/circuits/{key}/reset?kind=provider`). The sidecar itself logs status,
  error code and duration per failure.
- **Revert:** set `SPEAKING_GRADING_PINNED_PROVIDER=` (empty) in `/opt/oetwebapp/.env.production`
  and recreate the API slots and `oet-ai-worker`; grading returns to the default route only.
