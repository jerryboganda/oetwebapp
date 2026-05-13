# Mocks — Follow-Up Waves (post-V2 closure roadmap)

> Date: 2026-05-12
> Owner: Mocks Platform team
> Source: `docs/mocks/PROGRESS.md` § Follow-Up Waves
> Status: planned for v1.1+

## Why this doc exists

Track B closure (May 2026) finished every Wave 5 / Wave 6 / Wave 7 / Wave 8 reviewer Medium and dead-code follow-up. The 5 follow-up waves listed at the bottom of `docs/mocks/PROGRESS.md` are larger scope and are not v1 launch blockers. Each wave is enumerated below with current state, acceptance criteria, dependencies, and risk so the next planning session has a single canonical reference.

---

## Wave 1 — Server-resolved section result adapters

### Current state

The `MockReport.PayloadJson` is built from per-section `MockSectionAttempt.EvidenceJson` rows the players POST after grading. The Reading and Listening grading services already write authoritative graded rows in `ReadingAttempt` / `ListeningAttempt`, but the report aggregation in `MockService.PrepareReportPayloadAsync` re-reads the client-supplied evidence. This is correct enough for v1 because all four player pages POST `completeMockSection` with server-graded scores (closed in MOCK-GAP-008), but a strictly server-resolved adapter would let us drop the client trust completely.

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

Speaking room state transitions are driven by the learner state machine in `app/mocks/speaking-room/[bookingId]/page.tsx` (pre → rp1-prep → rp1-speak → rp2-prep → rp2-speak → submitting → done). The tutor view (`app/expert/speaking-room/[bookingId]/page.tsx`) is read-only and cannot intervene if the learner stalls. The admin booking page also lacks transition controls.

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

**✅ Already shipped in V2 Wave 7.** `/mocks/diagnostic/page.tsx` wires entitlement → chooser → study path → SoR card. Track B added a Suspense boundary fallback for the initial mount.

### Remaining acceptance criteria

1. E2E smoke spec `tests/e2e/learner/mocks-diagnostic-flow.spec.ts` covering the full landing → start → chooser → resume path.
2. Production smoke validation against the live route once the Listening V2 work is merged (since the diagnostic flow shares `/mocks/setup`).

### Effort

Low — just an E2E spec + a production smoke run.

---

## Wave 4 — Expanded admin item analysis

### Current state

`MockService.GetItemAnalysisAsync` exposes per-question difficulty + discrimination index for Reading. Listening has equivalent grading evidence (`ListeningAnswer` per `ListeningItem`) but no analytics endpoint. Writing / Speaking are tutor-graded so item-level analysis is not currently feasible.

### Acceptance criteria

1. New `GET /v1/admin/mocks/{bundleId}/listening-item-analysis` returns per-item difficulty (% correct), discrimination index (top-third minus bottom-third), and distractor frequency.
2. Endpoint reuses `MockService.GetItemAnalysisAsync` shape; difference is the entity it pulls from (`ListeningAnswer` vs `ReadingAnswer`).
3. Admin UI tab on the bundle item-analysis page picks the subtest.
4. AdminContentRead permission (matches existing analytics gates).
5. SQLite test fixture covering 10 attempts × 42 items × correct/incorrect distribution.

### Dependencies

- `ListeningAnswer` / `ListeningItem` schemas already capture the data.
- Adds one endpoint, one service method, one UI tab.

### Effort

Medium — pure read-side work, no schema changes.

---

## Wave 5 — Booking reminders + idempotent worker

### Current state

`MockBooking.ScheduledStartAt` is set when a booking is created but no notification fires before / after the slot. Tutors learn of new bookings only when they refresh their queue.

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
| 3 (E2E smoke) | First — v1.0.1 patch | Lowest effort; closes the only remaining V2 evidence gap. |
| 1 (server-resolved adapters) | v1.1 | Hardens the trust boundary that V2 closure already points to. |
| 5 (booking reminders) | v1.1 | High learner-facing value; modest effort. |
| 4 (Listening item analysis) | v1.1 | Mirror of existing Reading analytics; admin-only impact. |
| 2 (live-room transitions) | v1.2 | Highest scope; needs SignalR auth, learner-state hardening, admin UI. |

## Cross-links

- `docs/mocks/PROGRESS.md` — V2 closure ledger.
- `docs/mocks/PRD.md` — module PRD.
- `docs/MOCKS-RANDOMISATION.md` + `docs/MOCKS-OPTION-ID-MIGRATION.md` — Wave 8 randomisation prerequisites.
- `docs/audits/rulebook-compliance-2026-05-11-closure.md` — broader 2026-05-12 closure manifest.
