# Mocks — Follow-Up Waves (post-V2 closure roadmap)

> Date: 2026-05-12
> Owner: Mocks Platform team
> Source: `docs/mocks/PROGRESS.md` § Follow-Up Waves
> Status: waves 1, 2, 4, and 5 closed in code on 2026-05-13; Wave 3 route and E2E spec are present, with only production smoke execution remaining. Retained as an implementation ledger.

## Why this doc exists

Track B closure (May 2026) finished every Wave 5 / Wave 6 / Wave 7 / Wave 8 reviewer Medium and dead-code follow-up. The original 5 follow-up waves are kept here for traceability; waves 1, 2, 4, and 5 have since moved from roadmap to implemented code, and Wave 3 has an E2E spec registered.

---

## Wave 1 — Server-resolved section result adapters

### Current state

Implemented 2026-05-13. `MockReportAggregationService` owns report generation and resolves Reading / Listening through `ReadingMockSectionResultAdapter` and `ListeningMockSectionResultAdapter`, using `ReadingAttempt` / `ListeningAttempt` as the authoritative evidence source. `MockSectionResultResolverTests` lock the tamper-resistant overwrite behavior.

### Acceptance criteria

1. `MockReportAggregationService.AggregateReadingAsync(MockSectionAttempt)` reads `ReadingAttempt` directly via `MockSectionAttempt.SkillEvidenceId` and projects rawScore / maxRawScore / scaledScore / partBreakdown.
2. Same for Listening via `ListeningAttempt`.
3. Writing review-aware aggregation: pending tutor review keeps the subtest provisional and excludes it from the SoR readiness check.
4. Speaking review-aware aggregation: same.
5. `MOCKS-AGGREGATOR-PARITY` regression test compares the new server-resolved values against the existing client-evidence values for a corpus of 10 fixture attempts; differences must be zero.

### Dependencies

- `MockSectionAttempt.SkillEvidenceId` already exists.
- No DB schema changes.

### Effort

Medium — ~2 service files (one new aggregator, one MockService refactor) + 1 test fixture file.

---

## Wave 2 — Tutor / admin live-room controls

### Current state

Implemented 2026-05-13. Live-room state transitions are durable rows in `MockLiveRoomTransitions`, versioned on `MockBooking.LiveRoomTransitionVersion`, exposed through learner/expert/admin REST endpoints, and broadcast over `/v1/mocks/live-room/hub`. Learner and expert room pages subscribe to the hub; admin operations expose transition controls.

### Acceptance criteria

1. New `POST /v1/expert/mocks/bookings/{id}/transition` (ExpertOnly + AssignedTutorId match) accepts a `targetState` and validates against a server-side legal-transition table. Mirrors `SubscriptionStateMachine` pattern from billing slice D.
2. New admin override `POST /v1/admin/mock-bookings/{id}/transition` (AdminContentWrite or new ManageMockBookings permission).
3. Learner client subscribes to `MockBookingHub` SignalR group keyed on `bookingId`; tutor / admin transitions push state changes that the learner read-only state machine reflects.
4. Audit row per transition (`mock_booking_transition`).
5. Unit tests on the transition table.

### Dependencies

- `SubscriptionStateMachine` pattern is the template (Slice D).
- `MockBookingHub` is new — needs SignalR registration + auth handler + group join on bookingId.

### Effort

High — touches expert + admin endpoints, learner hub client, SignalR auth, and adds a new state machine.

---

## Wave 3 — Diagnostic mock learner journey

### Current state

**✅ Already shipped in V2 Wave 7.** `/mocks/diagnostic/page.tsx` wires entitlement → chooser → study path → SoR card. Track B added a Suspense boundary fallback for the initial mount, and `tests/e2e/learner/mocks-diagnostic-flow.spec.ts` is registered across the Playwright matrix.

### Remaining acceptance criteria

1. Production smoke validation against the live route once deployment has the closure build.

### Effort

Low — production smoke run only; the E2E spec is already in the repo.

---

## Wave 4 — Expanded admin item analysis

### Current state

Implemented 2026-05-13. Reading and Listening item analysis now expose difficulty, distractor frequency, and discrimination index. The bundle item-analysis page includes an all/listening switch and the global dashboard displays the discrimination value.

### Acceptance criteria

1. New `GET /v1/admin/mocks/{bundleId}/listening-item-analysis` returns per-item difficulty (% correct), discrimination index (top-third minus bottom-third), and distractor frequency.
2. Endpoint reuses `MockService.GetItemAnalysisAsync` shape; difference is the entity it pulls from (`ListeningAnswer` vs `ReadingAnswer`).
3. Admin UI tab on the bundle item-analysis page picks the subtest.
4. `AdminQualityAnalytics` permission for item-analysis and analytics read routes; content-read remains for ordinary bundle browsing.
5. SQLite test fixture covering 10 attempts × 42 items × correct/incorrect distribution.

### Dependencies

- `ListeningAnswer` / `ListeningItem` schemas already capture the data.
- Adds one endpoint, one service method, one UI tab.

### Effort

Medium — pure read-side work, no schema changes.

---

## Wave 5 — Booking reminders + idempotent worker

### Current state

Implemented 2026-05-13. `MockBookingReminderWorker` uses `MockBookingReminderPlanner` to fire 24h, 2h, and 30m reminders for the learner plus assigned tutor/interlocutor experts; notification dedupe keys make worker restarts idempotent.

### Acceptance criteria

1. New `MockBookingReminderWorker` (BackgroundService) wakes every 5 minutes, queries bookings where `ScheduledStartAt` is within `[Now+30min, Now+45min]` and `LastReminderAt IS NULL`, and emits a notification (learner + tutor).
2. Same worker emits a 24h-out reminder.
3. Idempotency: each (bookingId, kind) combo is keyed in `IdempotencyRecords` to prevent double-fires after a worker restart.
4. Per-booking max-reminder cap from `BillingOptions.MaxRemindersPerBooking` (default 3).
5. Unit tests on the worker query + idempotency contract.

### Dependencies

- `IdempotencyRecord` table already exists (Slice A).
- Notification catalog already covers `mock_booking.upcoming` / `mock_booking.starting_soon`.

### Effort

Medium — new BackgroundService + worker query + tests + notification template entries.

---

## Sequencing recommendation

| Wave | Recommended timing | Rationale |
| ---- | ------------------ | --------- |
| 3 (diagnostic smoke) | Spec added; production smoke pending | Route and Playwright spec exist; run against deployed closure build. |
| 1 (server-resolved adapters) | Done 2026-05-13 | Trust boundary moved into backend adapters. |
| 5 (booking reminders) | Done 2026-05-13 | Learner and expert reminder fan-out implemented. |
| 4 (Listening item analysis) | Done 2026-05-13 | Listening endpoint/UI plus discrimination index implemented. |
| 2 (live-room transitions) | Done 2026-05-13 | SignalR, transition rows, role endpoints, and UI controls implemented. |

## Cross-links

- `docs/mocks/PROGRESS.md` — V2 closure ledger.
- `docs/mocks/PRD.md` — module PRD.
- `docs/MOCKS-RANDOMISATION.md` + `docs/MOCKS-OPTION-ID-MIGRATION.md` — Wave 8 randomisation prerequisites.
- `docs/audits/rulebook-compliance-2026-05-11-closure.md` — broader 2026-05-12 closure manifest.
