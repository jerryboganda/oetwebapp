# QA Master Report

## Scope
- Date: 2026-03-29
- Workspace: `C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App`
- Frontend target: `http://localhost:3000`
- API target: `http://localhost:5198`
- App areas audited: auth, learner, expert, admin, shared layout/auth guards, accessibility, cross-browser smoke, responsive learner smoke, locale/timezone learner smoke, learner/expert/admin detail deep links, learner/expert/admin targeted interaction workflows, immersive learner completion flows, backend auth stability, CI smoke workflow readiness

## App Overview
- Protected multi-role OET preparation platform with learner, expert, and admin workspaces
- Frontend: Next.js 15 App Router, React 19, TypeScript, Tailwind 4, Lucide, Motion, Recharts
- Backend: ASP.NET Core 10, EF Core, PostgreSQL
- Auth/session: client-side auth guard plus backend role enforcement, local/session storage token persistence, privileged MFA setup branch for expert/admin users

## What Was Audited
- Authentication entry, protected-route redirects, password recovery screens, verify-email error handling
- Learner core workspace routes:
  - `/`
  - `/study-plan`
  - `/progress`
  - `/readiness`
  - `/reading`
  - `/listening`
  - `/writing`
  - `/speaking`
  - `/submissions`
  - `/settings/profile`
  - `/billing`
  - `/mocks`
  - `/diagnostic`
- Learner deep-link/detail smoke:
  - `/reading/player/rt-001`
  - `/mocks/report/mock-report-001`
- Learner interaction and completion workflow coverage:
  - reading player answer persistence across next/previous navigation
  - reading player review-flag persistence across navigation
  - listening player answer completion across question types with submit to results
  - writing player autosave, leave-warning dialog, submit-confirm dialog, and result redirect
  - speaking self-practice recording pause/resume, stop/submit dialogs, and result redirect
- Expert workspace route smoke:
  - `/expert`
  - `/expert/queue`
  - `/expert/calibration`
  - `/expert/metrics`
  - `/expert/schedule`
  - `/expert/learners`
  - `/expert/learners/mock-user-001`
  - `/expert/review/writing/<disposable-review-request>`
  - `/expert/review/speaking/<disposable-review-request>`
  - `/mfa/setup?next=/expert`
- Expert interaction workflow coverage:
  - writing review rubric completion, validation, draft save via `Ctrl/Cmd+S`, final submit via `Ctrl/Cmd+Enter`
  - writing review rework validation plus successful rework request submission
  - speaking review tab navigation, rubric completion, and successful final submission
- Admin workspace route smoke:
  - `/admin`
  - `/admin/content`
  - `/admin/content/lt-001`
  - `/admin/criteria`
  - `/admin/taxonomy`
  - `/admin/flags`
  - `/admin/users`
  - `/admin/users/mock-user-001`
  - `/admin/billing`
  - `/admin/audit-logs`
  - `/mfa/setup?next=/admin`
- Admin interaction workflow coverage:
  - disposable content creation from `/admin/content/new`
  - draft save, publish, and revision-history navigation
  - audit-log search, filter, detail drawer inspection, keyboard close, focus restore, and CSV export
  - admin user credit adjustment, password reset trigger, suspend/reactivate path when available
- Role-protection behavior:
  - learner blocked from expert/admin
  - expert blocked from admin
  - admin blocked from expert
- Accessibility smoke:
  - `/sign-in`
  - `/`
  - `/settings/profile`
  - `/expert/queue`
  - `/admin/content`
  - `/admin/users/mock-user-001`
  - `/admin/audit-logs`
  - `/writing/player?taskId=wt-001`
  - `/speaking/task/st-001?mode=self`
  - `/speaking/results/<generated-id>`
- Shared runtime quality:
  - console errors
  - page errors
  - request failures
  - 5xx responses during Playwright runs
- Responsive learner smoke:
  - Pixel 7
  - iPhone 14
- Cross-browser smoke:
  - Chromium
  - Firefox
  - WebKit
- Locale/timezone smoke:
  - `Australia/Sydney` learner project
- Backend regression:
  - full .NET test suite
  - targeted MFA endpoint regression

## Test Environment and Commands
- Playwright browsers installed via `npm.cmd run test:e2e:install`
- Auth storage bootstrap via `npm.cmd run test:e2e:auth`
- Baseline full frontend E2E pass before deep-workflow expansion:
  - `npm.cmd run test:e2e`
  - Result: `150 passed, 412 skipped, 0 failed`
- Targeted detail-route expansion pass:
  - `npm.cmd run test:e2e -- tests/e2e/learner/deep-link-smoke.spec.ts tests/e2e/expert/detail-smoke.spec.ts tests/e2e/admin/detail-smoke.spec.ts`
  - Result: `30 passed, 64 skipped, 0 failed`
- Targeted expert completion pass:
  - `npm.cmd run test:e2e -- --project=chromium-expert tests/e2e/expert/review-completion.spec.ts`
  - Result: `6 passed, 0 failed`
- Targeted admin deep-mutation pass:
  - `npm.cmd run test:e2e -- --project=chromium-admin tests/e2e/admin/admin-deep-mutations.spec.ts`
  - Result: `6 passed, 0 failed`
- Targeted learner immersive-completion pass:
  - `npm.cmd run test:e2e -- --project=chromium-learner tests/e2e/learner/immersive-completion.spec.ts`
  - Result: `6 passed, 0 failed`
- Cross-browser expert regression after disposable-review stabilization:
  - `npm.cmd run test:e2e -- --project=chromium-expert --project=firefox-expert --project=webkit-expert tests/e2e/expert/detail-smoke.spec.ts tests/e2e/expert/review-workflows.spec.ts`
  - Result: `14 passed, 4 skipped, 0 failed`
- Shared modal regression:
  - `npm.cmd run test -- components/ui/__tests__/modal.test.tsx`
  - Result: `2 passed, 0 failed`
- Final post-remediation smoke rerun:
  - `npm.cmd run test:e2e:smoke`
  - Result: `175 passed, 478 skipped, 0 failed`
- Backend pass:
  - `dotnet test backend/OetLearner.sln --configuration Release --no-logo`
  - Result: `111 passed, 0 failed`
- Targeted backend MFA validation:
  - `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~AuthEndpoints_BeginAndConfirmAuthenticatorSetup_EnableMfaForVerifiedExpert"`
- CI smoke workflow added:
  - `.github/workflows/qa-smoke.yml`
  - Runs backend tests, boots the Docker smoke stack, executes `npm run test:e2e:smoke`, and uploads Playwright artifacts
- Local stack rebuilds used during remediation:
  - `docker compose -f docker-compose.desktop.yml up -d --build web`
  - `docker compose -f docker-compose.desktop.yml up -d --build learner-api`
  - `docker compose -f docker-compose.desktop.yml up -d --build web learner-api`

## Key Findings

### Resolved high-severity defects
1. Privileged MFA setup bootstrap could return HTTP 500 during concurrent cross-browser auth setup.
2. Authenticated expert/admin routing and role-protection behavior were inconsistent enough to risk incorrect landing/guard behavior.
3. Critical or serious accessibility defects existed on audited sign-in, learner, expert, and admin screens.

### Resolved medium-severity defects
1. API-supplied learner deep links still pointed to stale legacy routes.
2. The standalone Docker image omitted `public/`, causing `manifest.json` failures and browser instability.
3. Submission history surfaced raw ISO timestamps instead of readable dates.
4. Admin credit-adjustment modal dropped keyboard focus to `body` when dismissed.
5. The seeded listening completion flow referenced a missing playable media asset, breaking local immersive listening completion.
6. The admin audit-log drawer did not reliably restore focus to the invoking row after keyboard dismissal.

### Resolved low-severity defects
1. Firefox navigation-abort noise created false-positive diagnostics failures.
2. Expert review keyboard shortcuts depended on case-sensitive key matching and were not reliable across environments.

### QA infrastructure uplift
1. Repo-owned Playwright system added with browser/device/timezone matrix.
2. Deterministic learner/expert/admin auth bootstrap added through real sign-in flows.
3. Console/page/network/5xx diagnostics now attach to E2E results.
4. Firefox navigation-abort noise is filtered correctly so the release gate stays strict without false failures.
5. Detail deep-link smoke coverage now exists for learner immersive/report entry points, expert review/detail routes, and admin detail routes.
6. Chromium deep workflow coverage now exists for:
   - learner reading/listening/writing/speaking completion or stateful in-session behavior
   - expert writing/speaking review completion plus rework
   - admin content publish/revisions, audit-log drawer/export, and user mutations
7. Dedicated QA documentation scaffold added under `docs/qa/`.
8. A repo-owned GitHub Actions smoke workflow now exists for backend plus Playwright smoke checks.

## Fixes Applied
- Added Playwright config, scripts, auth bootstrap, smoke suite, role-guard suite, and accessibility suite.
- Hardened post-auth destination resolution for learner/expert/admin users and privileged MFA routing.
- Added role requirements to privileged layouts and redirect hooks.
- Rewrote stale backend-provided learner route mappings to current route structure.
- Copied `public/` into the standalone Docker runner image.
- Fixed submission-history date rendering.
- Fixed accessibility blockers:
  - unnamed progress indicators
  - mobile home link without accessible name
  - low-contrast sign-in/dashboard/expert metadata text
- Fixed admin modal/drawer focus restoration:
  - shared modal/drawer restore logic now runs on the actual open-to-closed transition
  - admin user credit modal now closes through an explicit trigger-restore path
  - admin audit-log drawer now restores focus to the originating row
- Fixed backend MFA setup concurrency behavior in `AuthService`.
- Added a generated fallback listening-media route for the seeded `lt-001` task so the local immersive listening flow is playable and submittable.
- Normalized expert review shortcut handling so `Ctrl/Cmd+S` and `Ctrl/Cmd+Enter` work reliably in writing and speaking review workspaces.
- Added a visible `Performance Summary` heading and intro block to the speaking results page to strengthen result-surface semantics and testability.

## Validation Summary
- Baseline full Playwright suite passed after the first remediation wave.
- Expanded learner/expert/admin detail-route smoke coverage passed.
- Expert review completion and rework workflows passed in Chromium.
- Admin deep publish/export/user-mutation workflows passed in Chromium.
- Learner immersive listening/writing/speaking completion workflows passed in Chromium.
- Shared modal focus-regression tests passed.
- Full smoke matrix remained green after the deep-workflow fixes.
- Full backend test suite passed after remediation.
- No known critical or high defects remain from the exercised surface area.

## Evidence Locations
- Playwright HTML report: `output/playwright/report/index.html`
- Playwright raw artifacts: `output/playwright/test-results/`
- Example screenshot artifact: `output/playwright/settings-profile-headless.png`

## Residual Risks
- This is not evidence that the application is bug-free.
- Deep mutation coverage is now materially stronger, but most deep workflows still run only in Chromium; Firefox/WebKit remain smoke-level for those areas.
- Not every admin criteria/taxonomy/flags/billing mutation path is automated end-to-end.
- Mock-player and diagnostic immersive completion paths remain lighter than the audited learner reading/listening/writing/speaking flows.
- Automated accessibility coverage is meaningful, but real assistive-technology runs were not performed from this workspace.
- The new GitHub Actions smoke workflow was added in-repo, but it was not observed running on GitHub during this local audit.

## Assistive-Tech Handoff
- Required human signoff still pending for:
  - NVDA on Windows: sign-in, learner immersive flows, expert review completion, admin audit-log drawer/export, admin user credit modal
  - VoiceOver on macOS or iOS: learner dashboard, learner settings/profile, and one immersive learner flow
- The execution checklist and evidence template are in `docs/qa/accessibility-report.md`.

## GitHub Workflow Observation Gate
- The local repo now contains `.github/workflows/qa-smoke.yml`.
- This workspace cannot truthfully complete hosted GitHub observation because no connected PR/run context is available here.
- Release handoff still requires:
  1. one successful GitHub-hosted `QA Smoke` run on a branch or PR
  2. captured run URL
  3. confirmation that `playwright-smoke-artifacts` uploaded successfully

## Release Recommendation
- Recommendation: `Conditional Go`
- Rationale:
  - no known critical defects remain
  - no known high-severity defects remain
  - all reproduced medium defects found during this audit were fixed
  - remaining concerns are coverage-depth and external handoff risks, not known current release blockers
- Recommended release mode:
  - staged or controlled release
  - keep the Playwright smoke suite and backend tests as required pre-release checks
  - complete the documented manual assistive-tech signoff and GitHub-hosted smoke observation before final release signoff
