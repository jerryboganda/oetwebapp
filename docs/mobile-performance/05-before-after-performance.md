# Before / After — Measured Evidence

## Environment boundary

This session had **no physical Android/iOS device, no emulator, no Android SDK/JDK, and no browser** available (`java`/`ANDROID_HOME` unresolved; confirmed by direct check). All claims below are either (a) reproducible static/type/lint/unit-test evidence gathered in this session, or (b) explicitly marked `NOT MEASURABLE IN CURRENT ENVIRONMENT`. No on-device timing, frame-rate, or Lighthouse/Chrome-profiler number is fabricated anywhere in this document or the final report.

## Code-verifiable metrics

| Metric | Before | After | Change | Evidence |
|---|---|---|---|---|
| Dashboard cold-open API requests | 13 (9 dashboard + 2 duplicate entitlement/streak-adjacent + 2 feature flags), `/v1/me/entitlement-snapshot` fetched **twice** | 12 (entitlement deduped to 1 shared TanStack Query fetch) | −1 duplicate request per cold Dashboard load | `04-dashboard-performance-analysis.md` §API audit; `hooks/use-enabled-modules.ts` diff |
| Requests fired on **every** learner navigation (not just Dashboard) | +2 uncached (`fetchStreak`, `fetchXP` in `LearnerStreakBadges`, refired on every `TopNav` remount) | +0 (React-Query-cached, 60s staleTime; no refetch on remount while fresh) | −2 requests per tap, steady state | `learner-streak-badges.tsx` diff; `03-global-performance-analysis.md` §D.1 |
| Background refetch on Dashboard revisit within 30–60s (entitlement/subscription/ai-credits/scoring-policy) | Always refetches (staleTime 30–60s, shorter than a typical "leave and come back" gap) | Skipped while cached data is < 2–5 min old; correctness preserved via existing explicit invalidation on the mutations that actually change this data | 4 fewer background requests on a typical warm revisit | `04-dashboard-performance-analysis.md` §Root causes 2 |
| `tsc --noEmit` errors introduced by this work | — | 0 (3 pre-existing errors, confirmed unrelated — see below) | No regression | Full repo `tsc --noEmit` run, this session |
| `eslint` errors/warnings introduced by this work | — | 0 new (465 pre-existing warnings, 0 errors, unrelated files) | No regression | Full repo `pnpm run lint` run, this session |
| Vitest — touched-area suites | — | 25/25 passing (`app-shell`, `learner-dashboard-shell`, `sidebar-route-matching`, `feature-flag-nav`, `top-nav` [new], `use-dashboard-home`, `mobile-runtime`, `lib/query/hooks`) | No regression, 1 new regression-guard test | This session's `vitest run` output |
| Vitest — broader `renderWithRouter`-based suite (59 files, 217 tests) | — | 203/217 passing; 14 failing tests in 2 files, **proven pre-existing** (identical failure reproduced on the pristine `git stash`-restored baseline before this session's changes) | No regression from this work | See "Pre-existing vs. introduced" below |
| New top-nav header regression test | — | Fails against the pre-fix header structure (fixed height + safe-area padding on one element), passes against the fix | Confirms the test is meaningful, not vacuous | Verified by temporarily `git stash`-ing `top-nav.tsx` and re-running the test in this session |

## Pre-existing vs. introduced — verification method

Two failures surfaced in `app/page.test.tsx` (and one in the unrelated `app/get-app/page.test.tsx`) during the broad test run. Rather than assume, this was verified directly: this session's entire changeset was `git stash`-ed (all 15 modified/1 new file), the exact same test files were re-run against the untouched baseline, and the **identical** failure (`useAuth must be used within AuthProvider`, thrown from `LearnerSkillSwitcher` via `useEnabledModules` — same call site, same original line number) reproduced. The stash was then restored. This confirms the failure predates this session's work: `app/page.test.tsx` mocks away `AppShell`/`AuthGuard` entirely (`vi.mock('@/components/layout', ...)`) but never provides an `AuthContext.Provider`, and `useEnabledModules` (both the old and new implementation) calls `useAuth()` unconditionally — a pre-existing test gap, not a regression from this work. Not fixed in this pass (out of scope; see `07-release-verification.md`).

## Metrics requiring a physical device or profiler

The following, requested by the task brief, are genuinely not producible in this environment and are marked accordingly rather than estimated:

| Metric | Status |
|---|---|
| Cold app start (tap icon → interactive) | NOT MEASURABLE IN CURRENT ENVIRONMENT |
| Warm resume time | NOT MEASURABLE IN CURRENT ENVIRONMENT |
| Dashboard cold-open wall-clock time | NOT MEASURABLE IN CURRENT ENVIRONMENT |
| Dashboard warm-return wall-clock time | NOT MEASURABLE IN CURRENT ENVIRONMENT |
| Major route-transition wall-clock time | NOT MEASURABLE IN CURRENT ENVIRONMENT |
| Dropped/janky frame counts | NOT MEASURABLE IN CURRENT ENVIRONMENT |
| Header visual alignment on S24 Ultra-class hardware | NOT MEASURABLE IN CURRENT ENVIRONMENT — see `06-device-test-matrix.md` for the exact procedure to run this |

A prior, unrelated effort in this same repository (`docs/performance/2026-08-07-performance-evidence.md`) did produce exactly this class of measurement via an isolated staging-like GitHub Actions stack with Playwright browser budgets and k6 load testing. That infrastructure exists in this repo's CI but was not available to run interactively in this session (no Docker, no CI runner access here). `07-release-verification.md` recommends running that same gate (or an equivalent Mobile CI run) against this branch before merging, both to measure the change and to confirm it does not regress the LCP/FCP budget that prior pass established.
