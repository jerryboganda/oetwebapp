# Writing Module Implementation Loop

Last updated: 2026-05-27

## Resume Protocol

If the coding session stops because of GitHub Copilot/provider limits, resume after a 15-minute cooldown by reading this file, `PROGRESS.md`, and `git status --short`, then continue from the first unchecked item in the current wave. Do not restart from scratch and do not revert unrelated dirty files.

Validation is currently paused because the user asked not to run validations. Use editor diagnostics and read-only review while this instruction remains active. If the user re-enables validation, run only Docker Desktop or GitHub Actions checks, never host builds/tests and never VPS validation.

## Implementation Principle

Implement the full Writing plan as additive layers over the existing Writing attempt/evaluation/grading contracts. Do not create parallel submission or grading systems that duplicate `Attempt`, `Evaluation`, `WritingEvaluationPipeline`, `OetScoring`, rulebook grounding, AI usage recording, billing entitlements, or tutor review primitives.

## Completed Baseline

- Learner Writing profile, pathway, today plan, canon response contracts.
- Backend pathway service, endpoints, migration, DbContext, and Program registration.
- Frontend typed client, Writing dashboard pathway entry, profile setup, pathway, today, and canon pages.
- Focused test files added but not executed after validation pause.
- Read-only review findings from the baseline slice fixed.

## Specialist-Agent Findings Captured

- Ten read-only specialist agents mapped the remaining plan into lanes: backend diagnostic/editor spine, frontend player reuse, wave sequencing, lessons/drills, exemplars/mistakes/canon, analytics/mocks, AI/security, admin authoring, mobile/offline, and independent baseline review.
- Key safe-design decision: Wave 1 diagnostic must use existing `Attempt`, `Evaluation`, `WritingEvaluationPipeline`, timer guards, rulebook grounding, and result pages. No parallel diagnostic grading table.
- Baseline blocker fixed before Wave 1: learner canon now projects from canonical Writing rulebook services instead of hard-coded `SC-*` rules.
- Baseline blocker fixed before Wave 1: Writing pathway target country now normalizes through `OetScoring.NormalizeWritingCountry`, mirrors to existing learner goals when present, and the grading pipeline reads the Writing profile country before falling back to `LearnerGoal`.
- Baseline blocker fixed before Wave 1: pathway task selection no longer falls back to an unrelated profession's Writing task.
- Baseline blocker fixed before Wave 1: `lib/writing-pathway-api.ts` now wraps shared `apiClient` rather than duplicating auth/CSRF/retry behavior.

## Wave 1 — Diagnostic And Editor Spine

- [x] Route pathway diagnostic launches through existing Writing attempts using `Context = diagnostic` and `Mode = exam`.
- [x] Keep diagnostic grading/result flow on existing Writing submit/evaluation/result endpoints.
- [x] Scope `DiagnosticCompleted` / `LastDiagnosticEvaluationId` to completed evaluations joined to diagnostic-context exam attempts only.
- [x] Reuse existing server-side 5-minute reading-window guard for diagnostic-context exam attempts.
- [x] Build learner diagnostic landing/submitted/results shell pages that route into the current player/result surfaces.
- [x] Add explicit diagnostic state/timing response DTOs if product needs a standalone API status panel beyond the existing attempt/evaluation state. Current backend/frontend contracts expose diagnostic session state and timing through the Writing V2 diagnostic session DTOs.
- [x] Extract a reusable Writing editor shell for diagnostic/mock/practice pages, keeping the current player intact. `WritingEditorV2` is the shared shell now used across diagnostic/mock/practice/revision surfaces.
- [x] Add stronger local/offline draft recovery hooks using existing Writing draft APIs where available.

## Wave 2 — Foundation Lessons And Skill Tree

- [x] Add W1-W8 lesson/content contracts using existing content primitives where possible.
- [x] Add learner lesson list/detail/progress endpoints.
- [x] Seed starter W1-W8 lesson metadata and quiz stubs with clearly editable authored content.
- [x] Build `/writing/skill-tree` and `/writing/lessons/[id]` pages.
- [x] Add lesson completion and quiz score tracking.
- [x] Post-review hardening: plan item ownership now returns learner-safe 404s, quiz scores validate in service code, and first-use starter seeding tolerates unique-key races.
- [x] Reviewer migration warning about extra unmigrated profile/pathway columns was checked against current files and did not apply; no such properties exist in `WritingPathwayEntities.cs`.

## Wave 3 — Drills And Practice Engine

- [x] Add sentence drill and case-note drill contracts/entities only where existing content models cannot represent them.
- [x] Add drill listing/detail/attempt endpoints with deterministic grading first.
- [x] Extend today-plan generation with sentence drills, case-note drills, exemplar review, and canon refresher items.
- [x] Add `/writing/drills`, `/writing/drills/practice/[id]`, and `/writing/case-notes-drills` pages.
- [x] Add skip-pattern and recency-aware selection metadata.
- [x] Post-review hardening: backend-backed drill details moved to `/writing/drills/practice/[id]` to avoid the legacy `/writing/drills/[type]` route, `/v1/writing` pathway aliases were removed to avoid V2 API contract collisions, drill details no longer expose answer keys, and missing answer keys fail closed.
- [x] Re-review hardening: stale duplicate drill-index implementations removed and case-note feedback returns normalized labels only.
- [x] V2 compatibility: existing `/v1/writing/drills` endpoints now bind to shared service methods/interfaces instead of colliding with pathway drill additions.
- [x] Contract cleanup: duplicate case-note drill interface avoided; V2 and pathway case-note routes now use the existing case-note service, while admin drill routes bind to admin methods on `IWritingDrillService`.

## Wave 4 — Exemplars And Mistakes

- [x] Add exemplar contracts and learner-safe exemplar selection over existing published Writing content/assets where possible.
- [x] Add side-by-side exemplar comparison page/component.
- [x] Add common mistakes library and learner-specific mistake frequency views from `WritingRuleViolation` analytics.
- [x] Add `/writing/common-mistakes` and `/writing/common-mistakes/mine` pages.
- [x] Post-review hardening: learner exemplar reads are published-only, admin draft access is preserved, admin publish/status transitions can produce learner-visible exemplars, the placeholder test-grade gate reports `PassesQualityBar = false`, and learner mistake stats merge existing per-learner stats with `WritingRuleViolation` analytics.

## Wave 5 — Analytics, Readiness, And Mocks

- [x] Add Writing stats/readiness contracts and service methods from recent evaluations, violations, timing, and plan completion.
- [x] Add `/writing/stats` dashboard page.
- [x] Add Writing mock listing/session/result surfaces that reuse existing mock/evaluation infrastructure.
- [x] Add readiness widget to the Writing dashboard.
- [x] Post-review hardening: stats/readiness filters now re-check learner ownership on letter-type/type-consistency/mock-grade paths, skill mastery returns UI-ready percentages, band-history targets use raw-score chart values, mock writing phases are persisted and server-timed, mock submit enforces minimum word count and 40-minute expiry, duplicate submits transition through a guarded grading state, mock grading is awaited instead of captured in background `Task.Run`, results can poll pending grades, and reused idempotency grades are materialized for the current mock submission before results are exposed.

## Wave 6 — AI Coach, Appeals, OCR, Tutor, Community

- [x] Add AI Coach contracts and endpoint routed through the grounded AI gateway with usage recording.
- [x] Add score appeal request/status flow over existing billing/AI usage contracts.
- [x] Add paper-mode OCR upload/status surfaces through storage abstractions, no raw file writes.
- [x] Add Writing tutor review queue/request integration using existing tutor/review primitives.
- [x] Add opt-in showcase/community shell pages without exposing private letters by default.
- [x] Post-review hardening: coach hints are sanitized and use direct API-origin realtime with HTTP fallback, grade-ready events now bridge to SignalR, idempotent grade reuse materializes per-submission grades in the pipeline, appeals preserve original grades and link adjusted grades, OCR validates ownership/type/size/magic bytes and stores through `IFileStorage`, tutor review details use an expert-scoped claimed-assignment endpoint, tutor claim/submit enforce ownership and terminal states, and showcase submission requires opt-in, A-grade status, moderation, and PII redaction gates.

## Wave 7 — Admin Authoring

- [x] Load admin discipline before touching admin Writing UI. Hallmark skill was unavailable in this workspace/tool surface, so broad admin UI additions/redesigns are blocked unless the user approves a documented substitute from `docs/admin-redesign/`.
- [x] Add admin scenario/exemplar/drill/canon/lesson/mistake management surfaces over existing admin content/upload services. User approved completing the remaining work with the documented `docs/admin-redesign/axelit-study/` substitute after Hallmark remained unavailable; admin UI now covers the Writing hub, ContentPaper queue, scenarios, exemplars, canon, drills, lessons, mistakes, and audit.
- [x] Add content audit/review workflow wiring only where no equivalent exists. Backend admin Writing endpoints now retain `AdminOnly`, add granular content read/write/publish/audit policies, audit `/audit` views, and persist scenario/exemplar/drill/canon/lesson/mistake mutation audit rows in the same `SaveChangesAsync` call as the content change.

## Current Completion Notes

- Writing module code implementation is complete against the tracked waves in this ledger.
- Current validation remains limited to editor diagnostics and read-only review because the user paused Docker/build/test validation. Do not claim fresh Docker green until validation is re-enabled and run in local Docker Desktop.
- Remaining open items are content/operations dependencies, not missing code paths: Dr Ahmed-approved content scale, full Arabic translation beyond the current chrome pass, and native mobile proof across real devices.

## Open Content Dependencies

Large banks of authored scenarios, exemplars, drills, lessons, mistakes, quizzes, and expanded canon rules require real Dr Ahmed-authored or approved content. Code can ship editable starter/stub content and import/admin surfaces, but it must not fabricate final medical exam content as if approved.