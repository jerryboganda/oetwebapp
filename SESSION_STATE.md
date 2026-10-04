# SESSION STATE

Session: deployment-latency
Goal: Reduce main-push-to-healthy-serving deployment latency toward 300 seconds on existing free hosted runners, preserving release, migration, health and rollback contracts.
Mode: verify
Updated: 2026-10-04T01:43:22Z
Branch: main
HEAD: d6f7e0104

<!--
The current run's working memory. This is layer 2 of three:
  1. AGENTS.md / .github/instructions/**  permanent rules
  2. SESSION_STATE.md + TASKS.json         this run        <- you are here
  3. VERIFICATION.md / git / Actions runs  objective truth

Rules
- Header keys are required, and `Mode` must be plan | execute | verify | blocked | done.
- The seven H2 sections below are required and the order is load-bearing
  (`pnpm run ax:check` enforces it).
- Never tick a gate without evidence. A `PASS` row needs a run id, a workflow
  file, or `local:<command>`. Record real runs with `pnpm run ax:record`.
- Keep it short. It is working memory, not a history file.
- Two sessions writing this at once? Take the newer `Updated:` block wholesale —
  do not hand-merge. The durable, merge-safe ledger is PROGRESS.md.
-->

## Objective

Shorten the full main-push-to-healthy-public-serving path toward 300 seconds,
including queues, cold builds, migrations and required Writing checks.
Use existing free hosted runners only; report measured misses without bypassing
release provenance, protected storage, readiness or rollback.

## Acceptance criteria

- [ ] AC-1 Only changed runtime inputs compile; every reused component has verified immutable provenance.
- [ ] AC-2 API SQL comes from the same publish compilation; required Writing tests really execute.
- [ ] AC-3 Unchanged healthy services stay running; router cutover is durable and rolls back as a pair.
- [ ] AC-4 Actions regression/build/deploy evidence and exact serving-image proof are recorded.
- [ ] AC-5 Inclusive elapsed measurements disclose cold/Writing/queue misses and unmeasured categories.

## Decisions (do not revisit)

- D-1 Owner approved implementation on existing free GitHub-hosted runners; no paid or self-hosted capacity.
- D-2 Reuse successful ancestor manifests, never mutable latest; a built-but-unapplied API still needs SQL.
- D-3 Only successful descendant main builds supersede; CI rechecks between bound preparation and promotion.
- D-4 Preserve required Writing tests, health gates, Max-first behavior, drainage and all protected volumes.
- D-5 Ship/watch elapsed starts before the first push attempt; live_at excludes later cleanup, not safety gates.
- D-6 Speaking session released HEAD/staging after its local commit 092e9434dc3b54b8a41ba8ca67e89ccdc154ed01; no push occurred. The deployment commit will be its descendant. Preserve unrelated dirty files.
- D-7 Jev planning/cutover/cache decisions are validated semantic evidence, not executable verification.
- D-8 Independent findings are triaged through validated live Jev: bound no-op proof, HTTP 200, scoped registry cleanup, earlier pair recovery and successful-promotion preference.
- D-9 Renewed owner task authorization is limited to this repository. Live Jev validated process-scoped existing owner authentication; native identity/admin=true were verified. Shared CLI defaults and stored credentials stay unchanged; the ship lease still owns visibility.

## Touched files

| Path | Change |
| --- | --- |
| .github/workflows/build-images.yml | classification, immutable reuse, same-publish SQL/references, caches, guards |
| .github/workflows/production-deploy.yml | artifact-only SQL, bound prepare/promote, actual promotion evidence |
| scripts/deploy/release-manifest.mjs + .test.mjs | new provenance helper and focused offline regressions |
| scripts/deploy/auto-deploy-ghcr.sh + docker-compose.production.yml | service reuse, durable router reload, paired rollback and serving proof |
| scripts/deploy/verify-*.sh + verify-pipeline-contract.mjs + prune-stale-images.sh | revised mechanical contracts and image-ID protection |
| scripts/ship/ship.mjs + watch-deploy.ps1 | inclusive timing and actual serving-release verification |
| Dockerfile + per-image Docker ignore files + next.config.ts | consumed contexts and trusted build-cache persistence |
| docs/ops/deploy-gate.md + AGENTS.md + deployment/validation instructions | current pipeline-only operational contract |
| SESSION_STATE.md + TASKS.json + PROGRESS.md | owned task state and durable handoff |

## Verification gates

| Gate | Command / workflow | Evidence | Result |
| --- | --- | --- | --- |
| ship-gate | pnpm run ship:gate | local:ship:gate | PASS |
| regressions | build-images.yml / guards | 37168983221 | FAIL |
| images + SQL | build-images.yml | NOT RUN | NOT RUN |
| Writing | build-images.yml / writing-model-answer-gate | NOT RUN | NOT RUN |
| deploy + live proof | production-deploy.yml + ship watcher | NOT RUN | NOT RUN |
| cold benchmark | build-images.yml / benchmark dispatch | NOT RUN | NOT RUN |

## Blockers

- The shared CLI default lacks admin, but the existing repository-owner credential was natively verified with admin=true in an isolated process. Use it only in bounded approved ship/measurement processes; do not change the shared default.
- First Actions run 37168983221 executed 30 regressions: 29 passed, router-start failure injection did not match its padded command suffix. Live Jev validated the fixture-boundary correction without weakening the production failure path or rollback assertions. Re-run acceptance is pending; no production promotion occurred.
- The ship lease kept the repository public while other Actions are queued/in progress. Do not flip it private until the lease safety check permits it.

## Next action

1. Commit the corrected offline router-start fixture, then rerun pnpm run ship with process-scoped verified owner authentication. Own Actions through actual live proof and warm/cold/Writing measurements; restore privacy under the lease rule.
