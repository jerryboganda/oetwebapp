# Load tests (k6)

**Inert manual tools. Run manually by the owner from a self-provisioned load generator; no CI runs this; agents
never run it.** No workflow in this repository runs k6, a load test or a benchmark, on any trigger (including
`workflow_dispatch`): AGENTS.md "NO AUTOMATED QA ANYWHERE" bars it and the pipeline contract
(`scripts/deploy/verify-pipeline-contract.mjs`) fails the build if one appears. The tests beside the code
(`*.test.mjs`) are manual tools as well: nothing runs them and nothing here claims they passed.

Everything is aimed at a **non-production** stack and every entry point refuses a production host.
The runbook is `docs/ops/LOAD-TESTING.md`; read it before running anything.

| Script | Scale | What it is |
| --- | --- | --- |
| `fleet-1000.k6.js` | 1,000 to 1,500 distinct learners, 100 AI speaking sessions, 50 tutor rooms | the real scenario: per-learner auth and refresh, SignalR long-polling through the web origin, exam autosave and submission with lost-save and duplicate-charge checks, live AI speaking (including the admission line) and rooms against simulators, warm-up, 60 minute steady state, overload surge and recovery |
| `critical-paths.k6.js` | up to 100 VUs sharing **one** learner session | smoke-scale read-path check (bootstrap, dashboard, subscription, entitlement) |
| `speaking-session-create.k6.js` | `K6_VUS` (default 100; use 1 with the single seeded learner) | Speaking session creation |
| `speaking-livekit-token.k6.js` | `K6_VUS` | LiveKit token mint for **one existing room** (`SPEAKING_LIVE_ROOM_ID`, required: it fails at start without one) |

The platform allows one active session per account, so the smoke scripts cannot use many VUs on a shared account:
set `OET_LOAD_DISTINCT_VUS=1` against a stack seeded with `seed/seed-accounts.mjs`, or run one VU.

## Layout

```
fleet-1000.k6.js            entry: setup (content discovery), scenarios, thresholds, handleSummary
fleet/                      pure *.mjs (node-testable) and k6-side *.js modules; contract.js names every endpoint
seed/                       seed-accounts.mjs (create / --purge), audit-ledger.mjs (duplicate-charge audit)
simulators/                 provider-sim.mjs (OpenAI / Gemini / LiveKit) and cli-stubs/ for the real writing sidecars
sut/                        compose overlay that points the staging stack at the simulators
report/                     k6-load-report.mjs: merges the per-leg summaries into a markdown report and a verdict
pdf-bench/                  PdfExtractBench (the API's PDF extractor link-compiled) and pdf-extract-bench.mjs: a manual
                            benchmark and parity oracle, not a load test
critical-paths.k6.test.mjs  source guards for the smoke scripts
```

## Unit tests of the harness

The `*.test.mjs` files beside the modules are plain `node:test` files. They are not wired to any workflow. The owner
may run them by hand when changing the harness:

```
node --test tests/load/fleet/ tests/load/seed/ tests/load/simulators/ tests/load/report/ tests/load/pdf-bench/
```

## Quick start (smoke)

On the load-generator host, with the staging stack up and the accounts seeded, run `k6 run tests/load/fleet-1000.k6.js`
with `K6_PROFILE=smoke` (full commands: `docs/ops/LOAD-TESTING.md` section 6.1). The report includes the endpoint
status matrix, the contract probe for this harness.

## SLOs

See `docs/ops/LOAD-TESTING.md` section 1 for the owner's targets that gate the fleet run, and
`docs/load-testing/speaking-budgets.md` for the per-route Speaking budgets the smoke scripts use.
