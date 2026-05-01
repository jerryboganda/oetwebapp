# Mocks Module — V2 Enhancement Plan (Spec-Aligned)

> **Source spec:** `OET_Mocks_Module.md` (28 sections, attached by user).  
> **Predecessor:** `docs/MOCKS-MODULE-PLAN.md` (Phases A–C shipped — resume cards, profession
> filter, readiness, score-guarantee, cohort percentile).
> **Owner:** Platform Engineering. **Goal:** raise the Mocks module from "functionally correct
> orchestration" to "premium OET-style assessment ecosystem" matching the 28-section product spec
> without breaking existing mission-critical contracts (Scoring, Rulebook, AI Gateway, Billing,
> Expert Console).

---

## 1. Spec → Current State Gap Matrix

Legend: ✅ shipped · 🟡 partial · ❌ missing · 🔒 governance/policy only.

| Spec § | Capability | State | Notes |
|--------|------------|-------|-------|
| §1 Mock types | Full / LRW / Speaking / Sectional / Part / Diagnostic / Final-readiness / Remedial | 🟡 | `MockBundle.MockType` is a free-form string; only `full` + sub-test bundles in use. No `lrw`, `part`, `diagnostic`, `final_readiness`, `remedial` enum or admin UI. |
| §2 Delivery model | Paper / Computer / OET@Home | ❌ | No `DeliveryMode` field on bundle/attempt. |
| §3 Full mock structure | L → R → W → S, optional break after Reading Part A | 🟡 | Section ordering enforced; break flag missing. |
| §4 Practice vs Mock mode | Replay/pause/hints toggles per mode | 🟡 | `MockAttempt.Mode = "exam"` exists; no `practice` mode wiring through to grader behaviour. |
| §5A Diagnostic Mock | First-test flow + auto study path | 🟡 | `MockDiagnosticService` exists; **no learner-facing diagnostic UI** and no auto study-path seeding. |
| §5C Part Mock | Reading Part A only, Listening Part C only, etc. | ❌ | Bundle data model supports it (single section), but no admin distinction or learner filter. |
| §6 Listening (one-play, hidden transcript, audio issue report) | 🟡 | One-play already enforced in Listening grader; transcript hidden; **no in-player "Report audio issue" affordance**. |
| §7 Reading (Part A 15-min lock) | ✅ | Part A lock + Parts B/C 45-min shared timer enforced server-side. |
| §8 Writing (5-min reading + 40-min writing, no grammar AI) | 🟡 | Strict-mode timer wired; **grammar-assist suppression in `mock` mode not explicitly enforced**. |
| §9 Speaking (live interlocutor room + recording) | 🟡 | `SpeakingMockSet` exists with two role-plays; **no live tutor room, no interlocutor-card hiding, recording optional**. |
| §10 Scoring (raw → 0–500 scale, B≈350) | ✅ | `OetScoring` is canonical; never bypassed. |
| §11 Result release (instant / after teacher / scheduled) | 🟡 | Auto-marked sections release instantly; **no scheduled release flag**. |
| §12 Mock report (overall, parts, skills, timing, errors, drills, retake/booking advice) | 🟡 | `MockReport.PayloadJson` includes scores + readiness; **timing analysis, error categories, retake advice, booking advice missing**. |
| §13 Readiness rating (Red/Amber/Green/Dark-green per module + overall) | 🟡 | Readiness tier shipped at recommendation level; **per-module RAG not surfaced on report**. |
| §14 Mock scheduling | ❌ | Notification key `LearnerMockScheduled` exists; **no `MockBooking` entity/endpoints/UI**. |
| §15 Proctoring (full-screen, tab-switch, mic/cam check, ID, time logs, plagiarism, AI-use) | ❌ | None implemented. |
| §16 Content security (ownership, source, copyright, leak report, watermark, randomisation, retire) | 🟡 | `SourceProvenance` field exists; no leak-report flow, no watermark, no randomisation. |
| §17 Mock content builder (admin) | 🟡 | Bundle/section CRUD exists; **profession + difficulty + topic + skill-tag metadata partial**. |
| §18 QA workflow (item analysis, pilot, retire/update) | ❌ | No item-analysis aggregation, no pilot test stage. |
| §19 Student dashboard | 🟡 | `/mocks` shows latest scores + readiness; **best/weakest module, common-issue, next-assignment, progress trend missing**. |
| §20 Teacher dashboard | 🟡 | Expert console queues exist; **class-mock heatmap + risk list + tutor-marking-consistency missing**. |
| §21 Admin dashboard | 🟡 | Some admin analytics exist; **mock-completion-rate, average-readiness, pass-prediction, marking-delay missing**. |
| §22 Mock packages (billing) | 🟡 | Billing catalog supports plans; explicit "free diagnostic / sectional / writing-correction / speaking / full / final-week" packages need catalog SKUs. |
| §23 Auto remediation 7-day plan | ❌ | Remediation service exists; **not auto-seeded from `MockReport` weakness tags**. |
| §24 Data structure | 🟡 | `mock_tests`, `mock_sections`, `mock_items` (via `MockBundleSection.ContentPaperId`), `attempts`, `section_attempts`, `responses` (via Reading/Listening tables), `writing_submissions`, `speaking_sessions`, `teacher_marks`, `score_estimates` (in `MockReport.PayloadJson`), `analytics`, **missing: `remediation_tasks` link to mock, `proctoring_logs`, `content_reviews`**. |
| §25 Report template | 🟡 | Statement-of-Results card exists; mock-report payload extension needed. |
| §26 Behaviours trained | 🟡 | Most enforced; "answer all" warning + "guess if unsure" reminder missing. |
| §27/28 Polish + business advice | 🔒 | Marketing surface; out-of-scope for engineering plan. |

**Top 8 high-value gaps** (ordered by leverage × low blast-radius):
1. **Mock-type taxonomy enum** + `lrw` / `diagnostic` / `final_readiness` / `remedial` / `part` support (admin + learner filter).
2. **Proctoring events** (full-screen exits, tab-switches, paste detection, audio/mic/cam checks).
3. **Item analysis** aggregation per `MockBundleSection` × `ContentPaper` for admin QA.
4. **Mock scheduling/booking** entity + reminder hooks + speaking-room calendar.
5. **Auto 7-day remediation plan** seeded from mock-report weakness tags.
6. **Speaking live role-play room** (interlocutor card hidden from candidate, tutor view exposes card, recording mandatory).
7. **Diagnostic mock learner flow** + auto study-path generation on completion.
8. **Content security**: leak-report button, watermark token on PDFs, question randomisation seed per attempt.

---

## 2. Architecture Principles (Hard Constraints)

These come from `AGENTS.md` MISSION-CRITICAL contracts and **must not be violated**:

1. **Scoring** — every raw↔scaled conversion, every pass/fail decision routes through `OetScoring` (.NET) / `lib/scoring.ts` (TS). Mock-report payload only stores results — never recomputes.
2. **Rulebooks** — Writing/Speaking rule citations on the mock report must come from `lib/rulebook` / `OetLearner.Api.Services.Rulebook`. No new JSON reads.
3. **AI Gateway** — any AI feedback in the mock report routes through `IAiGatewayService.BuildGroundedPrompt`. Add new feature codes (`AiFeatureCodes.MockReportInsight`, `MockRemediationDraft`) — do not bypass.
4. **Reading authoring** — exact-match grading; learner DTOs must never expose answer keys. Already enforced; do not regress.
5. **AudIO/files** — all audio I/O via `IFileStorage`, content-addressed SHA-256.
6. **Billing** — review credits flow only through existing wallet/reservation lifecycle. Score guarantee remains billing-owned.
7. **Migrations** — every new entity gets a migration; never recreate `oet_postgres_data`.

---

## 3. Wave Plan

Each wave is shippable independently, has its own migration (if needed), tests, and rollback note.

### Wave 1 — Mock-type taxonomy + mode contract (no schema break)
- **Backend**
  - Add `MockTypes` static class with constants: `full`, `lrw`, `subtest`, `part`, `diagnostic`, `final_readiness`, `remedial`. Validate `MockBundle.MockType` and `MockAttempt.MockType` against it.
  - Add `MockAttempt.DeliveryMode` (`computer` default, `paper`, `oet_home`) and `MockAttempt.Strictness` (`learning`, `exam`, `final_readiness`).
  - Migration `AddMockTypeTaxonomy`: nullable columns with `computer`/`exam` defaults (backwards-compatible).
  - Expose new fields in `MockAttemptCreateRequest` and `/v1/mocks` bundle response.
- **Frontend**
  - `app/mocks/setup` — show mock-type chips with explanatory copy ("Diagnostic — find your level", "LRW — three sections in one sitting", "Part — focused 15-minute Part A drill").
  - `app/mocks/page.tsx` — group bundles by mock-type tab.
- **Tests**
  - `MockServiceTests.MockType_*`: rejects invalid types, persists `DeliveryMode`/`Strictness`.
  - `app/mocks/setup/page.test.tsx`: chip click changes type filter.
- **Rollback** — drop new columns, revert validation to free-form.

### Wave 2 — Proctoring events
- **Backend**
  - New entity `MockProctoringEvent` (`Id`, `MockAttemptId`, `MockSectionAttemptId?`, `Kind` enum, `OccurredAt`, `MetadataJson`, `Severity`).
  - `Kind`: `fullscreen_exit`, `visibility_hidden`, `tab_switch`, `paste_blocked`, `mic_check_passed`, `mic_check_failed`, `cam_check_passed`, `cam_check_failed`, `audio_issue_reported`, `network_drop`, `multiple_displays_detected`.
  - Endpoint: `POST /v1/mock-attempts/{id}/proctoring-events` (rate-limited, 100 events / attempt).
  - Aggregated count exposed on `MockReport.PayloadJson.proctoring` (no PII).
  - Mission-critical: never block submission on proctoring failure — only flag for tutor review.
- **Frontend**
  - New hook `useMockProctoring()` in `lib/hooks/`: fullscreen API, `document.visibilityState`, paste blocker, beforeunload warning.
  - Mic/cam pre-check modal in `app/mocks/player/[id]/page.tsx` and Speaking pre-room.
  - "Report audio issue" button in Listening player → posts `audio_issue_reported`.
- **Tests** — vitest hook test, endpoint integration test, rate-limit test.
- **Rollback** — drop table, remove hook.

### Wave 3 — Item analysis + admin QA dashboard
- **Backend**
  - New view (or scheduled aggregation entity) `MockItemAnalysisSnapshot`: per `ContentPaperItemId`, % correct, sample size, distractor distribution, avg time spent, last-30-days window.
  - Endpoint: `GET /v1/admin/mocks/item-analysis?bundleId=&paperId=` (perm: `ManageContent`).
- **Frontend**
  - New page `app/admin/content/mocks/[bundleId]/item-analysis/page.tsx` with sortable table + flags for "too easy" (>95%), "too hard" (<10%), "tempting distractor" (>40% on one wrong option).
- **Tests** — service unit test on aggregation; admin page snapshot.
- **Rollback** — feature flag `Admin:MockItemAnalysis`.

### Wave 4 — Mock scheduling / booking
- **Backend**
  - Entity `MockBooking` (`Id`, `UserId`, `MockBundleId`, `ScheduledStartAt`, `TimezoneIana`, `Status` enum, `AssignedTutorId?`, `AssignedInterlocutorId?`, `RescheduleCount`, `CreatedAt`).
  - Endpoints: `POST/GET/PATCH /v1/mock-bookings`, admin `GET /v1/admin/mocks/bookings`.
  - Reminder hook fires `LearnerMockScheduled` notification 24 h and 1 h before start (uses existing notification frequency caps).
- **Frontend**
  - `app/mocks/setup` — add "Book this mock" calendar picker for Speaking + Final-readiness.
  - Speaking pre-room: waiting-room countdown + "Tutor will join shortly".
- **Tests** — booking lifecycle, reminder cap respect.
- **Rollback** — entity flag `Mocks:Bookings:Enabled`.

### Wave 5 — Auto 7-day remediation plan
- **Backend**
  - On `MockReport` generation, derive `weaknessTags[]` from per-criterion deltas (Reading-Part-C-inference, Listening-Part-A-spelling, Writing-purpose, etc.). Map via static `RemediationCatalog` (skill-tag → recommended drill IDs).
  - Seed up-to-7 `RemediationTask` rows linked to user with day offsets.
  - Idempotent: if a plan already exists for the attempt, do not seed again.
  - AI-assisted personalisation routes through new `AiFeatureCodes.MockRemediationDraft`.
- **Frontend**
  - `app/mocks/report/[id]` — surface "Your next 7-day plan" card mirroring spec §23.
- **Tests** — weakness-tag → drill mapping coverage; idempotency; rulebook/AI gateway invocation asserts.
- **Rollback** — feature flag; unseen drill rows are harmless.

### Wave 6 — Speaking live role-play room
- **Backend**
  - Extend `SpeakingMockSet` attempt with `LiveRoomState` enum (`waiting`, `in_progress`, `completed`, `tutor_no_show`).
  - Endpoint: `POST /v1/speaking/mock-sets/{id}/attempts/{attemptId}/transition` (server-authoritative).
  - Tutor view exposes `interlocutorCardJson`; learner view strictly forbids it (DTO projection enforces).
  - Recording uploaded chunked to `IFileStorage`; mandatory consent stored on `MockBooking`.
- **Frontend**
  - `app/mocks/speaking-room/[bookingId]/page.tsx` — pre-room (consent → mic/cam check) → role-play 1 (3 min prep + 5 min speak) → role-play 2 → submit.
  - Tutor variant under `app/expert/speaking-room/[bookingId]/page.tsx`.
- **Security** — interlocutor card MUST NOT be returned by learner-scoped endpoint (regression test).
- **Rollback** — feature flag `Speaking:LiveRoom`.

### Wave 7 — Diagnostic mock learner flow + study-path
- **Backend**
  - Hook `MockDiagnosticService.CompleteAsync` → emits `DiagnosticStudyPath` (per-skill priority list) and seeds remediation tasks (reuses Wave 5 catalog).
  - New learner endpoint `GET /v1/mocks/diagnostic/study-path`.
- **Frontend**
  - `app/mocks/diagnostic/page.tsx` — landing → start → result → personalised study path with CTAs into `/listening`, `/reading`, `/writing`, `/speaking` modules.

### Wave 8 — Content security
- **Backend**
  - Add `MockBundle.SourceStatus` enum (`original`, `licensed`, `official_sample`, `needs_review`).
  - Endpoint `POST /v1/mocks/leak-report` → creates `ContentReview` with `Severity = high`.
  - Add per-attempt `RandomisationSeed` (uint32) — graders honour it for question shuffling where rulebook allows.
  - PDF export attaches watermark `userId|attemptId|timestamp` (Writing case-notes only).
- **Frontend**
  - "Report leak" button on `/mocks/report/[id]`.
  - Watermark visible on printable mock PDFs.

---

## 4. Cross-cutting concerns

- **Migrations**: each wave introduces at most one migration; named `Mocks_V2_W{n}_*`.
- **Feature flags**: every wave guarded behind a config flag in `MocksOptions` so canary rollout is possible.
- **Telemetry**: each wave emits `analytics.event` rows (`mock_type_filter_changed`, `proctoring_event_recorded`, `item_analysis_viewed`, `mock_booking_created`, `remediation_plan_seeded`, `speaking_room_joined`, `diagnostic_completed`, `leak_reported`).
- **Audit**: admin mutations write `AuditEvent` rows (existing infra).
- **A11y**: every new surface follows `DESIGN.md` + AA contrast; speaking room must have keyboard-accessible mic mute/end-call controls.
- **i18n**: copy strings centralised in existing translation pipeline.

---

## 5. Verification & Rollout

For each wave:
1. `npx tsc --noEmit` clean.
2. `npm run lint` clean.
3. `npm test` — only added/touched suites must pass; full suite green before merge.
4. `dotnet build backend/OetLearner.sln` clean.
5. `dotnet test backend/OetLearner.sln` — wave-specific suites + full suite green.
6. Targeted Playwright smoke for any new learner page.
7. Manual smoke against local Postgres.
8. Rollout via feature flag `Mocks:V2:Wave{n}=true` per environment (staging first).

---

## 6. Out of Scope

- Real-time co-watching of a mock by tutor + student outside the speaking room.
- AI-driven proctoring (gaze tracking, voice ID) — privacy-sensitive; deferred.
- Automated score-guarantee refund (billing-owned).
- New external content licensing deals — product/legal track.

---

## 7. Open Questions for Stakeholder

1. Should the diagnostic mock be **gated free** (one-time) or available to every plan?
2. Does Speaking live-room require a video stream or is audio-only acceptable for v1?
3. Watermark + randomisation: are these required for launch or post-launch?
4. Item-analysis dashboard audience: admins only, or shared with senior tutors?

> Default assumption if no answer: gated free diagnostic (per spec §22), audio-only v1 (matches OET@Home), watermark/randomisation post-launch (defer to Wave 8), admin-only for v1 (Wave 3).

---

## 8. Stakeholder Decisions (locked)

- **Wave order**: all 8 waves in sequence W1 → W8.
- **Speaking room**: **audio-only** for v1 (no video).
- **Diagnostic mock gating**: **NOT hardcoded** — fully admin-configurable as a property of each subscription package in the billing admin UI. Wave 7 must therefore add `BillingPlan.DiagnosticMockEntitlement` (`unlimited` | `one_per_lifetime` | `one_per_renewal_period` | `paid_per_use` | `disabled`) and an admin-panel control on the plan-edit page.
- **Implementation pace**: end-to-end, all waves, no pauses.
