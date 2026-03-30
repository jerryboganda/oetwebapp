# Defect Log

## Summary
- Open critical defects: `0 known`
- Open high defects: `0 known`
- Open medium defects: `0 known reproduced`
- Open low defects: `0 known reproduced`
- Residual release risk comes from cross-browser deep-mutation parity, manual assistive-tech execution, and the still-unobserved GitHub workflow execution, not from currently reproduced unresolved blockers.

---

## QA-001: Privileged MFA setup bootstrap returned HTTP 500
- Severity: High
- Area: Authentication, backend MFA, expert/admin sign-in setup
- Repro steps:
  1. Bootstrap expert/admin auth states across multiple Playwright browser projects.
  2. Navigate to `/mfa/setup?next=/expert` or `/mfa/setup?next=/admin`.
  3. Trigger authenticator setup begin flow under repeated or concurrent seeded-account use.
- Expected result:
  - Backend returns a successful authenticator setup payload every time.
- Actual result:
  - `/v1/auth/mfa/authenticator/begin` intermittently returned `500`.
- Evidence location:
  - `output/playwright/report/index.html`
  - `output/playwright/test-results/expert-privileged-smoke-*`
- Root cause notes:
  - Recovery-code replacement logic in backend MFA setup was not robust under repeated seeded-user setup activity across browser projects.
- Fix status:
  - Resolved
- Fix applied:
  - `backend/src/OetLearner.Api/Services/AuthService.cs`
  - Added provider-safe deletion logic for recovery codes using `ExecuteDeleteAsync` for relational providers and tracked `RemoveRange` for in-memory test provider compatibility.
- Regression coverage:
  - `tests/e2e/setup/auth.setup.ts`
  - `tests/e2e/expert/privileged-smoke.spec.ts`
  - `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~AuthEndpoints_BeginAndConfirmAuthenticatorSetup_EnableMfaForVerifiedExpert"`

---

## QA-002: Privileged post-auth routing and role guards were inconsistent
- Severity: High
- Area: Authentication, authorization, client routing
- Repro steps:
  1. Sign in as learner, expert, or admin.
  2. Attempt protected-route access across role boundaries and post-auth handoff paths.
  3. Observe privileged users who require MFA or users crossing into routes outside their role.
- Expected result:
  - Learner lands in learner workspace only.
  - Expert/admin users without authenticator are routed into MFA setup before workspace entry.
  - Cross-role navigation redirects to the correct workspace.
- Actual result:
  - Routing and protection behavior was not fully consistent across sign-in flow and layout/hook enforcement.
- Evidence location:
  - `output/playwright/report/index.html`
  - `output/playwright/test-results/shared-role-guards-*`
- Root cause notes:
  - Post-auth destination resolution and layout-level role enforcement were not fully centralized.
- Fix status:
  - Resolved
- Fix applied:
  - `lib/auth-routes.ts`
  - `components/auth/sign-in-form.tsx`
  - `app/(auth)/sign-in/page.tsx`
  - `app/(auth)/verify-email/page.tsx`
  - `components/layout/app-shell.tsx`
  - `app/admin/layout.tsx`
  - `app/expert/layout.tsx`
  - `lib/hooks/use-admin-auth.ts`
  - `lib/hooks/use-expert-auth.ts`
- Regression coverage:
  - `tests/e2e/shared/role-guards.spec.ts`
  - `tests/e2e/expert/privileged-smoke.spec.ts`
  - `app/(auth)/sign-in/page.test.tsx`

---

## QA-003: Learner API deep links pointed to stale legacy routes
- Severity: Medium
- Area: Learner navigation, API route normalization
- Repro steps:
  1. Load learner workspace data containing backend-provided navigation targets.
  2. Follow legacy route strings such as historic `/app/...` paths.
- Expected result:
  - Learner deep links open current Next.js routes.
- Actual result:
  - Some API links still referenced legacy route shapes that no longer matched the frontend.
- Evidence location:
  - Unit and route regression evidence rather than a single visual artifact
- Root cause notes:
  - The client normalization layer was incomplete for several learner route families.
- Fix status:
  - Resolved
- Fix applied:
  - `lib/api.ts`
  - Added `rewriteLegacyLearnerRoute` and speaking/listening/reading/writing route normalization coverage.
- Regression coverage:
  - `lib/__tests__/api.test.ts`
  - `tests/e2e/learner/learner-smoke.spec.ts`

---

## QA-004: Standalone Docker web image omitted public assets
- Severity: Medium
- Area: Deployment/runtime, browser compatibility
- Repro steps:
  1. Build and run the standalone Docker web image.
  2. Load the app and inspect asset requests such as `/manifest.json`.
- Expected result:
  - Public assets resolve successfully in the production-style container.
- Actual result:
  - `manifest.json` requests failed because `public/` was not copied into the runner image.
- Evidence location:
  - `output/playwright/report/index.html`
- Root cause notes:
  - The runner stage copied standalone server output and static assets, but not the `public/` directory.
- Fix status:
  - Resolved
- Fix applied:
  - `Dockerfile`
  - Added `COPY --from=builder /app/public ./public`
- Regression coverage:
  - Cross-browser Playwright smoke reruns on WebKit and Firefox
  - Local rebuilt container verification

---

## QA-005: Submission history displayed raw ISO timestamps
- Severity: Medium
- Area: Learner submission history
- Repro steps:
  1. Sign in as learner.
  2. Open `/submissions`.
  3. Inspect attempt timestamps.
- Expected result:
  - Human-readable date/time labels.
- Actual result:
  - Raw ISO strings were visible in the UI.
- Evidence location:
  - Learner smoke assertion history in Playwright and page-level frontend tests
- Root cause notes:
  - Submission date formatting was not normalized before rendering.
- Fix status:
  - Resolved
- Fix applied:
  - `app/submissions/page.tsx`
- Regression coverage:
  - `app/submissions/page.test.tsx`
  - `tests/e2e/learner/learner-smoke.spec.ts`

---

## QA-006: Critical/serious accessibility defects on audited key pages
- Severity: High
- Area: Accessibility, design system, shared navigation
- Repro steps:
  1. Run axe and keyboard smoke across sign-in, learner dashboard, learner settings/profile, expert queue, and admin content.
  2. Inspect accessibility tree, contrast, focusable controls, and landmark names.
- Expected result:
  - No critical/serious axe violations on audited pages and no obvious keyboard/name blockers.
- Actual result:
  - Audited pages initially exposed a mix of critical/serious defects:
    - progress indicators without accessible names
    - mobile home link without accessible name
    - low-contrast text in critical flows
- Evidence location:
  - `output/playwright/report/index.html`
  - `output/playwright/settings-profile-headless.png`
- Root cause notes:
  - Shared components lacked explicit ARIA naming in some cases and some text colors fell below acceptable contrast in real browser rendering.
- Fix status:
  - Resolved
- Fix applied:
  - `components/ui/progress.tsx`
  - `components/layout/top-nav.tsx`
  - `components/domain/expert-surface.tsx`
  - `components/auth/sign-in-form.tsx`
  - `components/layout/sidebar.tsx`
  - `app/page.tsx`
- Regression coverage:
  - `tests/e2e/shared/accessibility.spec.ts`
  - `components/ui/__tests__/progress.test.tsx`

---

## QA-007: Firefox reload abort noise caused false Playwright failures
- Severity: Low
- Area: QA harness, cross-browser diagnostics
- Repro steps:
  1. Run learner smoke in Firefox.
  2. Reload the learner dashboard during diagnostics capture.
  3. Observe aborted RSC-prefetch requests reported as failures.
- Expected result:
  - Browser-aborted navigation/prefetch noise should not fail the diagnostics gate.
- Actual result:
  - Firefox emitted `NS_BINDING_ABORTED` for harmless cancelled requests and the diagnostics fixture treated them as product failures.
- Evidence location:
  - `output/playwright/test-results/learner-learner-smoke-Lear-6067e-d-session-survives-a-reload-firefox-learner/`
- Root cause notes:
  - The diagnostics fixture ignored Chromium `ERR_ABORTED` noise but not Firefox `NS_BINDING_ABORTED`.
- Fix status:
  - Resolved
- Fix applied:
  - `tests/e2e/fixtures/diagnostics.ts`
- Regression coverage:
  - `lib/__tests__/diagnostics-fixture.test.ts`
  - targeted Firefox Playwright rerun

---

## QA-008: Admin credit-adjustment modal failed to restore keyboard focus on close
- Severity: Medium
- Area: Accessibility, admin user detail, shared modal behavior
- Repro steps:
  1. Sign in as admin.
  2. Open `/admin/users/mock-user-001`.
  3. Open the `Adjust Credits` modal.
  4. Dismiss it with `Escape`.
- Expected result:
  - Focus returns to the `Adjust Credits` trigger so keyboard users remain anchored in context.
- Actual result:
  - Focus dropped to `body`, leaving no meaningful active element after the dialog closed.
- Evidence location:
  - `output/playwright/report/index.html`
  - `output/playwright/test-results/admin-admin-workflows-*`
- Root cause notes:
  - Focus restoration relied on modal cleanup timing that did not reliably fire a real-browser restore event after dismissal.
- Fix status:
  - Resolved
- Fix applied:
  - `components/ui/modal.tsx`
  - `app/admin/users/[id]/page.tsx`
  - Moved shared modal/drawer focus restore to an explicit `open -> closed` transition and routed the admin credit modal through an explicit close-and-restore helper.
- Regression coverage:
  - `components/ui/__tests__/modal.test.tsx`
  - `tests/e2e/admin/admin-workflows.spec.ts`

---

## QA-009: Seeded immersive listening flow referenced a missing playable media asset
- Severity: Medium
- Area: Learner immersive listening workflow, local runtime media
- Repro steps:
  1. Sign in as learner.
  2. Open `/listening/player/lt-001`.
  3. Start the task in the local seeded stack.
- Expected result:
  - The listening task audio plays and the learner can complete and submit the task.
- Actual result:
  - The browser failed the media source with `404` / unsupported-source behavior, breaking local completion of the immersive listening flow.
- Evidence location:
  - `output/playwright/report/index.html`
  - `output/playwright/test-results/learner-immersive-completion-*`
- Root cause notes:
  - The seeded task referenced `/media/listening/lt-001.mp3`, but the local runtime had no matching playable asset in the frontend environment.
- Fix status:
  - Resolved
- Fix applied:
  - `app/media/listening/[asset]/route.ts`
  - Added a generated fallback WAV response for the seeded `lt-001.mp3` route so the local immersive listening flow remains operational without adding test-only APIs.
- Regression coverage:
  - `tests/e2e/learner/immersive-completion.spec.ts`

---

## QA-010: Admin audit-log drawer failed to restore focus to the invoking row
- Severity: Medium
- Area: Accessibility, admin audit logs, drawer dismissal
- Repro steps:
  1. Sign in as admin.
  2. Open `/admin/audit-logs`.
  3. Focus an event row and open the detail drawer with the keyboard.
  4. Dismiss the drawer with `Escape`.
- Expected result:
  - Focus returns to the same audit-log row that launched the drawer.
- Actual result:
  - Focus restoration was not reliable because the drawer closed without reacquiring the invoking row as the active target.
- Evidence location:
  - `output/playwright/report/index.html`
  - `output/playwright/test-results/admin-admin-deep-mutations-*`
- Root cause notes:
  - Generic drawer cleanup did not preserve enough row identity to restore focus to the exact invoking audit-log row after dismissal.
- Fix status:
  - Resolved
- Fix applied:
  - `components/ui/data-table.tsx`
  - `app/admin/audit-logs/page.tsx`
  - Added stable row identifiers and explicit close-time focus restoration back to the originating row.
- Regression coverage:
  - `components/ui/__tests__/modal.test.tsx`
  - `tests/e2e/admin/admin-deep-mutations.spec.ts`

---

## QA-011: Expert review keyboard shortcuts were unreliable because key handling was case-sensitive
- Severity: Low
- Area: Expert review productivity and keyboard interaction
- Repro steps:
  1. Sign in as expert.
  2. Open a writing or speaking review workspace.
  3. Use `Ctrl/Cmd+S` to save a draft or `Ctrl/Cmd+Enter` to submit.
- Expected result:
  - The documented keyboard shortcuts reliably trigger the intended save or submit action.
- Actual result:
  - Shortcut handling depended on raw `event.key` matching and could fail depending on platform/browser key casing.
- Evidence location:
  - `output/playwright/report/index.html`
  - `output/playwright/test-results/expert-review-completion-*`
- Root cause notes:
  - Keyboard shortcut handlers compared `event.key` without normalizing case before checking `s` or `enter`.
- Fix status:
  - Resolved
- Fix applied:
  - `app/expert/review/writing/[reviewRequestId]/page.tsx`
  - `app/expert/review/speaking/[reviewRequestId]/page.tsx`
  - Normalized `event.key` to lowercase before shortcut evaluation.
- Regression coverage:
  - `tests/e2e/expert/review-completion.spec.ts`
