# TypeSafe SystemOne / Jev - Environment Reference

Every `TypeSafe:*` setting, its production env name, default and meaning. Code:
`backend/src/OetLearner.Api/Configuration/TypeSafeOptions.cs`. Call sites:
`backend/src/OetLearner.Api/Services/Ai/TypeSafe/`. Policy and feature codes:
`docs/AI-USAGE-POLICY.md` section 5. Calibration kit: `tools/typesafe/README.md`.

## Purpose

Jev is a typed judgment layer. It returns `Choice` / `Noul` / `Score` answers with a
confidence; it never writes prose or code and never grades. Code owns every weight,
threshold and pass/fail decision. It never overrides the rulebook gateway verdicts,
`WritingRuleEngine`, the GEPA placement engine, `OetScoring` or `lib/scoring.ts`.
It sits beside Claude Max and Codex, not in place of them: Claude Max stays the
always-on primary for Writing and Speaking grading, and nothing in this file changes
that.

Every caller is fail-soft. A disabled, unconfigured, rate-limited or failing Jev means
"carry on without the judgment", never a learner-visible error.

Everything is **off by default** in the class, in the committed `appsettings.json`, and
in `docker-compose.production.yml`. Production opts in per surface, in the order below.

## Where the key lives (hard rules)

The TypeSafe API key is never written to a tracked file: not `appsettings*.json`, not a
compose default, not a workflow, a doc, a test, a log line or a client bundle. Minimum
length is 16 characters. Provision it in one of these homes only:

| Home | How | Notes |
|---|---|---|
| Admin provider row | `/admin/ai-providers`, row code `typesafe-jev` | Stored Data-Protection encrypted; the UI shows only the last 4 characters. At call time the registry key wins over the env key. |
| VPS env | `TYPESAFE__APIKEY` in `.env.production`, edited through `oet-env-edit` | The value is never echoed, logged or committed. |
| GitHub Actions secret | `TYPESAFE_API_KEY` (owner runs `gh secret set TYPESAFE_API_KEY`) | Used by `jev-calibrate.yml`, `listening-content-verify.yml` and `writing-rev8-ci.yml`. |

Two things to know before provisioning:

- `TYPESAFE__ENABLED=true` is required even when the key lives only in the admin row.
  The master switch is checked before the registry key is looked up.
- `scripts/deploy/validate-production-env.sh` requires `TYPESAFE__APIKEY` to be at least
  16 characters in `.env.production` whenever `TYPESAFE__ENABLED=true`. It fails the
  deploy otherwise.

A key that has ever appeared in chat, a ticket or a log is burned: rotate it, then
provision the new value in the homes above.

## Variables

Compose forwards each variable to the API slots (`oet-api-blue`, `oet-api-green`) and to
`oet-ai-worker`, which share one `&api-env` block with no `env_file`. The names below
are the `.env.production` names; compose maps `TYPESAFE__X` to `TypeSafe__X`. Options
are bound at startup, so a change takes effect on the next API and worker container
start (a normal Build & Deploy), not live.

### Core

| Variable | Default | Meaning |
|---|---|---|
| `TYPESAFE__ENABLED` | `false` | Master kill-switch. Every per-surface flag below is a no-op until this is `true`. |
| `TYPESAFE__APIKEY` | empty | Bearer key for the TypeSafe API. See "Where the key lives". |
| `TYPESAFE__MODEL` | `jev-1.13.0` | Pinned model id. See "Pinned model". |
| `TYPESAFE__BASEURL` | `https://api.typesafe.ai` | API base URL. Override only for tests. |
| `TYPESAFE__TIMEOUTSECONDS` | `10` | Per-attempt HTTP timeout. Integer >= 1. Judgments are latency-sensitive; a slow answer is worthless. |
| `TYPESAFE__MAXRETRIES` | `2` | Extra attempts after the first, on 429/529 only (exponential backoff). Integer >= 0 (0 = no retries). 401/422 are configuration bugs and are never retried. |
| `TYPESAFE__COSTPERINPUTTOKENUSD` | `0.000000042` | USD per input token ($42 per billion; output tokens are free). Meters the budget hold and the `AiUsageRecord` cost. Write it as a plain decimal. |

### Per-surface flags (all default `false`)

| Variable | Surface | Behaviour when on |
|---|---|---|
| `TYPESAFE__WRITINGGUARDENABLED` | Writing pre-gateway guard (feature `jev.writing.guard`) | Parallel Nouls (injection, rule-evasion, abuse, gibberish). Negative gate only: it may block or flag for tutor review, never auto-pass. |
| `TYPESAFE__WRITINGROUTEENABLED` | Writing request routing on `/v1/ai/complete` (`jev.writing.route`) | When the router confidence reaches `ROUTECONFIDENCETHRESHOLD`, the grounded prompt task and feature code realign to the routed target; otherwise the caller's request stands. |
| `TYPESAFE__WRITINGVERIFYENABLED` | Writing post-grade citation check (`jev.writing.verify`) | One Choice per AI finding (supported / contradicted / not-in-evidence). Contradicted or low-confidence findings flag the grade for tutor review (`WritingGrade.ConfidenceFlag`). |
| `TYPESAFE__WRITINGCRITERIAENABLED` | Writing advisory criteria (`jev.writing.criteria`) | Per-criterion Scores merged into the feedback JSON as `jevAdvisory`. Display-only, never a grade input. |
| `TYPESAFE__COMPANIONRERANKENABLED` | Companion retrieval rerank (`jev.companion.rerank`) | One Score per hybrid-search candidate, after the entitlement prefilter. Ordering only; an outage keeps the hybrid order. |
| `TYPESAFE__CONVERSATIONADVISORYENABLED` | AI-patient turn advisory (`jev.conversation.turn`) | Stays-in-role / clinically-appropriate / unsafe Nouls next to reply generation. Informational only; never gates, scores or ends a session. |
| `TYPESAFE__RESPONSEVERIFYENABLED` | Gateway response review (`jev.response.verify`) | Advisory review of a gateway completion (evidence relation, addresses-task, unsafe-recommendation). Never changes a grade or the gateway verdict. |
| `TYPESAFE__DEVELOPMENTTRIAGEENABLED` | Development/review tooling triage (`jev.development.triage`) | Typed triage for the owner agent console. Never on a learner path. |

### Thresholds (code-owned decisions; the model only supplies probabilities)

All six are probabilities in the range 0 to 1 inclusive.

| Variable | Default | Meaning |
|---|---|---|
| `TYPESAFE__GUARDBLOCKTHRESHOLD` | `0.80` | Any single guard Noul at or above this blocks the submission from the paid grade and flags it to a human. |
| `TYPESAFE__GUARDREVIEWTHRESHOLD` | `0.50` | At or above this (below the block threshold) the submission proceeds and is logged for calibration. |
| `TYPESAFE__ROUTECONFIDENCETHRESHOLD` | `0.70` | Router confidence required before a routed target may override the caller's explicit request. |
| `TYPESAFE__VERIFYCONFIDENCETHRESHOLD` | `0.60` | A "supported" verdict below this counts as unproven and flags the grade, like a contradicted verdict. |
| `TYPESAFE__RESPONSECONFIDENCETHRESHOLD` | `0.80` | Confidence required by response verify. |
| `TYPESAFE__DEVELOPMENTCONFIDENCETHRESHOLD` | `0.80` | Confidence required by development triage. |

### Limits and circuit breaker

| Variable | Default | Meaning |
|---|---|---|
| `TYPESAFE__VERIFYMAXFINDINGSPERCALL` | `12` | Findings verified in one fan-out call (integer >= 1). Findings beyond the cap are left unverified, not verified blind. |
| `TYPESAFE__BREAKERFAILURETHRESHOLD` | `5` | Consecutive failed sends (after in-call retries) before sends short-circuit platform-wide (integer >= 1). Any success resets the streak. |
| `TYPESAFE__BREAKERCOOLDOWNSECONDS` | `30` | How long sends stay short-circuited after the breaker trips (integer >= 1). The breaker only saves the timeout wait; callers already fail soft. |

### What `validate-production-env.sh` checks

- `TYPESAFE__ENABLED=true` requires `TYPESAFE__APIKEY` of at least 16 characters (fails).
- Any per-surface flag set to `true` while `TYPESAFE__ENABLED` is not `true` prints a
  warning (does not fail): the flag does nothing until the master switch is on.
- When set, the six thresholds and `TYPESAFE__COSTPERINPUTTOKENUSD` must be numbers
  between 0 and 1 (fails).
- When set, `TYPESAFE__TIMEOUTSECONDS`, `VERIFYMAXFINDINGSPERCALL`,
  `BREAKERFAILURETHRESHOLD` and `BREAKERCOOLDOWNSECONDS` must be integers >= 1, and
  `TYPESAFE__MAXRETRIES` an integer >= 0 (fails).
- Unset keys are fine; compose supplies the defaults above.

## Pinned model

`TYPESAFE__MODEL` is pinned to `jev-1.13.0`. Never set it to `jev-latest`: every
threshold here is tuned against a fixed version and the alias can move under them
silently. Bump the pin deliberately, in a reviewed change, and only after a green
`jev-calibrate.yml` run against the new version. At startup (when enabled) the API logs
a warning if the pinned model is no longer listed for the account.

## Flag-flip order

Flip one surface at a time, in this order. Each flip needs a green `jev-calibrate.yml`
run (GitHub Actions, `workflow_dispatch`, masked `TYPESAFE_API_KEY`) first, and a watch
of `/admin/ai-usage` (provider `typesafe-jev`) plus the logs afterwards. Never run the
calibration kit on a workstation or the VPS; it runs on Actions only.

1. `TYPESAFE__ENABLED=true`, with the key provisioned and every surface flag still off.
   Confirm a clean startup and the model-pin log line.
2. `TYPESAFE__DEVELOPMENTTRIAGEENABLED` (owner console only, no learner impact).
3. `TYPESAFE__RESPONSEVERIFYENABLED`, scoped. Only after the change that limits response
   verify to features with a consumer is live; before that it runs on every gateway
   completion.
4. `TYPESAFE__WRITINGVERIFYENABLED` and `TYPESAFE__WRITINGCRITERIAENABLED` (shadow first:
   tutor flags and advisory fields only).
5. `TYPESAFE__CONVERSATIONADVISORYENABLED` (Speaking shadow). Only after the advisory
   runs off the turn's critical path.
6. Reading and Listening surfaces, using the per-surface flags those waves add.
7. `TYPESAFE__WRITINGGUARDENABLED`, last. It is the only surface that can stop a
   submission, so it needs the longest calibration record, including
   `injection-attempt.json` staying green.

`TYPESAFE__WRITINGROUTEENABLED` and `TYPESAFE__COMPANIONRERANKENABLED` are outside the
planned waves. Leave them off unless the owner asks, and calibrate them first.

To change a flag: edit `.env.production` through `oet-env-edit`, then run Build &
Deploy (`.github/workflows/deploy.yml`). The deploy runs `validate-production-env.sh`.

## Kill levers

Use the narrowest one that works.

1. A single surface: set its flag to `false` and redeploy.
2. All Jev: set `TYPESAFE__ENABLED=false` and redeploy. Every call returns "disabled"
   and callers carry on without the judgment.
3. Hot, no deploy: the admin per-feature kill list (`DisabledFeaturesCsv`, add the
   `jev.*` feature code) on `/admin/ai-usage` under Budget & Kill-switch. This is the
   fast per-surface disable.
4. Owner-only emergency: the global AI kill switch (`AiGlobalKillSwitch`, same page).
   It stops ALL platform-keyed AI, including Writing and Speaking grading, so it is not
   a Jev-specific lever.

Levers 3 and 4 stop Jev only once the direct-call recorder honours the global policy
(the Wave 1 governance change to `DirectAiCallRecorder`); confirm that change is
deployed before relying on them for Jev.

The circuit breaker is automatic protection, not a lever: after
`TYPESAFE__BREAKERFAILURETHRESHOLD` failed sends in a row it short-circuits sends for
`TYPESAFE__BREAKERCOOLDOWNSECONDS`.
