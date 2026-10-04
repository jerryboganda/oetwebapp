# SESSION STATE

Session: deployment-latency
Goal: Reduce main-push-to-healthy-serving deployment latency toward 300 seconds on existing free hosted runners, preserving release, migration, health and rollback contracts.
Mode: execute
Updated: 2026-10-04T03:16:28Z
Branch: main
HEAD: f13b93bd47b74123b4cbeba09dc81ce2e0a19094

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

- [x] AC-1 Only changed runtime inputs compile; every reused component has verified immutable provenance.
- [x] AC-2 API SQL comes from the same publish compilation; required Writing tests really execute.
- [x] AC-3 Unchanged healthy services stay running; router cutover is durable and rolls back as a pair.
- [x] AC-4 Actions regression/build/deploy evidence and exact serving-image proof are recorded.
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
- D-10 First real promotion is verified, not five-minute acceptance: before-push to watcher live proof was 744.784 seconds. Benchmark 37170735127 was build-only, 322 seconds; it never promoted.
- D-11 Measured follow-up keeps beneficial Next caching, exports it natively, opts the existing test project into the exact required Writing sources and separates stable API publish bytes. Live typed Jev approved each choice; native Actions checks remain required.
- D-12 Benchmark-only builds must not replace the real watcher prerequisite. Preserve actual promotion preference, no-op proof and genuine release failure handling.
- D-13 Independent follow-up review found a PowerShell 5.1 array candidate. Live typed Jev approved explicit native enumeration and actual-source checks on existing Windows 5.1/Linux 7 Actions lanes; no installation or runtime prerequisite change.
- D-14 Follow-up f13b93bd4 is genuinely live: 37 protocol checks, native cache export/persistence and complete runtime-byte checks passed. Inclusive watcher timing was 569.457 seconds; driver public-live timing was 543.109 seconds. Neither meets 300 seconds.
- D-15 AX 37172886438 failed before jobs because step shell expressions reject runner context. Live typed Jev approved using the existing matrix.runner instead; actual native-engine Actions checks still need execution.

## Touched files

| Path | Change |
| --- | --- |
| .github/workflows/build-images.yml | classification, immutable reuse, same-publish SQL/references, native cache export, exact Writing compilation, guards |
| .github/workflows/production-deploy.yml | artifact-only SQL, bound prepare/promote, actual promotion evidence |
| scripts/deploy/release-manifest.mjs + .test.mjs | new provenance helper and focused offline regressions |
| scripts/deploy/auto-deploy-ghcr.sh + docker-compose.production.yml | service reuse, durable router reload, paired rollback and serving proof |
| scripts/deploy/verify-*.sh + verify-pipeline-contract.mjs + prune-stale-images.sh | revised mechanical contracts and image-ID protection |
| scripts/ship/ship.mjs + watch-deploy.ps1 + .github/workflows/ax-check.yml | inclusive timing, benchmark-purpose isolation, actual-source PowerShell compatibility and serving-release verification |
| Dockerfile + per-image Docker ignore files + next.config.ts | consumed contexts and trusted build-cache persistence |
| backend/Dockerfile.runtime + backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj | stable publish layers with byte verification; deployment-only Writing source set |
| docs/ops/deploy-gate.md + AGENTS.md + deployment/validation instructions | current pipeline-only operational contract |
| SESSION_STATE.md + TASKS.json + PROGRESS.md | owned task state and durable handoff |

## Verification gates

| Gate | Command / workflow | Evidence | Result |
| --- | --- | --- | --- |
| ship-gate | pnpm run ship:gate | local:ship:gate | PASS |
| 31 protocol regressions including apphost | build-images.yml / guards | 37170176749 | PASS |
| verified release provenance | build-images.yml | 37170176749 | PASS |
| 26 real Writing tests | build-images.yml / writing-model-answer-gate | 37170176749 | PASS |
| deploy + live proof | production-deploy.yml + ship watcher | 37170535406 | PASS |
| cold build-only benchmark | build-images.yml / benchmark dispatch | 37170735127 | PASS |
| 37 follow-up protocol regressions | build-images.yml / guards | 37172887015 | PASS |
| native cache export/persistence and runtime bytes | build-images.yml / build-web + build-api | 37172887015 | PASS |
| exact deployment Writing source set and real tests | build-images.yml / forced Writing benchmark | NOT RUN | NOT RUN |
| native PowerShell 5.1/7 selection | ax-check.yml / self-test matrix | NOT RUN | NOT RUN |
| follow-up serving/timing | production-deploy.yml + ship watcher | 37173143817 | PASS |

## Blockers

- The shared CLI default lacks admin, but the existing repository-owner credential was natively verified with admin=true in an isolated process. Use it only in bounded approved ship/measurement processes; do not change the shared default.
- The first inclusive result missed 300 seconds. API publish was 181 seconds, Writing 72 seconds, parallel image pulls 88.621 seconds and actual API readiness 76.594 seconds. Direct HTTP readiness already polls every three seconds; changing Docker health intervals would not address it.
- Follow-up f13b93bd4 built/deployed successfully, but its pipeline-only classification skipped Writing. The opt-in source set and actual required test count need a guarded forced-Writing benchmark. AX's one-line context correction is not yet pushed or validated.
- The ship lease kept the repository public while other Actions are queued/in progress. Do not flip it private until the lease safety check permits it.
- Native GitHub has an additional stale PR SCA queue record, 25907015352 (15 May, no jobs/artifacts, superseded ancestor). Standard cancellation says completed; documented force-cancel says not queued. Neither resolved it. Do not bypass privacy safety or delete the record without fresh specific authorization.

## Next action

1. Ship the scoped AX context correction and this genuine follow-up evidence (no runtime input change), then verify the actual native PowerShell matrix and forced-Writing cold build-only benchmark. Commit/ship the released eleven-path Speaking repair separately for a genuine changed-web/immutable-API measurement; report real run IDs to its owner and restore privacy only under the lease rule.
