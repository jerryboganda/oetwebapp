# Expert Console Enhancement Plan — Ultra-High-Level

## Executive Summary

The Expert Console is the **primary monetization engine** of the OET Prep Platform. Expert reviews command a 10–16× price premium over AI-only content. The console is already substantial (22 pages, ~100 API endpoints, 6-step onboarding) — but critical gaps in **quality assurance, operational resilience, compensation transparency, and cross-portal integration** limit its ability to scale the business.

This plan addresses every open gap from the system gap analysis (`docs/COMPREHENSIVE_SYSTEM_GAP_ANALYSIS.md`), UX audit backlog, testing coverage holes, and cross-portal integration weaknesses — all while maintaining strict adherence to mission-critical invariants (OET scoring, rulebooks, AI gateway, content upload, reading authoring, grammar, pronunciation, conversation).

---

## 1. Business-Tier Enhancements (Monetization & Trust)

### 1.1 Expert Compensation Dashboard (NEW PAGE)

**Rationale**: Expert supply constraint is the #1 operational risk. No compensation visibility exists today.

| Component | Details |
|---|---|
| **New route** | `/expert/compensation` |
| **Backend endpoint** | `GET /v1/expert/compensation` — per-item rate, completed counts, pending payouts, historical earnings |
| **Backend endpoint** | `GET /v1/expert/compensation/history` — paginated payout ledger |
| **Admin counterpart** | New tab under `/admin/users?tab=tutors` → per-expert compensation view, payout approval |
| **Data model** | New `ExpertCompensationRate` entity (per-expert, per-subtest rates), `ExpertEarning` (per-review earnings), `ExpertPayout` (batch payouts) |
| **Invariants** | Earnings computed server-side only; no client-side calculation of monetary values; audit trail for every payout |
| **Cross-portal** | Admin sees aggregate tutor costs under `/admin/business-intelligence`; Sponsor billing doesn't directly expose per-expert rates |

### 1.2 Review SLA & Quality Guarantee Visibility

**Rationale**: "Premium review promise must be operationally visible."

| Component | Details |
|---|---|
| **Enhance** | Expert dashboard SLA card — show SLA promise made to learner, remaining time per assignment, escalation triggers |
| **Enhance** | Learner-side: `ReviewRequestDrawer` shows expert's SLA history, quality rating, calibration alignment |
| **New** | Admin `/admin/review-ops` → SLA violation report, expert performance trending |
| **Backend** | New `ExpertSlaSnapshot` metrics entity tracking per-review SLA compliance with learner-facing guarantees |

### 1.3 Amend Submitted Review Within Time Window (GAP E-02)

**Rationale**: Minor errors should be correctable without full rework cycle.

| Component | Details |
|---|---|
| **Policy** | 24-hour amend window after submission; amend count capped at 2 per review |
| **Backend** | `POST /v1/expert/reviews/{reviewRequestId}/amend` — accepts partial score/comment changes |
| **Backend** | `GET /v1/expert/reviews/{reviewRequestId}/amend-eligibility` — returns remaining window + count |
| **Frontend** | Review workspace detects amend-eligible state → shows "Edit Review" mode with diff highlighting |
| **Audit** | Every amendment creates `ExpertReviewAmend` audit record with before/after snapshot |
| **Learner** | Notification: "Your review has been updated by the expert" with diff summary |
| **Admin** | Amend history visible in review ops detail view |

---

## 2. Operational Resilience Enhancements

### 2.1 Expert↔Admin In-App Communication Channel (GAP R-12)

**Rationale**: No way for experts to flag issues or ask admins for help.

| Component | Details |
|---|---|
| **New route** | `/expert/messages` — thread-based messaging to admin team |
| **Backend** | `GET/POST /v1/expert/messages` — list/create expert↔admin threads |
| **Backend** | `GET/POST /v1/expert/messages/{threadId}/replies` — reply to thread |
| **Admin counterpart** | New section in `/admin/users?tab=tutors` → "Messages" tab showing all expert threads |
| **Notifications** | SignalR real-time notification for new messages on both sides |
| **Data model** | `ExpertMessageThread` + `ExpertMessageReply` entities |
| **Context linking** | Messages can link to specific review requests, calibration cases, or learner profiles |

### 2.2 Rework Chain History (GAP E-06)

**Rationale**: When reviews go through multiple rework cycles, experts lose context.

| Component | Details |
|---|---|
| **Enhance** | Review workspace sidebar shows full rework chain: original → rework1 → rework2 → current |
| **Backend** | `GET /v1/expert/reviews/{reviewRequestId}/rework-chain` — returns linked chain with scores/comments per iteration |
| **Frontend** | Timeline view with expandable iterations showing what changed between each cycle |
| **Cross-portal** | Learner sees rework chain in review results view; admin sees in review ops |

### 2.3 Bulk Review Actions (GAP E-07)

**Rationale**: High-volume experts waste time on single-item operations.

| Component | Details |
|---|---|
| **New** | Queue page adds multi-select mode (checkboxes + "Select All" / "Select Filtered") |
| **Backend** | `POST /v1/expert/queue/bulk-claim` — accepts array of review request IDs, claims up to `MaxBulkClaim` (configurable, default 10) |
| **Backend** | `POST /v1/expert/queue/bulk-release` — releases selected claimed reviews |
| **Frontend** | Bulk action toolbar with claim/release buttons, count badge, confirmation modal |
| **SLA guard** | Bulk claim checks expert availability to ensure they can complete within SLA; warns if overloaded |

### 2.4 Stale Session / Active-Status Client-Side Guard (GAP R-10)

**Rationale**: Deactivated experts briefly see dashboard before API rejects them.

| Component | Details |
|---|---|
| **Fix** | `useExpertAuth()` hook added periodic `isActive` verification (poll every 5 min or on tab focus) |
| **Fix** | SignalR `NotificationHub` pushes `ExpertDeactivated` event to connected clients — immediately redirects |
| **Backend** | `OnTokenValidated` check already fails; add SignalR group notification on deactivation |
| **Frontend** | Inline banner on dashboard if `isActive === false` (catches edge case before next poll) |

---

## 3. Cross-Portal Integration Strengthening

### 3.1 Unified Review Lifecycle Across All Portals

**Rationale**: Review state changes today propagate through point-to-point API calls. A unified event system prevents inconsistencies.

| Component | Details |
|---|---|
| **Backend** | New `ReviewLifecycleEvent` domain event pattern: `ReviewClaimed`, `ReviewDraftSaved`, `ReviewSubmitted`, `ReviewAmended`, `ReviewReworkRequested`, `ReviewCompleted` |
| **SignalR** | `NotificationHub` broadcasts lifecycle events to relevant groups: `learner:{learnerId}`, `expert:{expertId}`, `admin:review-ops` |
| **Frontend** | Expert queue auto-refreshes on `ReviewClaimed`/`ReviewReleased` from other experts |
| **Frontend** | Learner results page shows real-time status: "Expert is reviewing your submission" |
| **Frontend** | Admin review-ops shows live expert assignment changes |

### 3.2 Escalation Path Smoothing

**Rationale**: Current escalation flow is admin-heavy with no expert visibility.

| Component | Details |
|---|---|
| **Enhance** | Expert sees escalation status on review they submitted (badge: "Under Admin Review") |
| **Enhance** | Admin escalation detail shows submitting expert's calibration alignment for context |
| **New** | `POST /v1/admin/escalations/{escalationId}/request-expert-input` — admin can ask original expert for clarification before resolution |
| **New** | Expert notification: "Admin requests your input on escalation #..." |

### 3.3 Sponsor↔Expert Indirect Connection Enhancement

**Rationale**: Sponsors pay for expert reviews but have zero visibility into review quality.

| Component | Details |
|---|---|
| **New** | Sponsor dashboard shows aggregate review stats for sponsored learners (completion rate, avg turnaround, grade distribution) — anonymized, no per-expert breakdown |
| **New** | `GET /v1/sponsor/learners/{learnerId}/review-summary` — per-learner review history with anonymized expert identifiers |

---

## 4. Testing Coverage Enhancement

### 4.1 Unit Test Backfill (Critical Paths)

| Priority | Target | Test Count Target | What to Test |
|---|---|---|---|
| **P0** | `lib/stores/expert-store.ts` | 8 tests | `upsertReviewDraft`, `getReviewDraft`, `clearReviewDraft`, `partialize` persistence, corrupted localStorage recovery, schema migration, playback state reset on reload |
| **P0** | `lib/hooks/use-expert-auth.ts` | 6 tests | Loading state, deactivated expert, successful auth, 401 redirect to sign-in, non-expert role redirect, poll-based isActive check |
| **P1** | `app/expert/review/writing/[reviewRequestId]/page.tsx` | 10 tests | Draft restore, draft conflict resolution (`pickLatestDraft`), `beforeunload` guard, all-criteria-scored validation, submit flow, amend flow, rework flow, partial artifact status, error retry, Ctrl+S shortcut |
| **P1** | `app/expert/review/speaking/[reviewRequestId]/page.tsx` | 8 tests | Audio load, transcript sync, timestamp comment creation, AI flag interaction, submit validation (9 criteria), draft save, error state, role card rendering |
| **P1** | `app/expert/calibration/[caseId]/page.tsx` | 6 tests | Score input, conflict 409 handling (submit + draft-save), post-submit lock, alignment evidence display, read-only mode, draft restore |
| **P2** | `app/expert/mobile-review/page.tsx` | 5 tests | Step navigation, score input, draft save, submit, queue card display |
| **P2** | `app/expert/compensation/page.tsx` | 4 tests | Earnings display, history pagination, empty state, error state |

### 4.2 E2E Test Expansion

| Priority | Spec | Browser Matrix | Tests |
|---|---|---|---|
| **P0** | `tests/e2e/expert/workflow-cross-browser.spec.ts` | Chromium + Firefox + WebKit | Writing review draft→submit→amend lifecycle, speaking review draft→submit lifecycle (remove `chromium-only` skip) |
| **P1** | `tests/e2e/expert/calibration-workflow.spec.ts` | Chromium | Calibration cases list, open case, score + submit, alignment evidence, conflict 409, speaking calibration |
| **P1** | `tests/e2e/expert/onboarding-browser.spec.ts` | Chromium | Full 6-step browser-based onboarding from start to completion (currently API-only in prod-smoke) |
| **P2** | `tests/e2e/expert/mobile-expert.spec.ts` | mobile-chromium-expert | Mobile review wizard, queue page responsive, dashboard mobile nav |
| **P2** | `tests/e2e/expert/bulk-actions.spec.ts` | Chromium | Multi-select, bulk claim, bulk release, overload warning |
| **P2** | `tests/e2e/expert/compensation.spec.ts` | Chromium | Earnings display, history view |

### 4.3 Backend Test Backfill

| Priority | Target | Tests |
|---|---|---|
| **P0** | `ExpertOnboardingService` | 6 tests: profile save, qualifications save, rates save, complete validation (missing step), complete success, status retrieval |
| **P1** | Speaking review submit | 1 test: `ExpertDraftSaveAndSubmit_SpeakingFlow` (mirror of writing flow test) |
| **P1** | Rework request | 2 tests: successful rework, rework on non-claimed review (403) |
| **P1** | Amend review | 3 tests: amend within window, amend past window (400), amend count exceeded (400) |
| **P2** | SLA computation | 2 tests: on-track, overdue with edge-case timezone |
| **P2** | Bulk claim/release | 3 tests: bulk claim success, bulk claim exceeds limit (400), bulk claim overloading (warning) |

---

## 5. UX Audit & Design Polish

### 5.1 UX Audit Completion (All 16 Expert Routes)

Phase all expert routes through the UX audit pipeline (`docs/ux/UX-AUDIT-MASTER-PLAN.md`):

| Phase | Routes | Focus |
|---|---|---|
| **Phase 1** | `/expert`, `/expert/queue`, `/expert/review/*` | T0 launch-critical: Dashboard, Queue, Review Workspaces |
| **Phase 2** | `/expert/calibration/*`, `/expert/metrics`, `/expert/schedule`, `/expert/onboarding` | T1: Calibration, Metrics, Schedule, Onboarding |
| **Phase 3** | `/expert/learners/*`, `/expert/private-speaking`, `/expert/queue-priority`, `/expert/mobile-review` | T1/T2: Learners, Private Speaking, Priority, Mobile |
| **Phase 4** | `/expert/scoring-quality`, `/expert/rubric-reference`, `/expert/ai-prefill`, `/expert/annotation-templates`, `/expert/ask-an-expert`, `/expert/mocks/bookings` | T2: Auxiliary tools |

Each route audited against 10 heuristics (H1–H10), scored 0–3, target ≥24/30.

### 5.2 Expert Dashboard Redesign (Home Base)

| Change | Rationale |
|---|---|
| Replace current card grid with **workload-first layout**: large "Next Review" card (SLA countdown, review type, learner name) as primary action | Reduces cognitive load for "what do I do next" |
| Add **"Reviewer Health" widget**: current calibration alignment, weekly completion trend, rework rate vs. org average | Builds trust and motivates quality |
| Add **compensation snapshot**: earnings this week/month, pending payouts | New — transparency driver |
| Move "Recent Activity" to collapsible sidebar on desktop, below fold on mobile | Declutter main viewport |

### 5.3 Mobile-First Expert Experience

| Change | Details |
|---|---|
| **Responsive review workspace**: Stack criteria vertically on mobile instead of side-by-side; collapsible learner response panel |
| **Mobile calibration**: Simplified score grid (numeric input instead of band select dropdowns) |
| **Mobile queue**: Swipe actions (claim right, release left, open details tap) |
| **Mobile schedule**: Touch-friendly day/hour picker |
| **Safe area**: All expert mobile views respect `safe-area-inset` |

---

## 6. Feature Completeness

### 6.1 Annotation Templates Full-Text Search (GAP E-08)

| Component | Details |
|---|---|
| **Backend** | Add `search` param to `GET /v1/expert/annotation-templates` — searches label + templateText with ILIKE |
| **Frontend** | Search input above template list with debounced query |
| **Pagination** | Templates list paginated if >50 results |

### 6.2 Mock Data Fallback Removal (GAPS M-07, M-08)

| Change | Details |
|---|---|
| **`/expert/calibration`**: Remove `MOCK_CALIBRATION_CASES` fallback; show proper empty state with admin contact link if no cases |
| **`/expert/learners`**: Remove `MOCK_LEARNER_PROFILES` fallback; show "No assigned learners" empty state with explanation |

### 6.3 Review Workspace Quality-of-Life

| Change | Details |
|---|---|
| **Keyboard shortcut cheat sheet**: Modal accessible via `?` key showing all shortcuts (Ctrl+S save, Ctrl+Enter submit, Ctrl+Shift+R rework, 1-9 criterion focus) |
| **Auto-save indicator**: Animated checkmark when draft auto-saves (every 30s), red dot when save fails |
| **Criterion quick-nav**: Number keys 1–6 (writing) or 1–9 (speaking) jump focus to that criterion |
| **Undo/redo**: Ctrl+Z/Ctrl+Shift+Z for score changes and comment edits within draft session |

---

## 7. Implementation Sequence

### Phase A — Foundation (Weeks 1–2)
1. Backend: Amend review endpoints + eligibility check
2. Backend: Rework chain history endpoint
3. Backend: Expert↔Admin messaging system
4. Backend: Review lifecycle events + SignalR broadcasting
5. Unit tests: `expert-store.ts`, `use-expert-auth.ts`
6. Backend tests: Onboarding, speaking submit, rework

### Phase B — Core Enhancements (Weeks 3–4)
7. Frontend: Amend review UI in writing/speaking workspaces
8. Frontend: Rework chain visualization
9. Frontend: Expert↔Admin messaging UI + admin counterpart
10. Frontend: Real-time queue updates via SignalR
11. Unit tests: Review workspaces, calibration
12. E2E: Cross-browser workflow tests, calibration workflow

### Phase C — Monetization & Quality (Weeks 5–6)
13. Backend: Expert compensation system (entities, endpoints, admin views)
14. Frontend: Compensation dashboard
15. Frontend: Admin tutor compensation management
16. Backend: SLA snapshot tracking
17. Frontend: SLA & quality visibility enhancements
18. Frontend: Dashboard redesign (workload-first layout)
19. E2E: Compensation tests

### Phase D — Operations & Polish (Weeks 7–8)
20. Backend: Bulk claim/release endpoints
21. Frontend: Queue multi-select + bulk action toolbar
22. Backend: Annotation template full-text search
23. Frontend: Template search UI
24. Frontend: Mock data fallback removal
25. Frontend: Review workspace QoL (keyboard cheat sheet, auto-save indicator, undo/redo)
26. UX audit: Phase 1 T0 routes (Dashboard, Queue, Review Workspaces)
27. E2E: Bulk actions, mobile expert, onboarding browser

### Phase E — Cross-Portal & Mobile (Weeks 9–10)
28. Frontend: Sponsor review summary dashboard
29. Frontend: Escalation path smoothing (expert visibility, admin→expert input request)
30. Frontend: Mobile-first responsive review workspace
31. Frontend: Mobile calibration, mobile queue swipe actions
32. UX audit: Phases 2–4 (all remaining routes)
33. E2E: Mobile expert test suite
34. Documentation: Update `expert-console-manual.md` with all new features

---

## 8. Files to Modify / Create

### New Files

| Path | Purpose |
|---|---|
| `app/expert/compensation/page.tsx` | Expert compensation dashboard |
| `app/expert/messages/page.tsx` | Expert↔Admin messaging |
| `app/expert/messages/[threadId]/page.tsx` | Message thread detail |
| `backend/src/OetLearner.Api/Endpoints/ExpertMessagingEndpoints.cs` | Messaging API |
| `backend/src/OetLearner.Api/Endpoints/ExpertCompensationEndpoints.cs` | Compensation API |
| `backend/src/OetLearner.Api/Domain/ExpertCompensationEntities.cs` | Compensation data models |
| `backend/src/OetLearner.Api/Domain/ExpertMessagingEntities.cs` | Messaging data models |
| `backend/src/OetLearner.Api/Services/ExpertCompensationService.cs` | Compensation logic |
| `backend/src/OetLearner.Api/Services/ExpertMessagingService.cs` | Messaging logic |
| `backend/src/OetLearner.Api/Contracts/ExpertCompensationContracts.cs` | Compensation DTOs |
| `backend/src/OetLearner.Api/Contracts/ExpertMessagingContracts.cs` | Messaging DTOs |
| `lib/types/expert-compensation.ts` | Frontend compensation types |
| `lib/types/expert-messaging.ts` | Frontend messaging types |
| `lib/hooks/use-expert-messages.ts` | Messaging hook |
| `lib/hooks/use-expert-compensation.ts` | Compensation hook |
| `tests/e2e/expert/workflow-cross-browser.spec.ts` | Cross-browser workflow E2E |
| `tests/e2e/expert/calibration-workflow.spec.ts` | Calibration workflow E2E |
| `tests/e2e/expert/onboarding-browser.spec.ts` | Browser onboarding E2E |
| `tests/e2e/expert/mobile-expert.spec.ts` | Mobile expert E2E |
| `tests/e2e/expert/bulk-actions.spec.ts` | Bulk actions E2E |
| `tests/e2e/expert/compensation.spec.ts` | Compensation E2E |

### Modified Files

| Path | Change |
|---|---|
| `app/expert/layout.tsx` | Add Compensation + Messages nav items |
| `app/expert/page.tsx` | Dashboard redesign: workload-first layout, compensation snapshot, reviewer health widget |
| `app/expert/queue/page.tsx` | Multi-select mode, bulk action toolbar, SignalR real-time updates |
| `app/expert/review/writing/[reviewRequestId]/page.tsx` | Amend mode, rework chain, keyboard shortcuts, auto-save indicator, undo/redo |
| `app/expert/review/speaking/[reviewRequestId]/page.tsx` | Amend mode, rework chain, keyboard shortcuts, auto-save indicator, undo/redo |
| `app/expert/calibration/page.tsx` | Remove mock fallback |
| `app/expert/calibration/[caseId]/page.tsx` | Real-time status updates |
| `app/expert/learners/page.tsx` | Remove mock fallback |
| `app/expert/annotation-templates/page.tsx` | Full-text search input |
| `app/expert/mobile-review/page.tsx` | Safe-area improvements |
| `app/expert/onboarding/page.tsx` | Post-onboarding compensation setup step |
| `components/layout/expert-dashboard-shell.tsx` | New nav items |
| `components/domain/expert-route-surface.tsx` | New card variants for compensation/messaging |
| `lib/api.ts` | All new endpoints + amend, rework-chain, bulk actions, search |
| `lib/types/expert.ts` | Amend types, rework chain types, lifecycle event types |
| `lib/stores/expert-store.ts` | Undo/redo stack, auto-save state, compensation state |
| `lib/hooks/use-expert-auth.ts` | Periodic isActive check, SignalR deactivation listener |
| `lib/scoring.ts` | No changes (mission-critical invariant) |
| `lib/rulebook/` | No changes (mission-critical invariant) |
| `backend/.../Endpoints/ExpertEndpoints.cs` | Amend, rework-chain, bulk claim/release, SLA endpoints |
| `backend/.../Services/ExpertService.cs` | Amend logic, rework chain, bulk operations, SLA computation |
| `backend/.../Services/ExpertOnboardingService.cs` | Post-onboarding compensation rate setup |
| `backend/.../Domain/ExpertEntities.cs` | Amend, compensation, messaging entities |
| `backend/.../Contracts/ExpertRequests.cs` | Amend, bulk, search request DTOs |
| `backend/.../Contracts/ExpertResponses.cs` | Amend, chain, SLA, compensation response DTOs |
| `backend/.../Hubs/NotificationHub.cs` | Lifecycle event broadcasting, deactivation push |
| `backend/.../Data/LearnerDbContext.cs` | New entity DbSets + migrations |
| `app/admin/users/page.tsx` | Compensation tab, messaging tab |
| `app/admin/review-ops/page.tsx` | SLA violation report, real-time updates |
| `app/admin/escalations/page.tsx` | Expert calibration context, request-expert-input action |
| `app/sponsor/page.tsx` | Review summary widget |
| `app/sponsor/learners/page.tsx` | Per-learner review summary |
| `app/dashboard/page.tsx` | Real-time review status indicator |
| `app/escalations/page.tsx` | Real-time resolution status |
| `components/domain/review-request-drawer.tsx` | Expert SLA/quality display |
| `playwright.config.ts` | New mobile-expert project, E2E spec registration |
| `docs/product-manual/expert-console-manual.md` | Full update for all new features |

---

## 9. Verification

### Automated Verification

```bash
# Type-check (0 errors)
npx tsc --noEmit

# Lint (0 errors)
npm run lint

# Unit tests (target: +40 new tests, 100% pass)
npm test

# Backend tests (target: +18 new tests, 100% pass)
npm run backend:test

# Production build (must compile all new pages)
npm run build

# E2E expert suite (target: +6 new specs, 100% pass)
npm run test:e2e -- --grep expert

# Cross-browser expert suite
npm run test:e2e -- --project=chromium-expert --project=firefox-expert --project=webkit-expert
```

### Manual Verification Checklist

1. Expert onboarding flow: new expert → complete 6 steps → redirected to dashboard → sees compensation setup
2. Review lifecycle: learner submits → expert claims → expert reviews → expert submits → learner sees results → expert amends within 24h → amendment reflected
3. Rework chain: expert requests rework → learner resubmits → expert sees full chain with diff
4. Bulk actions: expert selects 5 reviews → bulk claims → all move to in-progress
5. Messaging: expert sends message → admin receives notification → admin replies → expert sees reply
6. Compensation: admin sets per-expert rates → expert completes reviews → earnings update in real-time → admin approves payout
7. Deactivation: admin deactivates expert → SignalR pushes event → expert redirected immediately
8. Mobile: all expert pages render correctly at 360px viewport, safe-area respected, swipe actions work
9. Sponsor: sponsor sees anonymized aggregate review stats for sponsored learners
10. Cross-portal real-time: claim a review in one browser → second expert browser sees queue update

### Mission-Critical Invariant Verification

- [ ] All scoring still routes through `lib/scoring.ts` and `OetScoring` (.NET)
- [ ] All rule enforcement still routes through `lib/rulebook/` and `Rulebook` (.NET)
- [ ] All AI calls route through `buildAiGroundedPrompt()` / `AiGatewayService`
- [ ] Content uploads still go through `IFileStorage`
- [ ] Statement of Results card unchanged
- [ ] Reading authoring invariants preserved
- [ ] Grammar module invariants preserved
- [ ] Pronunciation module invariants preserved
- [ ] Conversation module invariants preserved
- [ ] No inline scoring comparisons (`score >= 350`)
- [ ] No direct rulebook JSON reads from UI code
