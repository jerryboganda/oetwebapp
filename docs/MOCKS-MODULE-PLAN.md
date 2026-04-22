# Mocks Module — Audit, Gap Analysis & Enhancement Plan

> Owner: Platform Engineering · Scope: learner-facing Mock Center at `/mocks` and its supporting
> endpoints, setup flow, in-flight orchestrator, and report surface. Backend: `MockService.cs`,
> `MockAdminEndpoints.cs`, `LearnerEndpoints.cs` (`/v1/mocks*`, `/v1/mock-attempts*`, `/v1/mock-reports*`).
> Frontend: `app/mocks/**`, `lib/api.ts` (`fetchMocksHome`, `fetchMockOptions`, …).

---

## 1. Executive Summary

The Mocks module is **architecturally correct and well-wired end-to-end**: bundles → attempts → sections →
reports, with a wallet-backed review-credit reservation system and a background report-generation job.
The data model is sound and matches the OET exam shape (4 sub-tests in canonical Listening → Reading →
Writing → Speaking order, full + sub-test bundles, per-profession routing, review credits with
reservation/consumption/release lifecycle).

However, the **learner-facing surface has five concrete weaknesses** that Dr. Hesham's screenshot
(`Failed to load mock center.`) exposes today, plus several business-level gaps that reduce the module's
pedagogical value. None are blocking architectural issues — all can be remediated incrementally without
schema changes.

| # | Severity | Issue | Fix |
|---|----------|-------|-----|
| 1 | **High**   | Loader fails silently with a bare red string; no retry, no diagnostic, no fallback content. | Graceful error panel with retry + clear empty state. |
| 2 | **High**   | `resumableAttempts` is computed server-side but **never rendered**. Learners lose context of in-flight mocks and always see a clean-slate surface. | Surface "Resume where you left off" card at the top of the layout. |
| 3 | Medium     | No per-section progress visibility on Full Mocks (e.g., "Listening ✓  Reading ✓  Writing —  Speaking —"). | Expand full-mock row with section dot-row using real `MockSectionAttempt.State`. |
| 4 | Medium     | No profession filter; learners browsing a shared account see bundles for all professions. | Add single-select profession pill filter; default to learner profile's profession. |
| 5 | Medium     | Review-credits surface lists `availableCredits` only; hides reserved / consumed / pending reviews that the backend already exposes. | Expand the Review Capacity card with the full credit ledger summary. |
| 6 | Low        | Hero highlights ("0 available · 0 full mocks · 0 available") render even when the API call has failed, making the state ambiguous. | Suppress hero highlights in error state; show "Couldn't load counts — try again" affordance instead. |
| 7 | Low        | "Recommended Next Step" copy is static when no bundles exist; clicking it routes to `/mocks/setup` which then also fails. | Tie recommendation CTA to real `emptyState.route` when no bundles are published. |

---

## 2. Business Requirements (Reference)

### 2.1 What the Mock Center MUST do

1. **Let learners pick the right simulation depth** — a Full Mock (all 4 sub-tests back-to-back) or a
   Sub-test Mock (one skill only) tied to a published `MockBundle`.
2. **Enforce OET exam fidelity** — Listening → Reading → Writing → Speaking order for Full mocks;
   per-sub-test strict timer defaults; exam vs practice modes.
3. **Integrate with expert review** — productive-skill mocks (Writing, Speaking) can reserve review
   credits up front; credits are atomically debited and released on cancel.
4. **Surface progress & evidence** — a completed mock generates a `MockReport` via background job,
   visible on `/mocks/report/{reportId}`, with study-plan update CTA.
5. **Never let learners lose state** — paused / in-progress / evaluating attempts must be resumable.
6. **Be profession-aware** — bundles can target Medicine, Nursing, Pharmacy, Dentistry, etc. (or be
   universal), and the visible list should be scoped to the learner's profession by default.
7. **Be credit-safe** — no expert review can be reserved when the wallet has insufficient credits;
   cancellation must release reserved credits.

### 2.2 Governance

- **Mission-critical rulebooks** (`docs/SCORING.md`, `docs/READING-AUTHORING-POLICY.md`, Writing /
  Speaking rulebooks) still apply to the mock's individual sub-test runs — the Mock Center does not
  grade; it orchestrates.
- **Entitlement**: free-tier rate limits apply at the sub-test grader level, not at `/v1/mocks`; the
  mock center stays open.

---

## 3. Current-State Audit

### 3.1 Backend — `/v1/mocks` (`MockService.GetMocksAsync`)

**Strengths**
- Correct data composition (bundles × attempts × reports × reservations).
- `emptyState` + `recommendedNextMock` return pre-shaped props, so the UI doesn't invent semantics.
- Wallet is auto-created via `EnsureWalletAsync` — no silent 404 on first-ever mock landing.
- Returns `resumableAttempts` filtered to `InProgress | Paused | Evaluating`.

**Gaps**
- `EnsureUserAsync` throws `ApiException.NotFound("learner_not_found")` if the JWT `userId` has no
  matching `Users` row. In production this surfaces as a 404 on `/v1/mocks` — the exact failure mode
  Dr. Hesham sees. **Root cause**: the learner exists in Identity but their profile row isn't
  materialised until `LearnerService.BootstrapAsync` runs. Ensuring `/v1/mocks` can **render an empty
  state instead of 404** for a valid-but-uninitialised learner is the safest fix.

**Recommendation (included in this plan's implementation phase)**: treat "learner profile missing" as
an empty-state render path rather than an error.

### 3.2 Frontend — `app/mocks/page.tsx`

**Strengths**
- Uses `LearnerDashboardShell`, `LearnerPageHero`, `LearnerSurfaceCard`, `LearnerSurfaceSectionHeader`
  — matches the design system in `DESIGN.md`.
- Skeleton + error states exist.
- Motion primitives used.

**Gaps**
- `resumableAttempts` is destructured from `home` only implicitly; no UI surface consumes it.
- `error` state renders a single sentence with no retry button, no detail, no fallback.
- `home` variable type is `Record<string, any>` — loses all type safety.
- Hero highlights render in the error case with `0 / 0 / 0` — confusing.

### 3.3 Frontend — `app/mocks/setup/page.tsx`

**Strengths**: robust setup, review-credit insufficiency detection, profession normalisation.
**Gaps**: same error-panel weakness (catches the API failure and shows a flat string).

### 3.4 Frontend — `app/mocks/player/[id]/page.tsx`

**Strengths**: resume + complete + submit wired correctly.
**Gaps**: no pause/resume explicit CTA; no server-driven timer enforcement (out of scope here).

---

## 4. Enhancement Plan

### 4.1 Phase A — Resilience & Visibility (this PR)

**A1. Graceful loader + retry**
- Replace the bare red error with an `InlineAlert`-styled card with:
  - Short problem statement
  - Correlated help text
  - Primary action: "Retry"
  - Secondary action: "Contact support" (links to existing `/help` / `/settings/support` route)
- When loading has failed, **suppress hero highlight chips** so `0/0/0` does not appear as if it were
  real data.

**A2. Resume-in-progress surface**
- Add a new "Continue where you left off" section that renders `home.resumableAttempts` with a card
  per attempt using `LearnerSurfaceCard` (`accent: 'primary'`, `kind: 'status'`).
- Each card links to `/mocks/player/{attemptId}` and shows the last active section + mock type label.
- Empty result: render nothing (not a placeholder) — avoid section-chrome noise.

**A3. Full-mock per-section progress dots**
- For each full mock row, render a compact 4-dot strip (`Listening / Reading / Writing / Speaking`)
  driven by the bundle's included subtests + last attempt's per-section state if available.
- Colour dots with existing DESIGN.md palette — green (`completed`), amber (`in-progress`), gray
  (`not-started`), slate (`locked`).

**A4. Expanded review-credit ledger card**
- Show `availableCredits`, `reservedCredits`, `consumedCredits`, `pendingReviews`, `completedReviews`
  — all already returned by the backend — inside a single `LearnerSurfaceCard` with `metaItems`.

**A5. Backend resilience — empty state for uninitialised learners**
- In `MockService.GetMocksAsync`, if the `Users` row is missing, return the canonical empty shape
  (wallet=0, bundles=[], attempts=[], reports=[], emptyState={ learnerUninitialised message + CTA to
  `/dashboard` to complete bootstrap }) **instead of throwing 404**. Keep strict 404 behaviour for the
  admin endpoints.

### 4.2 Phase B — Business-Value Additions

- **B1. Profession filter pill group** ✅ shipped — scoped to learner's profession by default; `All
  professions` toggle; backend returns `learnerProfession` + `availableProfessions`.
- **B2. Pass-prediction tie-in** ✅ shipped — `recommendedNextMock` now returns `latestOverallScore`,
  `latestOverallGrade`, `trend` (`up`/`down`/`flat`), and a `readiness` advisory block (`strong` /
  `passing` / `developing` / `foundation`) anchored on the OET 350/500 pass threshold. Frontend surfaces
  the trend label + readiness message in the Recommended Next Step card.
- **B3. Readiness-score gating** ✅ shipped (advisory) — the readiness tier drives the recommended
  card's description copy so sub-pass learners are nudged toward targeted drills before burning a
  full-mock attempt.
- **B4. Mock review SLA surfacing** ✅ shipped — `purchasedMockReviews.reviewTurnaroundHours` and
  `reviewSlaLabel` are returned by the backend and displayed inside the Review Capacity card so
  learners know the 48-hour expert turnaround up-front.
- **B5. Multi-profession households** ✅ shipped (via B1) — the profession filter lets a learner
  temporarily browse bundles for a different profession without mutating their profile; the default
  snaps back to their active profession on next load.

### 4.3 Phase C — Policy & Rulebook integration

- **C1. Score-guarantee eligibility check** ✅ shipped — `/v1/mocks` now returns a read-only
  `scoreGuarantee` block (status, baseline, guaranteedScore, latestOverallScore, onTrack,
  daysRemaining, message). The Mock Center renders it as a status card only when a pledge exists;
  the billing module remains the source of truth for activation, claim submission, and refund
  flows.
- **C2. Cohort analytics** ✅ shipped — `/v1/mocks` returns a `cohortPercentile` block (banded
  percentile rounded to the nearest 5, cohort size, 90-day window) computed over peer `MockReports`
  joined through `MockAttempts.UserId`. The response is suppressed when the cohort is smaller than
  `CohortPrivacyMinimum = 10` peers to prevent re-identification; banded percentiles further blunt
  inference risk.

---

## 5. UI/UX Contract

All additions MUST follow `DESIGN.md` + the exact learner dashboard patterns:

- `LearnerDashboardShell` wrapper (already in place).
- `LearnerPageHero` with `accent='navy'` for top hero.
- `LearnerSurfaceCard` for every tile; do not inline bespoke card markup.
- `LearnerSurfaceSectionHeader` for every section header.
- Typography: `text-navy` headings, `text-muted` supporting copy, `font-black` for score numerals.
- Motion: `MotionSection` / `MotionItem` only.
- Icons: `lucide-react`, single-sized 20–24 px in card headers.
- Colour accents: re-use existing per-subtest colour map (`rose/purple/indigo/blue`).
- Skeletons use `rounded-2xl` sizes already present on the page.

---

## 6. Verification Plan

- `npx tsc --noEmit` — 0 errors.
- Targeted vitest: `app/mocks/page.test.tsx` (new), existing `app/mocks/player/[id]/page.test.tsx`.
- `dotnet build backend/OetLearner.sln` — 0 errors.
- Manual smoke: simulate (a) API 404 → retry button, (b) in-progress attempt → resume card appears,
  (c) seeded DB with published bundles → per-section dots correct.

---

## 7. Out-of-Scope for this Plan

- Real-time collaborative mock sessions.
- Anti-cheat camera / lockdown browser integration.
- New mock `MockBundle` publishing UI — already lives in `/admin/content/mocks`.
- Score-guarantee refund automation — owned by billing module.
