# Writing AI Provider Architecture — Runbook

**Status:** authoritative operations runbook for the final Writing AI provider setup.
**Owner rule MAX-ALWAYS-ON (2 Oct 2026, supersedes the 29 Sep chain):** every Writing grading run starts on **Claude Opus 5.5 (high) on the dedicated Max 5x subscription**. Nothing persisted or computed (a mode, a utilisation threshold, a quota marker, a circuit, a readiness probe) can switch Max off or skip it. Only inside one run, after Max actually returned an error, does grading fail over to **Claude Opus 5.5 via the Anthropic API** and then **Codex GPT-6.1 Sol** (`gpt-6.1-sol`, high). Nothing carries over to the next run.
**Owner directive 2026-09-30:** Speaking grading (`speaking.grade`) now also runs on the Claude sidecar, with the previous route as automatic fallback — see [§9](#9-speaking-grading-on-the-same-sidecar). Writing and Speaking share **one serial lane** on this sidecar.

---

## 0. TL;DR

```
writing.grade — one grading RUN (first grade, automatic re-queue or manual Retry)
        │
        ▼  WritingSubscriptionSelector → always Max (reason max_always_first)
  L1 oet-writing-claude :8080   claude CLI, Max 5x           ×2 attempts, 420 s each
  L2 anthropic API row          pay-as-you-go Anthropic key   ×1 attempt,  150 s
  L3 oet-writing-codex :8080    codex CLI, gpt-6.1-sol        ×2 attempts, 420 s each
        (run deadline 1200 s; under 20 s left = stop; one AiOperation per attempt and hop)
```

The two sidecars are registered as ordinary `AiProvider` rows (plus the existing
`anthropic` API row for level 2), so routing, usage logging, budgets, and the
admin AI board all work unchanged. Every attempt is its own `AiOperation` on its own
resource slot (`ResourceVersion = 100000 + epoch·256 + hop·64 + attempt·16`), so a dead or
duplicated operation is never replayed into the next attempt or the next run. The learner is
charged once per letter whatever the number of attempts, and the unique grade index allows one
grade per letter.

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
   WRITING_CLAUDE_WEEKLY_TOKEN_CAP=0   # admin usage estimate only; never switches providers
   WRITING_CODEX_MODEL=gpt-6.1-sol
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
- **Seeder self-heal (every boot):** `writing-claude-sub` is kept active (MAX-ALWAYS-ON), and a
  `writing-codex-sub` row still on the old `gpt-6-sol` default is moved to `gpt-6.1-sol` (a model an
  admin chose deliberately is left alone). The grade pins provider and model per hop; feature routes
  do not steer `writing.grade`.

---

## 4. Admin control

`/admin/writing-ai` (requires `AdminAiConfig`):

- **Max is always on.** `PUT /v1/admin/ai/writing-provider` accepts only `auto`/`claude`; `codex`
  (or anything else) is refused with `400 max_subscription_always_on`. The `writing-claude-sub` row
  cannot be deactivated or deleted (`409 max_subscription_always_on`); its other fields stay editable.
  A stored mode, warn/failover percentage or legacy quota marker is inert: routing never reads it.
  Migration `20270104090000` cleared the live marker and turned a stored `codex` mode back to `auto`.
- **KPIs (information only):** letters graded today and this week, the Claude weekly usage estimate
  and reset time, API and Codex calls. The weekly estimate never switches providers.

---

## 5. Failover semantics

- **Every run, same plan:** Max ×2 → Anthropic API ×1 → Codex ×2. Each attempt gets
  `min(its budget, what is left of the 1200 s run)` on a linked token (an expired attempt is
  recorded Cancelled, i.e. replayable) and parses the reply inside the attempt (an unreadable reply
  fails over too). Budgets: `Writing__GradeChain__L1AttemptSeconds` 420, `L2AttemptSeconds` 150,
  `L3AttemptSeconds` 420, `ChainDeadlineSeconds` 1200.
- **What fails over:** every provider-side failure, including a duplicate, conflicting or in-flight
  operation slot, a timeout and an unreadable answer. A typed quota, auth or invalid-request answer
  only skips the SAME route's second attempt (the next route still runs). Never failed over: the
  learner's AI quota refusal, the platform budget (only while spend caps are on), the feature
  policy, an ungrounded prompt, the mock ban, and the caller's own cancellation.
- **No sticky state:** nothing a run sees is written for the next run. The provider circuit never
  opens for `writing-claude-sub` (`AiCircuitBreakerStore.IsAlwaysOn`); the API and Codex rows keep
  their circuits.
- **Auto-resume:** a run that ends without a grade never wedges in `grading`:
  - a retryable failure with re-queues left goes back to `queued` after 2 / 5 / 15 / 30 min
    (`Writing__GradeChain__MaxAutoRetries` 4, `BackoffMinutes`); the page says "Your letter is
    saved. This is taking longer than usual." and the credit hold stays on the letter;
  - re-queues spent, a credits refusal, or a non-retryable outcome → `failed`; Retry is offered
    unless it cannot help;
  - worker shutdown → `queued` at once without spending a re-queue; `WritingGradeShutdownRequeue`
    (API slots and `oet-ai-worker`) also re-queues this process's own claims on stop;
  - a hard kill → the batch cron (every minute, 5 rows) reclaims a `grading` claim older than the
    25-minute lease (`WritingGradeTimings.StaleClaimLease`), and Retry is offered on it.

  Failure writes are fenced on the run's claim owner. `MaxAutoRetries=0` restores the old
  "fail at once" behaviour without code.
- **Candidate-safe failure codes** (`WritingSubmission.FailureCode`, never a provider name):
  `grading_delayed` (every route failed, or canon/DB errors — re-queued), `credits_insufficient`
  (plan or credit refusal — failed, Retry after a top-up), `service_paused` (platform gate, budget,
  policy — re-queued), `task_not_ready` / `manual_review` / `letter_invalid` (failed, no Retry).
  The submission DTO adds `failureCode`, `canRetry`, `autoRetrying` and `attemptCount`.
- **Spend caps:** platform USD caps refuse a call only while `AiGlobalPolicy.EnforceSpendCaps` is on
  (off by default since 2026-10-02; `docs/AI-USAGE-POLICY.md` §7). The kill switch, per-feature
  kill list, per-user disable, learner credits and plan token caps still apply; a credit-funded
  Writing grade is sent with `FreeSampleGrant=true`, so the plan feature list and token counters do
  not refuse it.
- **QA-only fault switch (WAI-05, off by default, no seed):** Admin › Feature Flags rows
  `writing_grade_fault:{userId}` (every hop fails) or `writing_grade_fault_l1l2:{userId}` (Max and
  the API fail, Codex serves for real). `RolloutPercentage` 1-5 = the first N runs of each
  submission fail (0 ⇒ 1, capped at 5); a faulted hop throws before any provider call (no usage
  row, no circuit effect); while a flag is active that learner's failures are not re-queued, so the
  real `failed` row and its Retry are proven (run N+1 grades). A flag that is absent, disabled,
  unreadable or not updated for 24 h is off (fails closed). Code: `WritingQaFault`.
- Every call records provider, model, outcome and failure class in `AiUsageRecord`.

---

## 6. Safety / billing guarantees

- One `AiOperation` per attempt and hop (never one for the whole chain). The unique grade index
  allows one grade per letter, and the learner's credit is debited **exactly once**, at task open
  (a revision, or a letter submitted without opening the task, pays once at its first grade). A
  failed run keeps the hold; Retry costs nothing; nothing is refunded automatically.
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
| Many grades served by the API or Codex | Max is erroring inside each run (it is still tried first every time): `docker logs oet-writing-claude`, `GET /readyz` (display only: `ready`, `reason`, `queueDepth`, `authOk`, `plan`; routing never reads it) and `/usage` rows with `class=quota_exhausted` / `auth` |
| Requests answered `503 lane_busy` | Sidecar lane full: `WRITING_QUEUE_MAX` (40) already waiting, or a request waited `WRITING_QUEUE_WAIT_MS` (420000). That attempt fails over; the sidecar is never switched off. CLI timeout `WRITING_CLI_TIMEOUT_MS` 300000, lane width `WRITING_LANE_CONCURRENCY` 1 |
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
- **Not steered by the Writing selector.** Speaking pins `writing-claude-sub` directly in
  `SpeakingGradeChain`. The `writing-claude-sub` provider circuit never opens (MAX-ALWAYS-ON), so
  neither Speaking nor Writing failures can switch the other off. Speaking grades count toward the
  sidecar's weekly usage estimate, which is information only.
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
  when the request reaches the front of the sidecar's queue. Queue wait is bounded by
  `WRITING_QUEUE_WAIT_MS` (420000; then `503 lane_busy`), and the .NET client for this row (Anthropic dialect) keeps the 30-minute
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
  ever in that log line). `class=quota_exhausted` or `class=auth` open a provider circuit at
  once (never the `writing-claude-sub` one) (`GET /v1/admin/ai/circuits`, reset with
  `POST /v1/admin/ai/circuits/{key}/reset?kind=provider`). The sidecar itself logs status,
  error code and duration per failure.
- **Revert:** set `SPEAKING_GRADING_PINNED_PROVIDER=` (empty) in `/opt/oetwebapp/.env.production`
  and recreate the API slots and `oet-ai-worker`; grading returns to the default route only.
