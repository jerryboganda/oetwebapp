# Agent State (local)

## Current task — Writing Assessment: Fix false missing recipient block on submit (Doris White) — VERIFIED + READY TO SHIP
- **User Request**: When submitting letter for grading on "Medicine - Doris White" in writing practice session, error banner appeared: `"Writing assessment is blocked because required input is missing: recipient."`
- **Root Cause**: `WritingTaskUnderstandingService.RecipientCategory` had an overly narrow set of 6 regexes (`emergency registrar`, `occupational therapist`, `physiotherapist|social worker|psychologist|dietitian`, `GP|general practitioner`, `admissions officer`, `Dr|doctor|consultant|clinician`). The Doris White task prompt ("write a letter to Mrs Lucy Walters , a Community Nurse , requesting for wound dressing...") did not match any of those patterns, returning `"unknown"`. `WritingAssessmentPreflightService` then blocked grading with `"recipient"`.
- **Fix**:
  1. `WritingTaskUnderstandingService.cs`:
     - Expanded `RecipientCategory(task, notes)` to detect `community_nurse` (Community Nurse, District Nurse, Home Care Nurse), `nurse` (Nurse, Charge Nurse, Registered Nurse, Practice Nurse, Nurse Unit Manager, Sister-in-Charge, Matron), expanded medical clinicians and specialists without mandatory "Dr" prefix (cardiologist, endocrinologist, neurologist, dermatologist, surgeon, etc.), admissions officers, and named recipients (`Mr|Mrs|Ms|Miss|Prof` or direct address).
     - Added `ExtractPlanOrReferralLines` fallback: if the task prompt is generic, parses `Plan:`, `Referral:`, or `Address:` sections in the case notes to extract the recipient.
     - Expanded `AddSignal` non-medical referral keywords to include `dietician`, `speech pathologist`, `speech therapist`, `podiatrist`, `audiologist` (keeping nurses strictly medical per Rule R01.7 / R15.1).
  2. `WritingTaskUnderstandingTests.cs`:
     - Added unit tests for Doris White task prompt, nurse and nurse-in-charge detection, specialist detection without "Dr", named recipients with honorifics, and case notes plan fallback.
  3. `WritingAssessmentPreflightTests.cs`:
     - Added `Doris_white_community_nurse_recipient_allows_scoring` integration test validating that Doris White submission passes preflight with `CanScore == true` and 0 missing input codes.
- **Validation**:
  - `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter "FullyQualifiedName~WritingTaskUnderstandingTests|FullyQualifiedName~WritingAssessmentPreflightTests"` passed (16 passed, 0 failed).
  - `pnpm run ship:gate` passed (`ship-gate files=10 typescript=yes`).
- **Next**: Ship via AGENTS.md Ship-It workflow.

## Previous — Layout Alignment across /get-app, Boost widget, and /goals — READY TO SHIP
- **User Request**: Layout alignment across all screen sizes (laptops, desktops, mobiles, tablets) per uploaded photo of `/get-app` with misaligned button baselines and mismatched columns, along with `/boost` and `/goal` alignments.
- **Root Causes**:
  1. `/get-app` cards had mismatched subtitle line counts (macOS 3 lines vs Windows 1 line) with insufficient minimum height, and `store-badges.tsx` badge had fixed `px-7` padding causing "Download iOS App" to wrap and collide with `leading-none`.
  2. Bottom features section used an asymmetric 2-column subgrid that did not line up with the 4-column download cards row.
  3. Dashboard Addons Boost widget had asymmetric button padding (`pt-4` overrode `py-2.5`) and differing card title heights.
  4. `/goals` weak sub-tests grid was stuck in 2 columns while target scores used 4 columns (`sm:grid-cols-4`).
  5. Shorthand `/goal` and `/boost` routes returned 404.
- **Fixes**:
  1. `app/get-app/page.tsx`: Set text container `min-h-[64px] sm:min-h-[76px]` so card text heights stay consistent across desktop, tablet, and mobile, locking all 4 buttons to the exact same baseline. Restructured features into a matching 4-column grid (`sm:grid-cols-2 lg:grid-cols-4`) aligning with the download cards row. Redesigned the companion QR code section into a responsive banner.
  2. `components/marketing/store-badges.tsx`: Added responsive padding (`px-4 sm:px-6`), glyph scaling, and `whitespace-nowrap` with single-line font scaling (`text-xs min-[400px]:text-sm sm:text-base`) for badges.
  3. `components/learner/dashboard-addons-widget.tsx`: Added `id="boost"`, title container `min-h-[38px]`, description container `min-h-[36px] mb-4`, and symmetrical `mt-auto` button positioning.
  4. `app/goals/page.tsx`: Aligned weak sub-tests grid to `grid-cols-2 sm:grid-cols-4`.
  5. `next.config.ts`: Added redirects for `/goal` -> `/goals` and `/boost` -> `/#boost`.
  6. `next.config.test.ts`: Added test cases for the new redirects.
- **Validation**:
  - `pnpm exec vitest run --exclude "**/pdf-policy-release*/**" app/get-app/page.test.tsx components/marketing/app-download-promo.test.tsx next.config.test.ts components/layout/__tests__/learner-dashboard-shell.test.tsx components/auth/__tests__/auth-screen-shell.test.tsx` passed (5 files, 12 tests).
  - `pnpm run ship:gate` passed (`ship-gate files=86 typescript=yes`).
  - `git diff --check` clean with 0 whitespace errors.
- **Next**: Ship via AGENTS.md Ship-It workflow (commit, public, push, watch, private, live health check).
- **Root Cause**: `CourseContentMatrix.cs` hardcoded that all English videos (`lang == "en"`) were shared across all professions, causing English Writing/Speaking videos to have `ProfessionIdsJson: []` and pass `VideoAppearsFor` regardless of candidate profession.
- **Fix**:
  1. Updated `CourseContentMatrix.cs` so that Writing and Speaking videos require explicit profession targeting in both English and Arabic (`ExpectedVideoTargets`, `TryValidateVideo`, `VideoSourceLabel`). Listening, Reading, and Basic English remain shared across all professions.
  2. Added EF Core migration `20261213090000_SyncLibraryVideoProfessionTargetsFromScope.cs` to synchronize `ProfessionIdsJson` for all Writing/Speaking videos from their `VisibilityScope` (FULL_MEDICINE/CRASH -> `["medicine", "physiotherapy", "dentistry", "radiography"]`, FULL_NURSING -> `["nursing"]`, FULL_PHARMACY -> `["pharmacy"]`).
  3. Applied live data patch to production PostgreSQL (updated 21 records).
  4. Updated unit tests in `CourseContentMatrixTests.cs` and `VideoVisibilityScopeLearnerExclusionTests.cs`.
- **Validation**: `pnpm run ship:gate` passed cleanly.
- **Next**: Commit and deploy via Ship-It workflow.

## Current task — Listening Part B/C headings + Separate Part C + audio pipeline — SHIPPED + LIVE
- Release `ac499562b` + handoff `2cdf9b86d` are on `origin/main` and **deployed**. `Build & Deploy (web + API)` run `33271190604` succeeded 2026-08-29 19:57 UTC, all seven jobs green incl. `migrate-production` and `deploy`. Verified live: `/health/ready` all-ok (database, migrations, stuck_jobs, storage); served `sw.js` carries `CACHE_VERSION = 'oet-v5'` + `EXAM_MEDIA_API`; the new admin route `.../listening/part-bc/source-audit` returns 401 while a fake sibling route returns 404, proving registration.
- **Deploy procedure (project rule — follow it):** Actions runs need the repo **public**. Flip public, run, then flip back to private immediately; never leave it public. `gh repo edit jerryboganda/oetwebapp --visibility public|private --accept-visibility-change-consequences`. Repo confirmed **private** after this run. The earlier GitHub billing block is resolved.
- Root cause of the repeated Part B/C heading: migration `20261128000000_FixAllListeningPartBAndCStems` rewrote every sentinel stem to ONE hardcoded string, scoped by question number only with no paper/status predicate, so it hit every Listening paper. `20261129000000` then blanked it for all papers except Nova `77114cbc020347858619a88928ed0e32`, so the live symptom is now a BLANK heading above three options. It was never a mapping bug.
- Fix ships the recovery mechanism, not the repaired data: `ListeningPartBCSourceParser` + `ListeningPartBCSourceRecoveryService` re-derive stems/options from each paper's own cached question-paper text (`ContentPapers.ExtractedTextJson`), precision-first — ambiguous items are reported, never guessed. Admin routes `.../listening/part-bc/{source-audit,recover-source}` and a panel on the admin Listening → Questions tab. **An admin must run the sweep per paper after deploy.**
- Separate Part C C1→C2 dead button: `ApplyQuestionScope` nulled the combined paper MP3 for scoped practice, so a combined-audio-only paper had cue windows but no audio and the advance gate could never be satisfied; `confirmNextFromAudio` then returned silently. Fixed, plus the parent-key (`C` backing C1+C2) variant that made C1 wait for the whole file and restart C2 at 0:00. Part A practice (A1→A2) had the same defect and is fixed too.
- Atlas Sample 8 audio was already replaced and live from the prior session; this release only hardens the pipeline (upload duration now recorded — its absence made any re-uploaded paper unpublishable; case-safe primary-asset demotion; service worker no longer caches `/v1/media/*` or `/v1/listening/audio/*`, `CACHE_VERSION` → `oet-v5`).
- Validation: 21 new backend tests + 5 new frontend tests, all green. Backend Listening suite 514 passed with the SAME 23 failures the branch already had — confirmed by reverting my changes and re-running the baseline (23 failed / 493 passed). Frontend listening + admin 158/158. Repo-wide `tsc --noEmit` stays red on pre-existing unrelated errors (expert review, mocks results, pdf-policy-release*, scripts/admin).
- Handoff for the verifying agent: `docs/LISTENING-PARTBC-PARTC-AUDIO-VERIFICATION-HANDOFF.md`. It leads with the deploy blocker — nothing in its checklists is meaningful until the deployed SHA is `ac499562b` or later.
- Open boundary: no learner credentials were available in this session, so no authenticated playback or candidate-facing check was performed on production.

## Current task — Listening heading/navigation fix + Atlas Sample 8 audio replacement — SHIPPED + LIVE
- Listening learner backend/source-stem normalization was already on `origin/main`; this continuation adds standalone Part C parent-level audio fallback (C1/C2), guarded forward section transition, section-aware confirmation copy, and a regression test in `app/listening/player/[id]/page.tsx` and its CBLA fidelity suite.
- Atlas Sample 8 production paper `8625c6f593d440c1aeaf7a87ab34d4c0` now has exactly four primary Audio assets (Full, Part A, Part B, Part C) sourced from `C:\\Users\\Dr Faisal Maqsood PC\\Desktop\\9- Sample Test 8`; old 11 Audio rows and five old media records were removed. Part A was normalized to audio-only without modifying the source file. Temporary admin account, grants, trusted device, sessions, and upload/audit rows were deleted; final residual auth/upload counts were zero.
- Release commit `fed52163def3c7c4f25f3009fb25bc34a17092c0` (cherry-picked from local `7cd2ec1ef`) was pushed from a clean `origin/main` worktree to `main`. Build & Deploy run `33206522995` succeeded; web/API/agent images are healthy on the exact SHA, `/api/health`, `/health/ready`, and `/health/live` returned OK, and repository visibility was restored to private.
- Validation: focused frontend Listening slice 41/41; focused backend Listening sanitization 37/37; targeted lint 0 errors (461 warnings, existing); clean-worktree `pnpm run ship:gate` passed with exactly two files. Repository-wide `tsc --noEmit` remains red on pre-existing unrelated admin/reading/pdf-policy-release errors; current worktree retains unrelated AI/Billing edits and they were not staged or shipped.
- Acceptance boundary: production admin API projection and exact asset SHA/size checks passed before deploy; no authorized learner credentials were available for fresh authenticated browser playback on Atlas/Nova desktop/tablet/mobile. Owner should perform that final UI playback smoke when credentials are available.

## Current task — Listening Part B/C release-blocking fixes — DEPLOYED + LIVE / QA BOUNDARIES OPEN
- Implemented shared, fail-closed source-stem normalization for Listening B/C across backend authoring, backfill, learner DTO construction, structure validation, admin answer-sheet import, standalone player, and strict full-exam grouping. Bare/malformed part codes are recovered from canonical printed question numbers; generic headings/sentinels are rejected rather than rendered as stems.
- Restored one verified source paper (`77114cbc020347858619a88928ed0e32`, `Benchmark Listeninig Tests.pdf` Practice Test 1) through migration `20261129000000_RestoreListeningPartBCSourceStems.cs`; migration also strips option/document artifacts and clears invalid unknown-paper B/C stems. No live database migration has been run in this checkout.
- Full Exam Part B groups six deterministic questions Q25–Q30. Part C now exposes one candidate question workspace containing Q31–Q42 across C1/C2, while the two extracts still play sequentially and once; standalone A/B/C and full-exam audio use source-ready one-shot autoplay with a browser-policy fallback, without restarting on normal question navigation.
- This release patch adds cross-extract Part C jump/answer persistence in both full-exam renderers and permits answer writes for either C extract only while Part C is active; the server section cursor remains forward-only for the audio/submission contract.
- Validation: API rebuild 0 errors; focused backend Listening data/structure/authoring/learner suite 136/136 plus 98 no-build source/structure/sanitization/manifest tests; changed frontend Vitest slice 76/76 plus 26/26 legacy-player tests (37/37 combined focused player/page/navigation slice); targeted ESLint 0 errors (22 existing warnings); `pnpm run ship:gate` passed; `git diff --check` has no whitespace errors apart from normal CRLF notices.
- Test commit `758394a015cb12d8c0b9e47259a281b99a48fcc0` adds real legacy-player coverage for standalone A/B/C autoplay, full-exam B Q25–Q30, full-exam C Q31–Q42 across C1/C2, jump/Next navigation, answer persistence, and one-shot playback.
- Deployed SHA `758394a015cb12d8c0b9e47259a281b99a48fcc0` via Build & Deploy run `33173351228`; live web, API ready/live, database/migrations/storage checks, and blue image tags are healthy; unauthenticated policy endpoint confirms Part B=6 and Part C=12; repository is private again. Acceptance boundaries remain explicit: no live authenticated Atlas/Nova API/database corpus audit, authenticated desktop/tablet/mobile browser playback evidence, or production candidate-flow proof; the local source PDF inventory is authoritative only where paper identity is proven, and intentionally partial source papers remain fail-closed. Preserve unrelated AI-control-plane worktree changes.
- Latest continuation deployed as `37d71f25e7f2421e36a2f15d374eee2854891663` via Build & Deploy run `33177256966` (success): fixed the offline-answer reconciliation TypeScript narrowing in `app/listening/player/[id]/page.tsx` and completed the required DTO fields in the focused Part B/C navigation fixture. Validation passed: focused frontend Listening slice 37/37, backend source/structure/sanitization/manifest slice 98/98, targeted ESLint 0 errors (16 existing warnings), targeted root Listening `tsc` errors absent, and `pnpm run ship:gate` OK. Post-deploy web/API ready/API live checks and Part B=6/Part C=12 policy counts are healthy; blue images carry `37d71f25e`; repository is private again. Repository-wide QA Smoke run `33174484937` remains red from unrelated existing errors/environment failures; it is not evidence against the scoped fix. Acceptance boundaries are unchanged: no authorized live Atlas/Nova learner session or desktop/tablet/mobile browser playback evidence, and no canonical Atlas/Nova paper-to-source mapping in this checkout.
- Latest continuation deployed as `a3b8e8673ec8efb5d8c11df09886585ecc9fb866` via Build & Deploy run `33183452157` (success): learner home `PaperHomeDto` now unions normalized `listeningQuestions`/legacy `questions` counts with relational counts by distinct printed question number, so stale partial B/C relational rows cannot advertise only one item when the source set contains Q25–Q30 or Q31–Q42; a regression test covers a 42-question source with partial relational counts. API build and test-project compile both passed with 0 errors; the narrowly filtered test-host retry reached its connection timeout without producing results on this slow host. Post-deploy web/API ready/API live checks, healthy blue images, exact-SHA parity, and policy counts (Part B=6, Part C=12) passed; repository is private again. Acceptance boundaries are unchanged: no authorized live Atlas/Nova learner session or desktop/tablet/mobile browser playback evidence, and no canonical Atlas/Nova paper-to-source mapping in this checkout.


## Current task — Admin Verify Email recovery — SHIPPED + LIVE
- Feature `9d5edaa2e` (feat(admin): add manual candidate email verification): POST /v1/admin/users/{id}/verify-email (AdminUsersWrite) sets EmailVerifiedAt, consumes pending verify_email OTPs, revokes all sessions via ISessionRevocationService (refresh-token fallback if unavailable), writes audit; one-click `Verify Email` button (MailCheck) after Set Password on /admin/users/[id]; instant badge + verified toast; failure keeps current state. Build & Deploy run 32975533469 SUCCESS 2026-08-26; health 200 app/api-ready/api-live; web-blue/api-blue/agent-gateway images tagged 9d5edaa2e...; repo PRIVATE.
- Post-hoc owner-decision compliance fix: canVerifyEmail now requires status == active (suspended accounts no longer show the action); added AdminUsers_VerifyEmail_ActionHiddenForSuspendedAccount.
- COMMITTED+PUSHED `9dd71ec4d` (2026-08-26 ~15:14 UTC) together with this state update; sibling commit `6c4426306` landed on top of it on main. Validation this session: backend verify-email 3/3; admin users page Vitest 12/12 (28/28 with related suites); `dotnet build backend/OetLearner.sln` 0 errors; `pnpm run ship:gate` OK.
- DEPLOYED 2026-08-26 (fix included): owner fixed the billing runner gate; a 16:10 push run died as a private-repo run (~5s, empty logs — see Actions visibility rule), so retried publicly: manual dispatch run `32987591042` on main (HEAD `6c4426306` = fix + sibling access fix) SUCCESS 16:26 UTC. Health 200 web/api-ready/api-live; web-green/api-green/agent-gateway images on `6c4426306` (blue on 9d5edaa2e); repo PRIVATE again. Owner live acceptance of Verify Email still pending (disposable unverified account test).
- Unrelated dirty files PRESERVED uncommitted: AdminRequests.cs, UserAccessAllocationServiceTests.cs, components/admin/user-access/{manage-access-panel,module-toggles,quick-grant-modal}.tsx, root `oet-unrelated-dirty.patch` (listening). Never stage them with task commits.
- QA Smoke + Speaking Module CI chronically red (ignore per AGENTS.md). OWNER NEXT: live acceptance with a disposable unverified test account — admin Verify Email → badge flips immediately → candidate signed out → sign-in lands email-verified without the OTP trap.

## Previous — Per-paper objective practice credits — SHIPPED + DEPLOYED
- Credit rule (first part/full paper per paper = 1 credit; sibling parts/re-attempts free) committed `e4a75cd6f`; gate raw-string fix `7835c5922`; both in production now.
- Prod run: `2b40d46db` Build & Deploy run 32969766881 SUCCESS (syntax-gate/builds/migrate/deploy all green, 2026-08-26). Health 200 on api /health/live, /health/ready, app. Repo PRIVATE again.
- Compile blocker fixed en route: CS1929 `IReadOnlyList<string>.IndexOf` in ListeningBackfillService.cs:276 (a45a29519) — fixed by sibling agent commit `2b40d46db` "fix(listening): complete replay validation and grading build". Working tree clean.
- GitHub billing note: actions job start was blocked twice by account billing ("recent account payments have failed...") — owner fixed; if runners stop starting again, owner must re-check https://github.com/settings/billing before rerunning.
- Still live 2026-08-26: Listening-100% stream (9694c70bb, ad98fa4b2 history+scripts, 8b6ffc657 device cooldown, a45a29519 scripts/grading warnings, 2b40d46db replay validation).

## Previous — Finish Listening 100% and ship
- Player auto-advance restored: cue-end marks all section extracts, Part B waits for every workplace cue, 0s review hops V2 review then next/submit. Player suite 34/34. Backend listening 36/36. Page/category tests 16/16.
- Local commit `9694c70bb` pushed to origin/main; the listening/auth work listed below was since committed+pushed by sibling agent (ad98fa4b2, 8b6ffc657, a45a29519, 2b40d46db) and is in production — superseded.

## Previous — AI Packages spec 100% conformance sweep
- Audited full OET_AI_Packages spec (A01-A17, 3A/3B) against code: dashboard credits-only UI, Other papers hidden (CandidateVisible), instant webhook fulfilment + idempotency, reopen-free attempts, admin parity all verified implemented.
- FIXED major gap: Quick Check / Exam Prep Pro were seeded/stored as shared_credits (violates spec A03/A04 + master catalogue). Restored flexible_credits 5/15 via seed manifest + website copy + migration 20261001120000_RestoreFlexibleWsMixedPacks (converts live balances + ledger deltas back to Flexible W/S for those packages).
- FIXED minor gaps: admin CreditBucketAdjuster Add/Set-exact toggle; "Gifted Shared AI Credits" labels; payment-return invalidates entitlement+subscription+aiPackageCredits.
- FIXED ship gate: repaired 40 broken pnpm junctions (pnpm install --frozen-lockfile), gate now uses real TS parser for .ts/.tsx, [Fact] regex tightened to column-0 orphans, self-test extended.
- Validation: backend build 0 errors; 56/56 billing tests; ship-gate OK (typescript=yes); payment-return 17/17; ai-credit-summary 4/4; package-list + catalog-website-packages green. Pre-existing failures NOT touched: pdf-policy-release* snapshot tests, 126 repo-wide tsc errors.
- DEPLOYED: 7be86b744 Build & Deploy success; web/api health green; migrations applied; VPS images on this SHA; repo private again.
- PROD VERIFIED: /v1/billing/ai-packages now returns pkg_quick_check/pkg_exam_prep_pro with sharedCredits=0 and "flexible AI grading credits" copy (was sharedCredits 5/15). Balance conversion ran in the same migration batch.

## Previous — Shared wallet screenshot close-out + Master Catalogue merge
- Debit-on-start + screenshot remaining copy now wired: reading exam/paper/practice/parts, listening paper/player, speaking warmup + self-practice, writing V2 eligibility, writing paper direct launch.
- Unlimited L/R comes from live lot flags, not null remaining. Writing V2 eligibility deducts once on `writing-v2:{userId}:{scenarioId}`.
- Catalogue split kept: Shared vs restricted Flexible W/S; dedicated → Flex W/S → Shared; R/L never Flex W/S.
- Next: owner verifies live start toasts and spend priority. Apply SQL-only `20260924`/`20260925` on prod if not applied. Never `.impeccable/`. Never VPS compute.

## Previous — OET 2026 Master Catalogue conformance (IN PROGRESS, local edits not yet committed)
Implemented on working tree (branch main): SharedCredits vs restricted Flexible W/S split (migration 20260906090000 incl. exact W3/8/15 caps + balance reclassification by source package), consumption priority rewrite in AiPackageCreditService (dedicated?FlexWS?Shared; R/L never FlexWS), debit feedback messages + FE announcer (lib/credit-feedback.ts), dashboard CreditBalanceCard, candidate token-surface removal (AiUsageWidget deleted; settings/ai + ai-usage credits-only), unified GET /v1/me/attempts history + /submissions section + sidebar History link, strict Products 1-29 PendingVerification gate (webhook parks plan orders; gateway-receipt rows already feed admin Orders & Payments queue renamed from Payment Proofs; ApproveAsync completes deferred grants incl. IncludedCredits), pkg_* manual-payment submissions blocked + checkout CTAs hidden for AI packages, ContentPaper.CandidateVisible flag (+migration hiding non-series published Reading papers) enforced across reading/listening/generic/media/start routes with admin toggle endpoint, writing/speaking start eligibility gate + profession isolation on CreateAttemptAsync, mock bundle profession fallback removed, legacy cart checkout profession gate, per-user token admin page removed + legacy TokensDelta writes stopped, platform AI/API Usage & Billing retitled with ProviderCapacitySection, admin CreditBucketAdjuster UI wired to adjust endpoint.
Validation NOT run locally per owner instruction (owner will verify). Next: owner runs checks/reports errors; then commit/push via Actions deploy.

# Agent State (local)

## Current task — Antigravity integration DEPLOYED TO PRODUCTION; idle for next task
- **Production runs `451e7bb6c`** (deploy run 32745085131 success, 2026-08-24). Blue/green health gates passed; gateway v0.2 live in degraded standby (no GEMINI_API_KEY on VPS yet — set it in `.env.production` + restart gateway to activate Mode A).
- Two deploy incidents fixed en route (both in `docs/dev/lessons-learned.md`): (1) PS5.1 `Set-Content -Encoding UTF8` wrote a BOM into package.json → pnpm docker build died; (2) "unused import" cleanup removed `get_settings` still used by the production-only `create_app()` no-args path → gateway crash-loop, health gate blocked promote. Regression test added for the no-args path.
- All code phases shipped: v0.2 hardening (`d73c999a8`) + phases 3c–7 (`f6ad1d090`). Gateway pytest 39/39.
- Live state: students on existing providers; gateway Mode A standby; AI Pro Antigravity quota unused in prod (Mode B = owner PC only; Mode C awaits Google — `pnpm ai:flipday-watch`).
- Owner-side ops pending (manual): set GEMINI_API_KEY on VPS → `pnpm ai:parity` → 10% route flips in `/admin/ai-providers` (provider `antigravity-gateway`, model `agent:<name>`) → 100%; desktop clean-PC smoke; mobile device pass (`docs/antigravity/mobile-validation.md`); install `ops/prometheus/alerts-oet-gateway.yml` on VPS monitoring.
- Plain-language status + admin switch guide: `docs/antigravity/README.md` (Current status section). Env gotchas: `docs/dev/lessons-learned.md` (python via agent-gateway venv only, no docker locally, cargo check with space-free CARGO_TARGET_DIR, never Set-Content UTF8 on JSON).

## Previous — Antigravity gateway hardening (v0.2)
- Circuit breaker per route (fast-fail 503 → resolver fallback), turn timeout → 504, constant-time token compare, request caps 413, SSE keepalive, idle-session reaper, lock-safe eviction, graceful drain, accurate usage accounting, opt-in structured outputs (fixed manifest `json.loads(dict)` crash), optional Redis cache, Prometheus `/v1/metrics`, JSON logs.
- Compose dev/desktop/vps/production carry new env knobs; prod/vps got `stop_grace_period: 45s`. Gateway version 0.2.0. Commit `d73c999a8`.

## Previous — production deploy of ff29552c+fix
- Deploy blocked on missing GEMINI_API_KEY: gateway crashed in lifespan so routers did not flip.
- Gateway now degrades (healthz HTTP 200, auth_ready=false) so web/API can promote without a Gemini key.


## Previous — Auth/OTP mail isolated from marketing unsubscribe
- Root cause of missing password-reset OTP: Brevo accepted `/smtp/email` then blocked as unsubscribed. Marketing unsubscribe must never gate OTP.
- Code: four From lanes (`auth@`, `updates@`, `no-reply@`, `support@`). Webhook `unsubscribed` writes `__marketing__` only (never `EventKey=null`). Reputation events write `__non_auth__`. Admin inspect/unblock is one email; keeps marketing opt-out; never mass-unblock.
- Validation: `dotnet test --filter FullyQualifiedName~EmailLaneAndMailboxTests` 5/5. Shipped `dc92bbcc` via Actions `32652938029` (success). Repo private again.
- Next: owner unblocks only `drhagermurad2026@gmail.com` and `mindreader420123@gmail.com` in Admin → Notifications → Transactional Mailbox. In Brevo console confirm marketing unsubscribe does not add Transactional Blocklist, and OTP templates stay transactional From `auth@`.

## Previous — Mobile OTP auto-rotation on resume (FIXED + DEPLOYED)
- Fix commit `c512a4e4` deployed to production 2026-08-23 via rerun of Actions `32640038373`. Prod probe: /verify-email returns 200.
- No fresh app release needed: Capacitor `server.url` is remote-only.

## Goal
Continue official OET Reading uploads on production for **oetwebapp**.
Publish live. Same five book folders for Full Exam and Part A/B/C.

## Read first
1. `docs/READING-UPLOAD-ZERO-DEVIATION-CONTRACT.md`
2. `docs/READING-UPLOAD-AGENT-HANDOFF.md`
3. `docs/READING-MODULE-SAVE-AND-UPLOAD.md`
4. `docs/PRODUCTION-DATA-PERSISTENCE.md`

## Already live — do not re-import
- 80 published Reading papers (Jayden 01–05, AH 01–03, Atlas 01–26+Kaplan, Nova 01–20, VD 01–05/07–25, `reading-sample-1`)
- Atlas 09 Head injuries is 20/6/8 (no C2). Full exam starts. Scoring /42.
- Candidate Part B/C visible (`fdcc4776`): one Part A/B/C booklet on the left; Part B questions separate on the right.

## Next step
Idle unless the owner sends Atlas 09 C2 or asks to import Desktop extra `Reading -1.pdf`. Never invent C2. Never publish VD6. Never `down -v`.

## Persistence
Named volumes `oetwebsite_oet_*` are independent of containers. Compose pins them `external: true`. Host wrapper `/usr/local/bin/docker` blocks volume rm/prune and `compose down -v`. Content is deleted only from the admin UI. Deploy only recreates web/API slots.

## Constraints
- Public API only. No local API/DB. No `--dev-auth` on Production.
- Do not retry `admin@oet-prep.dev` or bootstrap passwords.
- GitHub Actions for deploys. Make the repo public for the run, then private again. Never leave it public. No VPS compute. No green recreate.
- Not DMB.

## Current task — Writing AI grading & Model Answer final implementation — SHIPPED + LIVE
- Commit 1b1210cd1 on origin/main; Build & Deploy SUCCESS; live web/api-ready (migrations ok)/api-live green; images on 1b1210cd1; repo PRIVATE again.
- Canonical grading inputs enforced at publish (case notes + exact task + rulebook + approved Model Answer); prep endpoints preparation-status / extract-from-pdf / generate-missing + CSV runbook; grading uses stored snapshots only (no live OCR).
- Submit always available incl. empty (deterministic zero grade, 0 provider calls, 0 credit hold); no exemplar-similarity scoring (canary test); LT-* pack/rule bridge ToPackLetterType.
- FINAL MASTER v1.0: 26 overridden legacy rule bodies corrected verbatim in all 13 writing JSONs (v1.0.1); 6 legacy detectors neutralized (.NET+TS); parity fixtures updated.
- One submit = one job: stable client key + server content-hash guard + claim/reuse/reservation; 429/409 single retry; retry-grade resume + grading-page retry card.
- Validation: backend writing+rulebook selection 213 pass (3 pre-existing HEAD failures proven identical on pristine HEAD worktree); frontend lib/writing+lib/rulebook pass except 6 pre-existing pdf-policy-release snapshot failures; tsc clean; lint 0 errors; ship:gate OK.
- Preserved uncommitted catalogue-track work in tree (analytics/focus/showcase pages, prompt templates, pathway services, builder files, CriticalFlowsTests, writing-catalogue-revision artifacts, *.txt logs) — not staged.
- OPEN (need prod data/admin): run preparation-status CSV → backfill case notes/task text → generate-missing → approve packs + model answers; profession-specific OW-edition rulebook sync; live 10-way parallel submit check. Details: artifacts/writing-ai-final/.

## Current task — Capacitor reload storm + device-limit OTP storm — SHIPPED + LIVE
- Commit d9c7f93e7 on origin/main; Build & Deploy SUCCESS; live web/api-ready (migrations ok)/api-live green; green images on d9c7f93e7; repo PRIVATE again. Blue (1b1210cd1) still up = rollback path.
- Fixes: single-flight hard auth navigation (lib/navigation/auth-redirect.ts, wired into auth-client sign-in redirect + api.ts email gate); SW registration blocked in native shells (providers.tsx runtime-kind guard; notification-worker guarded too); resend single-flight ref in device-challenge-form; backend device-OTP save-before-send + SentAt==null recovery (EmailOtpService).
- Validation: tsc clean; lint 0 errors; ship:gate OK; frontend 43 pass (incl. 6 new); backend OTP 6/6 + device/Firebase/email 34/34. CI: Build&Deploy+Tauri+SBOM green; Mobile CI Android build+emulator smoke+unit+lint green (iOS sim launch blocked by GH billing, infra); Speaking CI + QA Smoke failures proven pre-existing on pristine parent worktree (dashboard-shell + 2 backend speaking tests).
- Prod E2E (this session): admin login 200; 10x parallel /me all 200; fresh learner hit device_verification_required otp_required at 1/2 slots; 3x concurrent send-otp → SAME challengeId (no storm); wrong code → invalid_otp_code; test user hard-deleted (24 rows/9 tables). Valid-OTP acceptance on prod BLOCKED (no inbox access); covered by backend acceptance tests on identical SHA. E2E temp secrets scrubbed from $env:TEMP.
- Note for next agent: curl.exe from PowerShell mangles inline -d JSON (empty-400 symptom) — always use --data-binary @file. Direct api.* POSTs 400 on public Host (allowlist is internal-only) — drive E2E via app.oetwithdrhesham.co.uk/api/backend with Origin/Referer.
