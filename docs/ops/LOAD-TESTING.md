# Load testing the OET platform

> **Inert manual tools. Run manually by the owner from a self-provisioned load generator; no CI runs this; agents
> never run it.** There is no load-test or benchmark workflow in this repository on any trigger (not even
> `workflow_dispatch`): AGENTS.md "NO AUTOMATED QA ANYWHERE" bars it, and `scripts/deploy/verify-pipeline-contract.mjs`
> fails the build if a workflow runs `k6` or any test runner. Nothing here has been run by CI, and no number from it
> is claimed until the owner measures one and reports it.

Owner program 2026-10-05: prove, with numbers, that the primary VPS can serve **1,000 distinct active learners**
with **100 live AI speaking sessions** and **50 learner-tutor rooms**, and that **overload queues instead of
collapsing**. This page is the runbook for the owner's own runs, aimed at a **non-production** stack. Nothing in
this repository runs a load test on a schedule, on a push, from CI, or against production.

## 0. What a run proves, and what it does not

A run is **application and hardware validation**: the OET API, the Next.js web slot (the BFF every browser goes
through), SignalR over long-polling, PostgreSQL and its connection pool, the job and grading queues, and the
host's CPU and memory, under a realistic learner mix.

It does **not** validate external provider capacity. OpenAI GPT-Live, Gemini Live, LiveKit and the Claude Max /
Codex subscription lanes are driven against simulators with a configured latency. Their real concurrency limits,
rate limits and quotas are an **owner-supplied assumption**, and the report says so on its first lines. A passing
run also does not mean "release safe": it is evidence for one commit on one stack. The production pipeline
(`Build images` then `Deploy production`) is unchanged and has its own gates.

## 1. The owner's targets (all gating)

| Target | Limit | How it is measured |
| --- | --- | --- |
| Critical API reads (bootstrap, dashboard, entitlement, subscription, readiness, study plan, engagement, progress) | p95 <= 500 ms, p99 <= 1.5 s | k6 `http_req_duration{class:critical-read,phase:steady}` |
| Exam save (Reading answer, Listening answer, Writing draft) | p95 <= 500 ms | `{class:exam-save}` |
| Exam submission (Reading submit, Writing submit) | p95 <= 500 ms | `{class:submission}` |
| Live-session setup (speaking session create, consent, warm-up, preflight, provider offer or token, room create and token) | p95 <= 1 s | `{class:live-setup}` |
| Unexpected failures | < 0.1 % | `oet_unexpected_failure{phase:steady}` (see section 7) |
| Acknowledged saves | none lost | `oet_lost_ack_save`: after each attempt, the stored answers must equal the last acknowledged ones |
| Duplicate credits or charges | none | `oet_idempotency_violation` (a replayed submit returns the same submission, a page refresh never charges twice) plus the post-run ledger audit |
| Overload | queues, does not collapse; existing sessions survive | during the surge: established sessions >= 99 % OK, collapse responses (5xx other than a graceful 503, or no response) < 1 %; recovery meets the steady targets again |
| Admission queue (live AI speaking cap; on by default at 100) | waiting starts no timer and holds no credit | `oet_credit_consumed_while_queued`, `oet_timer_started_while_queued` must be 0 |

Grading is judged on queue drain, not request latency: `oet_ai_assess_ms` p95 < 10 minutes (the oldest-critical-job
SLO in `docs/ops/observability-slo-checklist.md`). The Max lane runs one CLI at a time with a 40-deep queue, so the
grading ceiling is the sidecar lane, and that is deliberately preserved (section 4.5).

## 2. What the harness simulates

`tests/load/fleet-1000.k6.js` runs **one learner per k6 iteration** and gives each a distinct account, device id,
token and refresh cycle. A learner's whole stay is one long iteration: sign in at a scheduled moment, a landing
page burst, then think time (spent **polling the notification hub**, as an idle browser tab does) interleaved with
one action per tick, plus an exam, speaking or room activity at a random moment.

| Role (global learner `g`, `g % 20`) | Share | Behaviour |
| --- | --- | --- |
| learner (17 of 20) | 85 % (850 of 1,000) | browse mix: dashboard, entitlement, study plan, readiness, engagement, notifications, search, progress, subscription. Personas by `g % 20`: reader (4), listener (3), writer (4), browser (6) |
| speaker (2 of 20) | 10 % (100) | back-to-back AI speaking sessions: create, consent, warm-up, finish warm-up (the admission line, see below), start role-play, preflight, provider offer / token, a dozen turns (every third with a small WAV), transcript, end, and an AI assessment on the first session and every fifth after it |
| room (1 of 20) | 5 % (50) | tutor rooms: consents, a `live_tutor` session, room create, LiveKit token, SignalR `JoinRoom`, hold, leave, end. With a rooms manifest an **expert** joins the same room and raises cues |

Persona flows:

- **Reader**: starts an Exam-mode attempt, saves Part A answers at a realistic cadence (one every 12 to 30 s, 10 %
  re-answered), reads the attempt back and compares every acknowledged answer with what is stored, then submits
  twice with the same `Idempotency-Key` and requires identical results.
- **Writer**: opens a scenario twice (the open is idempotent), autosaves the draft with optimistic concurrency,
  reads it back and compares, submits twice with one idempotency key (must be the same submission), and checks
  the credit pool moved by at most one letter's cost.
- **Listener**: starts an attempt and autosaves answers.

Admission line (live AI Speaking cap; owner decision 5 Oct 2026, `SpeakingLiveAdmissionService`): when the cap is
full, `finish-warmup` answers **HTTP 200** with the session still in `warmup` and an `admission` block
(`status: waiting`, position, `pollAfterSeconds`); nothing is held or timed while waiting, and the page repeats
the call. The harness does the same: it waits `pollAfterSeconds` (a 429 / 503 / 202 with `Retry-After` is waited out
the same way), counts the learner in `oet_live_queued_sessions`, records the wait in `oet_live_queue_wait_ms`,
checks once per wait that no credit moved for the session and the session never left `warmup`
(`oet_credit_consumed_while_queued`, `oet_timer_started_while_queued`), and after `K6_SPEAKING_MAX_WAIT_S` gives up
through `POST /v1/speaking/sessions/{id}/leave-queue`. The gate is on by default with a cap of 100 concurrent live
AI sessions (`Speaking:LiveAdmission`), so the 100 speakers of the steady profile normally never queue; the
overload surge does, and so does a lowered cap on the stack under test (`PUT /v1/admin/ai/live-voice/admission`
with `{"maxConcurrent": 50}`; `docs/speaking/live-voice.md` "Admission control"). Never switch the gate off to make
a run pass.

Transport fidelity: learner traffic goes through the **web origin's `/api/backend` proxy** (the Next.js BFF) with
the `Origin` and CSRF headers a browser sends; hubs use **long-polling**, because that is what production
browsers do (the BFF cannot upgrade WebSockets; `lib/env.ts` forces LongPolling for `/api/backend`). The BFF
exempts only the auth bootstrap calls and the hubs in `SIGNALR_HUB_PATH_PATTERN` (notifications, conversations,
ai-assistant, owner-agent and the tutor-room hub `/v1/speaking/live-rooms/hub`) from its double-submit CSRF
check, so those requests carry no CSRF header. `tests/load/fleet/contract.js` (`CSRF_EXEMPT_HUBS`) mirrors that
list for the hubs the harness uses, and `tests/load/fleet/contract.test.mjs` reads the pattern from
`lib/backend-proxy.ts`: when you change the harness or the proxy, run it by hand to see whether the two have
drifted (no CI does). Native clients that use WebSockets directly against the API are not modelled.

Security posture is **not** relaxed. `SingleActiveSessionEnabled` and `TrustedDeviceRequired` stay on; each
account's deterministic device id is auto-trusted on its first sign-in. Sign-ins are paced to 60 per minute per
generator leg to stay under the `AuthBruteforce` limit of 100 per minute per IP.

## 3. Files

| Path | Role |
| --- | --- |
| `tests/load/fleet-1000.k6.js` (deleted 2026-10-08 with all test code) | the scenario: setup (content discovery), `learners` and `experts` scenarios, thresholds, `handleSummary` |
| `tests/load/fleet/*.mjs` | pure, node-tested modules: `profiles` (timeline and leg maths), `thresholds`, `classify`, `signalr-frames`, `accounts` (incl. the production-host guard), `extract`, `summary-model`, `cadence` (effective think time under hub polling) |
| `tests/load/fleet/*.js` | k6-side modules: `config` (env), `contract` (every endpoint in one place), `http` (sessions, auth, headers, metrics), `signalr` (long-poll client), `flows`, `metrics` |
| `tests/load/seed/` | `seed-accounts.mjs` (create, `--purge`), `audit-ledger.mjs` (duplicate-charge audit) |
| `tests/load/simulators/` | `provider-sim.mjs` (OpenAI GPT-Live, Gemini Live, LiveKit Twirp and signed webhooks), `cli-stubs/` (stub `claude` and `codex` CLIs so the **real** writing sidecars run) |
| `tests/load/sut/docker-compose.load-overrides.yml` | overlay that adds the simulators to the staging stack and points the API at them |
| `tests/load/report/k6-load-report.mjs` | merges the legs into the markdown report and the verdict |
| `tests/load/pdf-bench/` | `pdf-extract-bench.mjs` and the `PdfExtractBench` kernel: a manual PDF extraction benchmark and parity oracle, not a load test (section 9) |
| `tests/load/*.k6.js` (the older scripts) | smoke-scale checks: `critical-paths` (up to 100 VUs on **one** shared session), `speaking-session-create`, `speaking-livekit-token` |
| `tests/load/**/*.test.mjs` | plain `node:test` files beside the modules; manual only, nothing runs them |

There is no workflow in the list because none exists: nothing in `.github/` runs any of this.

`tests/load/fleet/contract.js` is the single place that names paths, bodies' fields and expected statuses. If the
API renames a route, change it there.

## 4. The system under test

### 4.1 Rules

1. **Never the primary VPS, never production.** The primary is a small shared host (about 6 CPU, about 11 GiB,
   60+ co-tenant containers). A 1,000-learner run against it would be an outage with a report attached. The
   harness refuses production hosts in three places (`config.js` for the k6 run, the seed script, the audit).
2. A **dedicated staging host**, sized like the primary (at least 6 vCPU and 12 GiB, the same disk class), so the
   numbers transfer. The test is only meaningful if the host is not larger than production.
3. **Pull-only.** The staging host runs the release artifacts under test, `ghcr.io/jerryboganda/oetwebapp-api:<sha>`
   and `...-web:<sha>`, built by `Build images`. It never compiles anything (the overlay sets `image:` and the stack
   is started with `--no-build`). Coding agents do not operate this host and never run the harness: the owner does.
4. A real **TLS** endpoint for the web and API hostnames (Nginx Proxy Manager or nginx with a real certificate).
   Auth cookies are `Secure`, and the BFF checks the request origin, so plain HTTP does not behave like production.
5. A **private** data set: nothing from production is copied in.

### 4.2 Provisioning (tier A: what this change supports)

`docker-compose.staging.yml` (API, web, PostgreSQL 17 with `pg_stat_statements`, ClamAV) plus
`tests/load/sut/docker-compose.load-overrides.yml` (simulators, image pins, API environment). On the staging host,
from the repository root:

```bash
cp .env.staging.example .env.staging          # fill in the staging secrets: never production values
export API_IMAGE=ghcr.io/jerryboganda/oetwebapp-api:<sha> WEB_IMAGE=ghcr.io/jerryboganda/oetwebapp-web:<sha>
docker compose --env-file .env.staging \
  -f docker-compose.staging.yml -f tests/load/sut/docker-compose.load-overrides.yml up -d --no-build   # tests/load/ was deleted 2026-10-08 with all test code; this overlay no longer exists
```

Required settings (the overlay supplies the simulator ones; the rest is `.env.staging`):

| Setting | Value | Why |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Staging` (staging compose) | production rate limits (sign-in 100 / min / IP); Development would raise them |
| `AI__ProviderId` | `mock` | every AI call that is not Writing or live voice is answered locally |
| `LiveVoice__OpenAiBaseUrl`, `...ModelsBaseUrl`, `LiveVoice__Gemini*` | simulator URLs (overlay) | the API calls these for session creation and the model probe |
| `LiveKit__WssUrl`, `ApiKey`, `ApiSecret`, `WebhookSigningSecret`, `Egress*` | simulator (overlay) | room create and delete, egress, and signed webhooks |
| `OET_INTERNAL_AI_HOSTS` | `oet-writing-claude,oet-writing-codex` (overlay) | lets the seeded Writing sidecar providers pass the SSRF guard |
| `APP_URL`, `CORS_ALLOWED_ORIGINS` | the web origin | the BFF trusts exactly this origin for mutating requests |
| `Proxy__TrustForwardHeaders` | `true` | per-IP limiters see the real generator IP |

Not provided here (tier B): a production-shaped topology with the blue/green router and the `ai-worker` container.
The staging compose runs one API process that also runs the job processor, so the **blue/green cutover under open
hubs** and **ai-worker kill** experiments of section 8 need tier B: the production compose on a dedicated host
with a staging env file. That is an owner decision (host, secrets, domains); start with tier A.

### 4.3 Content and configuration prerequisites

The harness never invents content. The flows that need content **skip and are reported as coverage gaps** when it
is missing, and in the full profiles a required flow with no completion fails the run.

- **Profession.** Accounts are created for `--profession` (default `medicine`). Content is profession-locked, so
  the stack needs published Medicine content.
- **Reading**: at least one published paper the learner may open, with Part A questions.
- **Listening**: at least one published paper that can start an attempt (sound-check gate satisfied).
- **Writing**: at least one published scenario for the profession.
- **AI speaking**: at least one published role-play card that is live-voice ready (an owner-approved interlocutor
  script; otherwise `live_voice_content_not_ready`), and the live-voice flags on.
- **Tutor rooms**: nothing beyond a card; rooms are created by the learner. Pairing an expert needs a
  pre-provisioned room, see section 6.4.
- An **admin** account for seeding (`OET_LOAD_ADMIN_EMAIL`, `OET_LOAD_ADMIN_PASSWORD`) and the shared learner
  password (`OET_LOAD_PASSWORD`, at least 8 characters). Keep all three in the shell environment of the generator
  host: never commit them and never store them as repository secrets (no workflow runs the harness).

### 4.4 What must stay on

Do not weaken `SingleActiveSessionEnabled`, `TrustedDeviceRequired`, the rate limiters, or the AI kill switches
to make a run pass. A result obtained that way describes a stack nobody runs. If risk-based sign-in step-up
(`RiskMode`) challenges the generator's addresses, that is a finding to report, not a setting to turn off here.

### 4.5 The simulators

`provider-sim.mjs` (one process, one port, zero dependencies) answers the exact shapes the API parses:

- OpenAI GPT-Live: `POST /openai/v1/live/sessions` (SDP answer and session id), `.../hangup`, models probe.
- Gemini Live: `POST /gemini/v1beta/auth_tokens`, models probe.
- LiveKit: Twirp `CreateRoom`, `DeleteRoom`, `StartEgress`, `StopEgress`, plus **signed webhooks** back to
  `/v1/speaking/live-rooms/webhooks/livekit` (`room_started`, two `participant_joined`, `egress_ended`,
  `room_finished`; the HS256 JWT carries the body's SHA-256, exactly what `VerifyWebhookSignature` checks).
- Streamed completions (`stream: true`) in OpenAI and Anthropic shapes, with a configurable chunk count and delay.
- Control: `GET /sim/stats`, `POST /sim/config` (latency, jitter, failure rate and status at run time),
  `POST /sim/emit`.

Latency defaults to 150 ms with 50 ms of jitter, a deliberately modest stand-in; the report records the text you
pass as `K6_SIMULATORS_NOTE`. Real providers are slower and have limits the simulator does not.

The two **writing sidecars** are not re-implemented: `writing-claude-sim` and `writing-codex-sim` run the real
`writing-ai-sidecars/*/server.mjs` with a stub `claude` or `codex` CLI that sleeps (`LOAD_LLM_LATENCY_MS`, 20 s
by default) and prints the CLI's JSON. The lane (`WRITING_LANE_CONCURRENCY=1`, `WRITING_QUEUE_MAX=40`,
`WRITING_QUEUE_WAIT_MS=420000`) and its `503 lane_busy` behaviour are the real ones, so the actual grading
bottleneck is what the run exercises. The stub's reply is **not** a faithful Writing grade (set
`SIM_LLM_RESPONSE_FILE` to replay a captured real one), so grade-parse outcomes are not what a load run measures.

## 5. Accounts

`tests/load/seed/seed-accounts.mjs` creates `1 probe + N learners + M experts` through the admin API
(`POST /v1/admin/users`, password set, `sendInvite:false`) and grants each learner and the probe credit pools
(400 shared, 100 Writing, 200 Speaking, 100 Listening and 100 Reading tests, 10 mocks). It is idempotent. Names
are deterministic (`loadtest-learner-0000@load.oet.test`, `dev-loadtest-learner-0000` as the device id), which is
why no accounts file exists: every k6 leg derives its own slice.

The seeded accounts hold credits but **no subscription**, so `GET /v1/subscriptions/me` answers 404 for every one
of them; the harness treats that as the documented answer (`contract.js`), not as a failure. The k6 legs never log
in as the probe: every leg runs `setup()` at about the same time, and a fresh sign-in revokes the account's other
sessions (`SingleActiveSessionEnabled` / `TrustedDeviceRequired` stay on), so a shared login would 401 the other
legs mid-discovery. Each leg discovers content as its **own first learner** (global learner `g` = its leg index).
`setup()` throws, naming the last status, when not one discovery request succeeded.

```bash
OET_LOAD_ADMIN_EMAIL=... OET_LOAD_ADMIN_PASSWORD=... OET_LOAD_PASSWORD=... \
  node tests/load/seed/seed-accounts.mjs --api https://api.staging.example --learners 1500 --experts 75
```

Seed 1,500 learners to support both the 1,000-learner and the 1,500-learner profiles. `--purge` deletes them;
`audit-ledger.mjs` reads every learner's credit snapshot after a run and fails on any `(reason, referenceId)`
charged twice, any negative balance, and any account it cannot read **or cannot find** (an account that was not
audited is not a clean one). Admin user listing loads the whole user table server-side, so purge pages
once by prefix rather than looking users up one by one.

## 6. Running

### 6.1 Smoke first, always

On the load-generator host, seed the accounts (section 5), then run one smoke leg:

```bash
export K6_API_URL=https://api.staging.example K6_WEB_URL=https://app.staging.example \
       K6_PROFILE=smoke OET_LOAD_PASSWORD=... K6_SUMMARY_PATH=leg0.json K6_VERSION_STRING="$(k6 version)"
k6 run tests/load/fleet-1000.k6.js   # deleted 2026-10-08 with all test code; no harness to run
node tests/load/report/k6-load-report.mjs --input leg0.json --out report.md --json verdict.json --allow-no-data
```

It runs 20 learners for a few minutes with think times at 15 %, and **publishes the endpoint status matrix** in the
report: for every endpoint the harness can call, how many times it answered each status. That is the contract
probe. A status the endpoint is not documented to return means `tests/load/fleet/contract.js` and the API
disagree; fix that before trusting a latency number. The harness was written from the backend source and has not
been executed against a live stack by its author: expect the first smoke to find at least a path or body-shape
mismatch.

### 6.2 The profiles

| Profile | Shape | Use |
| --- | --- | --- |
| `smoke` | 20 learners, a short hold | prove the harness against the stack, read the status matrix |
| `capacity` | stepped 100, 300, 600, 1,000 learners, 10 minutes held per stage | find where the targets break; the report lists the highest passing stage |
| `steady` | ramp (about 4 minutes per 250 learners per leg), 2 minute settle, **60 minutes at 1,000**, 3 minute cool-down | the acceptance run |
| `overload` | 1,000 for 10 minutes, then +500 learners (1,500), held 20 minutes, 2 minute subside, 10 minutes of recovery | queue-not-collapse and recovery |

Thresholds apply to the **steady** phase (and **overload** / **recovery** for the overload profile); ramps and
warm-up are not judged. Everything is set by environment variable (section 6.4): the profile, the leg
split, the learner count and hold times, `K6_HUB_MODE`, `K6_THINK_SCALE`, `K6_ROOMS_FILE`, `K6_SIMULATORS_NOTE`.

### 6.3 Why legs, and the self-provisioned generator

One generator machine cannot drive 1,000 learners: each learner is a long-lived VU holding a long-poll
connection and issuing requests, and k6 saturates its own CPU before the system under test does, which would put
the generator's weakness into your latency numbers. So the run is split into **N legs**, one k6 process each on its
own generator: leg `L` owns the global learners `g = i * N + L`, so legs are interleaved, ramp in parallel,
never share an account, and each has its own source IP (the per-IP sign-in limit applies per leg). As a
conservative planning figure give a leg **at most about 300 learners** (a 4 vCPU / 16 GB machine; watch the
generator's CPU while it runs and treat a saturated generator as a distorted result): 1,000 learners need 4
legs, the 1,500-learner overload needs 5 or 6 (or bigger generators). The harness does not enforce the figure.
The leg reports are merged by `k6-load-report.mjs`; latency percentiles cannot be merged exactly, so the report
prints the **worst leg** (the conservative bound) and pools the rates.

Use a **self-provisioned load generator**. This is an owner or operator procedure on dedicated hosts; coding
agents never run it (AGENTS.md compute policy), no workflow or CI runner drives it, and a self-hosted runner is
never registered on the public repository for it.

1. Provision one or more VMs **outside the primary** with the same network path to the staging host as real
   users, at least 8 vCPU and 16 GB each for 1,000 learners (16 vCPU for 1,500). Raise the open-file and
   ephemeral-port limits (`ulimit -n 65535`).
2. Install k6 and Node 22 and check out the branch.
3. On each VM run its leg of the same script. Four legs serve the default 1,000 learners, so `K6_LEG_INDEX`
   runs 0 to 3, one value per generator (5 or 6 legs for the 1,500-learner overload):

```bash
export K6_API_URL=https://api.staging.example K6_WEB_URL=https://app.staging.example \
       K6_PROFILE=steady K6_LEG_COUNT=4 K6_LEG_INDEX=0 OET_LOAD_PASSWORD=... \
       K6_SUMMARY_PATH=leg0.json K6_VERSION_STRING="$(k6 version)"
k6 run tests/load/fleet-1000.k6.js        # exit 99 = a threshold failed
```

4. Copy the `legN.json` files together and merge: `node tests/load/report/k6-load-report.mjs --input leg0.json --input leg1.json --out report.md --json verdict.json --require-pass`.
5. Audit the credit ledger for duplicate charges, and purge the accounts when you are finished (section 5):
   `node tests/load/seed/audit-ledger.mjs --api https://api.staging.example --learners 1500 --experts 75 --report ledger-audit.json`
   (admin credentials in the environment).

### 6.4 Environment reference

| Variable | Default | Meaning |
| --- | --- | --- |
| `K6_API_URL` (required), `K6_WEB_URL` | | non-production origins; with `K6_WEB_URL` learner traffic goes through `/api/backend` |
| `K6_PROFILE` | `smoke` | `smoke`, `capacity`, `steady`, `overload` |
| `K6_LEG_COUNT`, `K6_LEG_INDEX` | 1, 0 | this process's leg |
| `K6_LEARNERS` | profile default | base learners (global, across legs) |
| `K6_SIGNIN_PER_MIN` | 60 | sign-ins per minute **per leg** (the IP limit is 100) |
| `K6_STEADY_MINUTES`, `K6_STAGE_HOLD_MINUTES`, `K6_SURGE_*`, `K6_RECOVERY_MINUTES` | 60, 10, 500 / 20, 10 | phase lengths |
| `K6_THINK_SCALE` | 1 (0.15 for `smoke`) | multiplies every think time |
| `K6_HUB_MODE` | `longpoll` | `longpoll` or `off` |
| `K6_SPEAKING_TURNS`, `K6_SPEAKING_ASSESS_EVERY`, `K6_SPEAKING_MAX_WAIT_S`, `K6_ROOM_SECONDS` | 12, 5, 600, 300 | live-session shape |
| `K6_READING_SAVES`, `K6_WRITING_SAVES`, `K6_LISTENING_SAVES` | 20, 8, 10 | exam autosaves per attempt |
| `K6_ROOMS_FILE` | | **absolute** path of a JSON array of `{ "ordinal": r, "liveRoomId": "lvrm_..." }`: pre-provisioned rooms whose session already has an expert (`InterlocutorActorId`). Room `r` is paired with learner `g = 20 r + 19` and expert `loadtest-expert-r`. Without it every room is created by its learner and runs without a tutor side; the report's cue count stays 0. |
| `K6_REQUIRED_FLOWS` | `browse` (smoke), `browse,reading,writing,speaking,room` | flows that must complete at least once on a leg that runs them |
| `K6_STATUS_MATRIX` | 1 for smoke | publish the per-endpoint status matrix |
| `OET_LOAD_PASSWORD` | | shared password of the disposable accounts |
| `OET_LOAD_ACCOUNT_PREFIX`, `OET_LOAD_EMAIL_DOMAIN` | `loadtest`, `load.oet.test` | account naming |
| `K6_SUMMARY_PATH`, `K6_VERSION_STRING` | `k6-summary.json`, | where this leg writes its summary; the k6 version recorded in the report |
| `K6_RUN_ID`, `K6_SHA`, `K6_TARGET_LABEL`, `K6_SIMULATORS_NOTE` | `local`, , , | free text the report prints: a run label, the commit under test, a short name of the stack, the simulator latency and fault settings in force |

Pre-provisioning rooms with an assigned expert cannot be done through the public API: a tutor is attached only by
the private-speaking booking flow (entitlement, lead time, availability, payment), so the `K6_ROOMS_FILE` manifest
is supplied by whoever provisions the staging database. The default is learner-created rooms.

## 7. Reading the report

The report starts with the verdict and the scope statement, then the owner-target table, the k6 thresholds that
failed, flow coverage, capacity stages, overload behaviour and the endpoint status matrix.

- **PASS**: every owner target had samples and met its limit, every threshold passed on every leg.
- **FAIL**: a target or a threshold failed. `k6` exits 99 for the leg, and the report exits 1 with `--require-pass`.
- **INCOMPLETE**: nothing failed but a target had **no samples** (a flow was skipped for lack of content). A target
  that was not exercised is not a pass; `--require-pass` fails it (smoke runs it with `--allow-no-data`).

**Unexpected failure** is exact: a documented business refusal (HTTP 4xx with a known code, such as the Reading
Part B/C window not being open) is not a failure; a graceful shed (429 or 503 with `Retry-After`) is not a failure
while overloading or while an admission queue is allowed; everything else is, including any 429 in steady state, a
5xx, a timeout or a connection error. A 401 is retried once after a token refresh and only the retry counts.

**Coverage gaps** (`oet_flow_skipped`) name what could not run and why (`no_content`, `start_refused`,
`create_refused`, `no_live_voice_card`, ...). They are printed in the leg log.

## 8. Fault experiments

Run these after a passing steady run, one at a time, and read them as experiments, not as gated runs (a breach
during the injected window is expected):

| Experiment | How | What good looks like |
| --- | --- | --- |
| Provider latency spike | `POST /sim/config {"latencyMs":3000}` on the simulator for ten minutes | live-setup p95 rises; provider timeouts become `503` and the browser fails over; nothing else degrades |
| Provider outage | `{"failRate":1}` | speaking creation returns the documented unavailable error, no 500s elsewhere; recovers when cleared |
| Writing lane pressure | raise `LOAD_LLM_LATENCY_MS`, or `LOAD_LLM_FAIL_RATE=1` | submits stay fast (grading is off the request path); lane `503` fails over to the next provider; credits are refunded on failure; the Max provider is never skipped |
| ai-worker kill, blue/green cutover under open hubs, DB pool starvation | tier B only (section 4.2) | leases expire and are reclaimed; hubs reconnect inside the `HubConnect` limit (30 / min / user); no 53300 |

## 9. PDF extraction benchmark and the Rust gate

`tests/load/pdf-bench/pdf-extract-bench.mjs` (a manual tool, labelled **benchmark evidence, not a release proof**;
no workflow runs it) measures PDF text extraction over every PDF tracked in the repository (it lists them with
`git ls-files`): per document wall time, CPU time and peak RSS, p50 and p95, and the in-process steady-state cost the
primary pays today. The owner builds the kernel and runs it by hand:

```bash
dotnet build tests/load/pdf-bench/PdfExtractBench/PdfExtractBench.csproj -c Release -o bench-out
node tests/load/pdf-bench/pdf-extract-bench.mjs --bench-dll bench-out/PdfExtractBench.dll --steady
```

The **in-process `PdfPigPdfTextExtractor`, link-compiled verbatim (with `PdfExtractionFacts.cs`, which it reports
into), is the parity oracle**. An optional **agent kernel** contender is compared against it byte for byte
(page count, characters, every page hash, text hash). Contenders are commands with a `{pdf}` placeholder that
print one JSON line of hashes and counts (never text). The **Rust gate** is explicit: exact parity on every
document AND (p95 at least 30 % lower OR CPU per job at least 40 % lower than the .NET baseline it replaces).
Owner decision D6: Rust is **not built**; with no candidate the report says so and records the baseline a candidate
would have to beat. The corpus has no Listening question paper, so Part B/C parser parity is not covered by it.

## 10. Costs to estimate before the first long run

No prices are asserted here; fill them from the actual quotes.

- the dedicated staging host (monthly) and its TLS / DNS;
- generator time: the self-provisioned VMs, legs x about 75 minutes per run;
- accounts and storage: 1,576 disposable accounts and the rows they create (purge afterwards);
- provider spend: **none** with the simulators. Validating real GPT-Live / Gemini Live / LiveKit capacity is a
  separate, paid, owner-run exercise with its own limits.

## 11. Known limits of this harness

- It was written from source and **not run** by its author; the smoke profile is the first real test.
- Tier A has no blue/green router and no `ai-worker`.
- Listening covers start and autosave only (no submit); its question ids are read from a payload whose shape is
  discovered at run time.
- Native-client WebSocket hubs are not modelled.
- Grading content is not faithful (see 4.5); the Writing lane's real concurrency is.
- **Hub polling slows the action cadence.** While a hub connection is open, think time is spent long-polling it,
  and a poll returns only when the server has a frame, normally its 15 s keep-alive. A pause of `T` seconds
  therefore lasts `15 * ceil(T / 15)` seconds: nothing shorter than one poll is reachable on a single
  connection, and the request rate the run drives is **below** the nominal think-time mix. Mean pauses for the
  default mix (the report prints the same table, computed for the run's `K6_THINK_SCALE`, in its method section):

  | activity | nominal mean pause | effective mean pause |
  | --- | --- | --- |
  | browse tick (8 to 25 s) | 16.5 s | about 23.8 s |
  | Reading answer save (12 to 30 s) | 21 s | 27.5 s |
  | Writing draft save (15 to 35 s) | 25 s | about 33.8 s |
  | speaking turn (4 to 9 s) | 6.5 s | 15 s |
  | speaking warm-up and role-play pauses (2 to 6 s, 1 to 3 s) | 4 s, 2 s | 15 s |

  A smoke run (`K6_THINK_SCALE` 0.15) is dominated by the 15 s floor. Read the achieved rates from the report's class
  counts, not from the nominal mix. The harness does not run the actions on a second, poll-free path (that would
  change what one browser tab does); sizing a run from the achieved rates, or adding learners to reach a target
  request rate, is the operator's call.
- **The CSRF exemption list is a copy.** The tutor-room hub used to be missing from the web proxy's
  `SIGNALR_HUB_PATH_PATTERN` (`lib/backend-proxy.ts`), so a browser's room-hub negotiate would have been refused
  with 403; the BFF change that exempts it is in this stack, and `CSRF_EXEMPT_HUBS` in `contract.js` now follows
  it. If the pattern changes again, `contract.js` must follow (the setup log warns `harness drift` for a hub the
  harness uses but does not list), or the numbers for that hub stop describing what a browser sees.
- **Per-process caches.** The API keeps a short per-process user-state cache (JWT liveness, entitlement and freeze
  reads, about 15 s). A session revoked by a fresh sign-in can keep working for that long on a process that cached
  it, and an entitlement change can lag by the same amount. The harness's lost-save and idempotency checks read
  through the same API and are unaffected, but read the timing of a 401 with this in mind.
