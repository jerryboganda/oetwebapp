# SESSION STATE

Session: deployment-latency
Goal: Reduce main-push-to-healthy-serving deployment latency toward 300 seconds on existing free hosted runners, preserving release, migration, health and rollback contracts.
Mode: verify
Updated: 2026-10-04T04:20:10Z
Branch: main
HEAD: 0689edfd042684923c09b4c4ec3c8082b8833763

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
- [x] AC-5 Inclusive elapsed measurements disclose cold/Writing/queue misses and unmeasured categories; no 300-second guarantee claimed.

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
- D-15 AX rejected both runner and matrix step-shell expressions. Move selection to supported job defaults, not another step expression. AX 37174084749 passed actual-source cases on native Windows 5.1.26100.33438 and Linux 7.6.6.
- D-16 Forced-Writing cold build-only benchmark 37173946904 evaluated 3 gate sources versus 666 default sources, reused apphost-consistent API references and executed 26/26 tests. Workflow elapsed was 310 seconds; no promotion or cold-live result.
- D-17 Genuine changed-web release e96e762ac: build 37174218390 reused the exact f13 API/backup/gateway digests, cached Next compilation took 5.7 seconds, and production 37174402463 verified green physical serving images. Inclusive timing was 582.880 seconds; driver public-live was 528.521 seconds. SQL was actually skipped on deployed-API proof.
- D-18 Live Jev selected native web copy-time ownership and stable dependency layers (p .99, confidence .99) after a measured 91.524-second image pull. Preserve original bytes, root-owned dependencies, nextjs-owned .next/public, entrypoint and health gates; native image assertions and live timing remain required.
- D-19 F6 build 37175518557 passed 38 protocol checks and native copy partition but failed before publication: Alpine BusyBox rejects GNU sha256sum --check/--quiet. Its supported -c/-s correction retained every assertion; e96 stayed live until the corrected 068 release.
- D-20 Corrected 0689edfd0 build 37176154885 passed 38 tests and actual WEB_RUNTIME_BYTES_AND_OWNERS_OK. Production 37176403559 verified blue physical images/public HTTP 200. Inclusive watcher 529.552s, driver 491.713s; target unmet. Native API reused; pull 54.659s/readiness 14.482s/layer export 2.2s. Next was cold at 89s after the Dockerfile cache input changed.
- D-21 QA 37175518536 and 37176154904 passed preserved frontend repairs (tsc/encoding/lint/vitest/build); backend skipped. E96 pending QA was superseded with zero jobs, not evidence. Preserve one backend=always evidence run after the final documentation push, followed by the Speaking owner.
- D-22 Live validated Jev selected truthful measured free-runner closeout (p/confidence 1): no speculative boot rewrite or weakened gates; report target misses, retain repaired-backend evidence and require specific authorization/native safety for privacy closure.
- D-23 Final ax:verify matched 78 recorded rows and 14 cited gate run IDs to native GitHub; ax:check and owned diff whitespace check passed. No local build/test workload ran.

## Touched files

| Path | Change |
| --- | --- |
| .github/workflows/build-images.yml | classification, immutable reuse, same-publish SQL/references, native cache export, exact Writing compilation, guards |
| .github/workflows/production-deploy.yml | artifact-only SQL, bound prepare/promote, actual promotion evidence |
| scripts/deploy/release-manifest.mjs + .test.mjs | new provenance helper and focused offline regressions |
| scripts/deploy/auto-deploy-ghcr.sh + docker-compose.production.yml | service reuse, durable router reload, paired rollback and serving proof |
| scripts/deploy/verify-*.sh + verify-pipeline-contract.mjs + prune-stale-images.sh | revised mechanical contracts and image-ID protection |
| scripts/ship/ship.mjs + watch-deploy.ps1 + .github/workflows/ax-check.yml | inclusive timing, benchmark-purpose isolation, actual-source PowerShell compatibility and serving-release verification |
| Dockerfile + per-image Docker ignore files + next.config.ts | consumed contexts, trusted cache persistence, stable web dependency layers and original byte/owner assertions |
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
| exact deployment Writing source set and 26 real tests | build-images.yml / forced Writing benchmark | 37173946904 | PASS |
| native PowerShell 5.1/7 actual-source cases | ax-check.yml / self-test matrix | 37174084749 | PASS |
| follow-up serving/timing | production-deploy.yml + ship watcher | 37173143817 | PASS |
| genuine changed-web/reused-API serving/timing | build-images.yml + production-deploy.yml + ship watcher | 37174402463 | PASS |
| 38 protocol tests and actual web bytes/owners | build-images.yml / guards + build-web | 37176154885 | PASS |
| final serving/timing and native API reuse | production-deploy.yml + ship watcher | 37176403559 | PASS |
| preserved frontend repairs, backend explicitly skipped | qa-smoke.yml / frontend-unit | 37176154904 | PASS |

## Blockers

- The shared CLI default lacks admin, but the existing repository-owner credential was natively verified with admin=true in an isolated process. Use it only in bounded approved ship/measurement processes; do not change the shared default.
- The 300-second target remains unmet. Verified inclusive releases measured 744.784 -> 569.457 -> 582.880 -> 529.552 seconds. The final release was cold web compilation with healthy API reuse, not a full cold/Writing-live release. Cold forced-Writing build-only took 310 seconds. Direct HTTP readiness already polls every three seconds; do not remove gates or claim warm projections as measured acceptance.
- Preserved backend fixtures still need the final one-time backend=always evidence run because the repaired pending QA was superseded; frontend and actual runtime image/build/deploy evidence are green. Speaking peer owns changed-fixture triage.
- The ship lease kept the repository public while other Actions are queued/in progress. Do not flip it private until the lease safety check permits it.
- Native GitHub has an additional stale PR SCA queue record, 25907015352 (15 May, no jobs/artifacts, superseded ancestor). Standard cancellation says completed; documented force-cancel says not queued. Neither resolved it. Do not bypass privacy safety or delete the record without fresh specific authorization.
- At 04:26Z, it was the only queued/in-progress record, the ship lock/lease were free and the repository was PUBLIC. Specific deletion approval was requested; the owner was unavailable, so no authorization was granted and no history was deleted.

## Next action

1. Verify the real cited gates with ax:verify, commit/ship only final measurement/doc/state/evidence paths (no runtime input change), dispatch QA backend=always once on the resulting main SHA and give its exact ID to the Speaking owner. Restore privacy only when the native guard permits; otherwise record the exact missing authorization/queue blocker without deleting history or bypassing it.
