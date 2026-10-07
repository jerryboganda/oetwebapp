# PROGRESS — Durable Checkpoint Ledger

Last updated: 2026-10-07

## How this file works

- Compact durable checkpoints only, newest first. One entry ≤ 10 lines, ideally with the real
  GitHub Actions run id that proved it.
- **This is not the current run's memory.** That is `SESSION_STATE.md` + `TASKS.json`.
  Verified evidence is `VERIFICATION.md` (machine-written — never hand-edit it).
- Do not paste historical ledgers here. Verbatim history lives in
  `docs/PROGRESS-ARCHIVE-2026.md`; older still is `git log -- PROGRESS.md`.

## Active checkpoint - Writing-AI urgent production patch (2026-10-07)

- P0 root cause (live, SSH+DB evidence): the codex sidecar's ChatGPT workspace is OUT OF CREDITS — every gpt-6.1-sol reviewer call 502s ("Your workspace is out of credits", 14/14 attempts in 24 h). Grader (writing-claude-sub) healthy; the owner's 2 letters exhausted retries into failed/grading_delayed during the outage (letters intact, Retryable). OWNER MUST TOP UP the ChatGPT workspace; then Retry proves grader→Sol→stored.
- 15-minute release acceptance PASSED live with zero problems (run 37547511993): countdown 14:59 at submit, reload 14:49, close+75 s reopen 13:29 (no reset), Post Submissions row present, auto-release exactly at the server instant, results opened, one grade call (no dup charge); reviewer outage completed the letter via the bounded WAI-07 fallback. Evidence: .tools-state/writing-samples-7oct/.
- Nursing Free Sample (Adam White) REPLACED with the approved verbatim vector retype and verified live (build 37546219716, swap run 37549715914, new asset b9243d78…, 24× smaller, fonts embedded, zero images, publishReady=true).
- All 223 published Writing stimuli classified (poppler on VPS): 136 vector + 81 crisp digital rasters (PASS legibility) + 6 genuine photo-scans (Erika Stone, Jonathon Apple, Sarah Day, Robert Smithson, James Andresen, Roger Stanton). Retype pipeline (OCR line-segment rebuild + corrections layer + review pairs): scripts/materials/retype-stimuli.mjs, retype-ledger.json (86 rows), workflows writing-samples-retype.yml / writing-stimulus-swap.yml. The 6 photo-scans need further per-document corrections cycles before swap.
- writing-release-qa.yml lives PARKED in .github/manual-workflows/ (pipeline contract: CI never runs automated QA); move-in → dispatch → move-out per the README there. Repo flipped PUBLIC 23:53 UTC 6 Oct for the Actions wave — flip PRIVATE once the photo-scan rebuilds finish.

## Active checkpoint - Writing AI-Final handoff (2026-10-06)

- Owner handoff implemented in one change set: candidate Writing shows no internal ids, no Revise & Resubmit, "Criteria score N/38", advisory never scores or ranks, Top Priorities distinct and impact-ordered.
- Secondary reviewer (Codex gpt-6.1-sol, feature code writing.grade.review) runs before publication; deterministic applier, soft uncapped 400+ verification; mode from FeatureFlags writing_ai_reviewer_shadow / writing_ai_reviewer and the writing-codex-sub row; Enforce holds on outage.
- 15-minute release window derived from SubmittedAt at read time (no schema change); five owner accounts exempt from the delay and from copy/paste; countdown is server-anchored.
- Hashed regression inputs, ValidatorVersion and the Model Answer lane untouched. Nothing compiled or tested locally: first compile is Build images, then owner QA (Medicine C/C+/B sweep, shadow reviewer first).

## Active checkpoint - Package sync, linked pricing, mobile multiline fix, website Login CTA (2026-10-06)

- Owner spec 06 Oct 2026. Root causes: overlay merge applied only `featured`; the whole-blob presentation PUT let the Storefront and Packages editors wipe each other; one-per-line textareas re-derived their value from a trimmed array (Enter/space lost on desktop too); the website CTA lived only in the hamburger / >=1500px nav.
- Master = `CatalogPresentationJson.websitePackages` keyed by plan code (static TS copy = factory default); Name/Description/bullets mirror onto BillingPlan/BillingAddOn/ContentPackage without a new version; price/currency/interval/status edit the same row (one version). Section-scoped PUTs with hash revisions (409 on stale), audit, no-store, public prune. Supersedes the 14 Aug "never overlay" rule.
- No schema/migration. Reviewed twice by independent static reviewers; not compiled or run locally (owner directive 2026-10-06) - compilation happens in Build images, everything else is owner QA.
- Website CTA + `oet_signed_in` hint cookie: Website branch `feat/website-login-cta` (root HTML via that repo's CI); VPS deploy needs the owner's go-ahead.

## Previous checkpoint - Compulsory accelerated release enforcement (2026-10-04)

- Owner requires every contributor/agent to retain the live510.240s architecture and explicitly authorizes scoped commit/main push/deploy; no duration guarantee or weakened gates.
- Live Jev approved guard/lifecycle/inheritance/console corrections (all p/confidence/margin1); Claude/Gemini imports and contributor/Copilot guidance inherit one baseline. Console engines remain isolated agent-branch/PR-only.
- Guarded controls reject completion/visibility/workflow bypasses, missing checkers and unknown native data; scoped mutations protect proven reuse/cache/SQL/Writing/runtime contracts. Console expiry retries safely, never force-private over holders/runs.
- Exclusive local locks/atomic state and the existing native workstation/console holder channel prevent missing-shaped state or one-way lease coverage; variable writes are not claimed as a global mutex. Additional live Jev approved both corrections; runnable boundary checks remain Actions-only.
- Native full e748QA37180577741 finished cancelled, not green; focused67/67 and actual production37180764537 proof are unchanged. Preserve the additional Writing/retired-worktree checkpoints.
- Privacy: native May25907015352 and its check-suite remain queued/zero jobs/checks, no pending-deployment approval; age exceeds GitHub30-day rerun limit. Exact deletion request unavailable, no grant; no history exclusion or bypass.
- First441 build37193948006 failed69/70 only on native PowerShell presentation; approved stdlib-only correction327 passed70/70 at37194323785. Deploy37194396337 public/physical blue proof284.177s (4m44.18, four unchanged proven digest reuses). AX37193948034 native matrices and441/327 frontend QA green; backend skipped.
- Console469 tests/images green, but six idle checks and final11:22:41Z remainoldc81/2 active. Restart approval unavailable, no grant. Refined live protected_pending boundary validated1 after uncertain preference paused. Main9e2 QA37197421291 frontend/placement/gate passed, backend skipped; real evidence recorded, console/private gates still BLOCKED.
- Owner issued the fresh specific grant for exactly the two blocked gates (2026-10-04 ~11:45Z): interrupt the two active console owner turns and apply the console update, and delete the stale May record for privacy restoration.
- Exact deletion executed with the verified owner credential: DELETE actions/runs/25907015352 returned HTTP 204 and native GET now 404; the phantom queued record left the Actions queue.
- Console apply=true dispatch 37199930253 passed all eight jobs; runtime recreated on exact main fde751378e3e39aeaa861560c5be6c807d09890e images with /healthz ok/activeTurns 0/draining false and the update-pending marker cleared; interrupted turns remain resumable.
- Privacy restoration goes through the guarded ship close only: flip private when no holder and no queued/in-progress run remains (unrelated Writing Rev8 37199702750 was in flight during the round, not owned by it).

## Previous checkpoint - Verified API correction and measured deployment closeout (2026-10-04)

- E7485ce2f is actually live in green: build37180577765 passed38 protocol/original-byte checks; production37180764537 verified/applied same-publish SQL and exact physical/public serving.
- Inclusive push-to-verified-live510.240s; driver460.272s, image pull22.908s, initial readiness68.965s, recheck13.755s and router cutover7.464s. Five-minute target remains unmet under approved free-runner scope.
- One forced QA 37177363819 passed frontend, all six default backend builds and shards 1/2/3/5/6; only shard4/dependent gate failed.
- Raw397/public400 regression repaired in one source using selected persisted tuple scores; readiness/holds preserved and assertions unchanged. Native37180640839 TRX executed/passed67/67, zero skipped, including exact regression/hold/classic/clean cases.
- Whole-solution compile111372344831/frontend111372344713 green; scoped Vitest12 cases in report-view file only. Manual lane regenerates canonical rulebooks, not a substitute for actual production-byte proof.
- Source owner independently confirmed/released correction. Live validated Jev p/confidence1 selected truthful best-effort closeout, not speculative startup rewrites or weakened gates.
- FullQA37180577741 later ended cancelled (recorded above), not whole-suite green. PUBLIC restoration is blocked by empty May25907015352 lacking specific deletion approval; native guard/lease never bypassed.

## Previous checkpoint - Deployment acceleration, measured free-runner target misses (2026-10-04)

- Wrote immutable component reuse, same-publish SQL/Writing references, persisted caches, lean contexts and health/config-aware service reuse; bound paired router recovery/serving identity remain enforced.
- Added inclusive ship timing, actual-promotion/no-op proof and offline provenance/driver/watcher/registry regressions.
- Existing owner authentication is verified process-scoped; shared CLI defaults/credentials remain unchanged.
- SHA 66458ca821: Build images 37170176749 passed 31 protocol checks and 26 real Writing tests; production 37170535406 served exact images in green.
- Inclusive push-to-verified-live was 744.784 seconds, not 300; cold build-only benchmark 37170735127 passed in about 322 seconds without promotion.
- Follow-up f13b93bd4: build 37172887015 passed 37 protocol tests, native cache persistence and complete runtime bytes; deploy 37173143817 served blue at 569.457 seconds inclusive (driver 543.109).
- AX 37174084749 passed actual-source cases on native Windows 5.1/Linux 7.6.6; forced-Writing benchmark 37173946904 passed 3/666 evaluated sources and 26 tests in 310 seconds build-only.
- Genuine changed-web/reused-API e96: build 37174218390 passed, deploy 37174402463 verified green at 582.880 seconds inclusive (driver 528.521); Next compile 5.7s, image pull 91.524s, native readiness 94.649s.
- Corrected web-layer release 0689edfd0: build 37176154885 passed 38 tests and actual byte/owner assertions; deploy 37176403559 verified blue at 529.552s inclusive (driver 491.713s). Cold Next 89s, pull 54.659s, reused-API readiness 14.482s.
- Frontend QA 37176154904 passed; preserved backend correction evidence is now recorded above. Target unmet and full cold/Writing-live unmeasured; private restoration faces the inconsistent empty May queue.

## Previous checkpoint - Writing journey repairs (2026-10-03)

- Fixed canon detail links and E2E session recovery through inert `/api/health` before fresh auth hydration; application auth remains unchanged.
- Completed the isolated demo's existing authored case notes, finite expiring three-credit Writing package and published mock; first-creation guards prevent replenishment or real-user backfills.
- Strict browser run `37084164663`, job `111090886562`, at `b17603b10`: nine passed (auth setup plus all six Writing journeys), zero skips/retries; drills cover selection/feedback/reset and mocks require a real locked-reading start.
- Scoped Writing/critical-flow backend, whole-solution compile, canonical rulebooks and frontend checks passed in `37084166890` at the same code SHA.
- CountUp exposed an intermediate score to the live check; exact-text waiting alone fixed it. Final code `fbcc5943236dc69a7f6f61eb768dcf74dce33e63` deployed successfully in `37087118739`, job `111102432795`; exact web/API images are healthy and all three public health endpoints return 200.
- Final post-deploy read-only browser `37088569322`, job `111103802960`: normal sign-in, grading-to-results redirect, six criteria, same saved grade/report on desktop and mobile refresh, zero Writing mutations.
- Removed both owned temporary learner secrets and verified their names absent; unrelated credentials and the other session's goal/tasks remain untouched.
- No further assessment, paid call, provider forcing or production data mutation; original submission untouched and current Max-always-on policy preserved.

## Previous checkpoint — AX: externalized agent working memory (2026-10-01)

- `.github/agent-state.local.md` was named as the current-task handoff in 21 files but did not
  exist and is gitignored, so every "read the handoff" gate pointed at nothing.
- Replaced with a tracked `SESSION_STATE.md` ledger + `TASKS.json` queue + machine-written
  `VERIFICATION.md` evidence index, driven by `scripts/agent/state.mjs` (`pnpm run ax:*`).
  The old path is now the gitignored raw evidence journal.
- `pnpm run ax:check` fails a `PASS` gate with no run id, workflow file or `local:<command>`,
  so a gate can no longer be ticked without evidence.
- Retired the stale local-Docker validation rule from the `.github/agents/**` and
  `.codex/skills/**` surfaces; they now point at the single Actions-only ladder instead of
  restating it, which removes the drift class.
- `PROGRESS.md` compacted; the retrospective checkpoints moved verbatim to
  `docs/PROGRESS-ARCHIVE-2026.md`.

## Previous checkpoint — Writing grading failure recovery

- Fixed misleading exemplar progress and failure visibility without refresh; focused UI/backend Actions checks passed.
- Diagnosed Codex HTTP 404: seeded root BaseUrl posts `/chat/completions`, while the sidecar accepted only `/v1/chat/completions`. The shared handler accepts both routes; red/green protocol evidence is in runs `36785645083` / `36785869736`.
- Fixed the large-prompt Codex argv transport with the existing stdin path; offline red/green runs `36814911854` / `36815163321`, including actual-image CLI checks with no network or inference. Sidecars at `2be1986a5385b83c4702e1869e5d1811c9ace5c1` are Healthy; credential volumes preserved.
- Preserved concurrent Speaking PR #305. Main deploy `36824151971` succeeded at `ed834765d4c6548e778f3cc52ad3a50503800e35` with exact web/API image tags and public health gates.
- Real Codex-only recovery passed in `36825878639`, job `110251344304`: same QA submission `8af5d137-6f43-4e28-b278-af936d6bf165`, saved grade `b9cd9019-e6e3-43f7-a34f-0009fd6bea62`, visible six-criterion report `4e1b550c-1e1d-41f3-bb00-122a1226d4e2`. Already-graded retry reused the grade with no new provider call; prior `auto` mode restored. Scoped frontend/backend/compile/canonical gates passed.
- User authorized one subscription-only QA assessment and incident-specific Jev waiver; paid Writing APIs remain forbidden. Original user submission untouched.

## Older checkpoints

Verbatim, newest first: `docs/PROGRESS-ARCHIVE-2026.md`
(UBAG provider board → FINAL Speaking brief → AI Packages conformance → Master Catalogue wave 1 →
Antigravity integration → Firebase OTP → answer-key reports → Atlas/Reading waves → billing checkout
→ AI packages → the 2026 PR-#38 portfolio work).
Older still: `git log -- PROGRESS.md`.

## Next-Step Protocol For New Agent Runs

1. Read `AGENTS.md`, `.github/copilot-instructions.md`, `SESSION_STATE.md`, `TASKS.json`, this file, and the domain doc for the surface you touch.
2. Non-trivial work: `pnpm run ax:status`, then continue from `SESSION_STATE.md` when its Goal matches the newest request; otherwise re-goal it with `pnpm run ax:init`.
3. Pick work with `pnpm run ax:next`.
4. Compute (build / test / lint / typecheck) runs on GitHub Actions only. `pnpm run ship:gate` and `pnpm run ax:check` are the only local checks — see `.github/instructions/validation.instructions.md`.
5. Production deploy uses GitHub Actions + GHCR images; never build on the VPS.
6. After this SHA's deploy is green: `pnpm run ax:record`, then `pnpm run ax:verify`, then update `SESSION_STATE.md` with validation, blockers and the next concrete step.

## Active Risks

- `SESSION_STATE.md` and `TASKS.json` are tracked and rewritten per task, so two parallel agent sessions can conflict. Take the newer `Updated:` block wholesale rather than hand-merging; `PROGRESS.md` remains the merge-safe durable ledger.
- State enforcement is warn-only inside `ship:gate` by design, so a session can still push a stale ledger. `pnpm run ax:verify` is what makes a false evidence claim detectable after the fact.
- Never stage unrelated untracked paths; `git add` explicit paths only.
