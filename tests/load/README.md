# Load tests (k6)

Everything here is **dispatch-only** (GitHub Actions `workflow_dispatch`) and aimed at a **non-production** stack.
Nothing runs on a push, a pull request or a schedule, and every entry point refuses a production host.
The runbook is `docs/ops/LOAD-TESTING.md`; read it before running anything.

| Script | Scale | What it is | Workflow |
| --- | --- | --- | --- |
| `fleet-1000.k6.js` | 1,000 to 1,500 distinct learners, 100 AI speaking sessions, 50 tutor rooms | the real scenario: per-learner auth and refresh, SignalR long-polling through the web origin, exam autosave and submission with lost-save and duplicate-charge checks, live AI speaking and rooms against simulators, warm-up, 60 minute steady state, overload surge and recovery | `load-fleet.yml` |
| `critical-paths.k6.js` | up to 100 VUs sharing **one** learner session | smoke-scale read-path check (bootstrap, dashboard, subscription, entitlement) | `performance.yml` |
| `speaking-session-create.k6.js` | `K6_VUS` (default 100; use 1 with the single seeded learner) | Speaking session creation | `speaking-load.yml` |
| `speaking-livekit-token.k6.js` | `K6_VUS` | LiveKit token mint for **one existing room** (`SPEAKING_LIVE_ROOM_ID`, required: it fails at start without one) | `speaking-load.yml` |

The platform allows one active session per account, so the smoke scripts cannot use many VUs on a shared account:
set `OET_LOAD_DISTINCT_VUS=1` against a stack seeded with `seed/seed-accounts.mjs`, or run one VU.

## Layout

```
fleet-1000.k6.js            entry: setup (content discovery), scenarios, thresholds, handleSummary
fleet/                      pure *.mjs (node-tested) and k6-side *.js modules; contract.js names every endpoint
seed/                       seed-accounts.mjs (create / --purge), audit-ledger.mjs (duplicate-charge audit)
simulators/                 provider-sim.mjs (OpenAI / Gemini / LiveKit) and cli-stubs/ for the real writing sidecars
sut/                        compose overlay that points the staging stack at the simulators
pdf-bench/                  PdfExtractBench (the API's PDF extractor link-compiled; benchmark + parity oracle)
workflow-guards.test.mjs    fails if a k6 gate is swallowed, a load workflow gains a schedule, or production leaks in
critical-paths.k6.test.mjs  source guards for the smoke scripts
```

## Tests

The node tests run in the preflight job of `load-fleet.yml` (and the smoke-script guards in `performance.yml`):

```
node --test 'tests/load/**/*.test.mjs' scripts/perf/k6-load-report.test.mjs scripts/perf/load-plan.test.mjs
```

## Quick start (smoke)

Dispatch `load-fleet.yml` with `profile=smoke`, `legs=1`, `seed_accounts=true`, your staging `api_url` / `web_url`,
and `confirm_non_production` ticked. The report includes the endpoint status matrix, the contract probe for this
harness.

## SLOs

See `docs/ops/LOAD-TESTING.md` section 1 for the owner's targets that gate the fleet run, and
`docs/load-testing/speaking-budgets.md` for the per-route Speaking budgets the smoke scripts use.
