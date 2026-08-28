# Agent State (local)

## Current task — Listening Part B/C release-blocking fixes — DEPLOYED + LIVE / QA BOUNDARIES OPEN
- Implemented shared, fail-closed source-stem normalization for Listening B/C across backend authoring, backfill, learner DTO construction, structure validation, admin answer-sheet import, standalone player, and strict full-exam grouping. Bare/malformed part codes are recovered from canonical printed question numbers; generic headings/sentinels are rejected rather than rendered as stems.
- Restored one verified source paper (`77114cbc020347858619a88928ed0e32`, `Benchmark Listeninig Tests.pdf` Practice Test 1) through migration `20261129000000_RestoreListeningPartBCSourceStems.cs`; migration also strips option/document artifacts and clears invalid unknown-paper B/C stems. No live database migration has been run in this checkout.
- Full Exam Part B groups six deterministic questions Q25–Q30. Part C now exposes one candidate question workspace containing Q31–Q42 across C1/C2, while the two extracts still play sequentially and once; standalone A/B/C and full-exam audio use source-ready one-shot autoplay with a browser-policy fallback, without restarting on normal question navigation.
- This release patch adds cross-extract Part C jump/answer persistence in both full-exam renderers and permits answer writes for either C extract only while Part C is active; the server section cursor remains forward-only for the audio/submission contract.
- Validation: API rebuild 0 errors; focused backend Listening data/structure/authoring/learner suite 136/136 plus 98 no-build source/structure/sanitization/manifest tests; changed frontend Vitest slice 76/76 plus 26/26 legacy-player tests (37/37 combined focused player/page/navigation slice); targeted ESLint 0 errors (22 existing warnings); `pnpm run ship:gate` passed; `git diff --check` has no whitespace errors apart from normal CRLF notices.
- Test commit `758394a015cb12d8c0b9e47259a281b99a48fcc0` adds real legacy-player coverage for standalone A/B/C autoplay, full-exam B Q25–Q30, full-exam C Q31–Q42 across C1/C2, jump/Next navigation, answer persistence, and one-shot playback.
- Deployed SHA `758394a015cb12d8c0b9e47259a281b99a48fcc0` via Build & Deploy run `33173351228`; live web, API ready/live, database/migrations/storage checks, and blue image tags are healthy; unauthenticated policy endpoint confirms Part B=6 and Part C=12; repository is private again. Acceptance boundaries remain explicit: no live authenticated Atlas/Nova API/database corpus audit, authenticated desktop/tablet/mobile browser playback evidence, or production candidate-flow proof; the local source PDF inventory is authoritative only where paper identity is proven, and intentionally partial source papers remain fail-closed. Preserve unrelated AI-control-plane worktree changes.
- Latest continuation before redeploy: fixed the offline-answer reconciliation TypeScript narrowing in `app/listening/player/[id]/page.tsx` and completed the required DTO fields in the focused Part B/C navigation fixture. Validation now passes: focused frontend Listening slice 37/37, backend source/structure/sanitization/manifest slice 98/98, targeted ESLint 0 errors (16 existing warnings), targeted root Listening `tsc` errors absent, and `pnpm run ship:gate` OK. Repository-wide QA Smoke run `33174484937` remains red from unrelated existing errors/environment failures; it is not evidence against the scoped fix. This closure patch is pending explicit commit/deploy. Acceptance boundaries are unchanged: no authorized live Atlas/Nova learner session or desktop/tablet/mobile browser playback evidence, and no canonical Atlas/Nova paper-to-source mapping in this checkout.


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
