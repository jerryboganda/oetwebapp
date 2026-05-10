# Writing Rulebook 100% Implementation — PRD

**Status:** complete for v1 launch (coverage-gated, 2026-05-10)
**Goal:** make every learner-facing Writing AI surface route through the grounded gateway with the rulebook embedded; close all 10 audit gaps; admin-configurable AI provider/model per Writing feature; admin-configurable entitlement.

## User-confirmed decisions
1. **AI provider routing**: admin-configurable per Writing feature code via existing `AiFeatureRoutesPanel`. No hard-pinned defaults.
2. **Human review**: AI grades immediately; expert review optional (existing `expert-request` flow).
3. **Free tier quota**: premium-paid only by default; admin-configurable via runtime-mutable `WritingOptions` row (mirrors `AiGlobalPolicy` pattern). Default `FreeTierLimit = 0` (premium only).
4. **DB rulebook overrides**: enable `DbBackedRulebookLoader` in `Program.cs`.
5. **Scope**: all 10 audit items.

## Scope (10 items)
1. Real AI grading: replace `BackgroundJobProcessor.CompleteWritingEvaluationAsync` with grounded gateway call via new `IWritingEvaluationPipeline`.
2. Port 15 missing `.NET` detectors + TS↔.NET parity test.
3. Rewrite `WritingCoachService` to use grounded AI gateway.
4. Register `DbBackedRulebookLoader`.
5. Add `AdminWritingDraft` feature code + `IWritingDraftService` + admin AI-draft page.
6. Vitest baseline-count assertion (172 rules per profession).
7. Playwright E2E: submit violating letter, assert rule findings.
8. `WritingOptions` config + kill-switch + `IWritingEntitlementService`.
9. Frontend: surface rule-cited findings in `/writing/feedback`.
10. Admin per-feature AI provider routing UI (extend existing panel for model picker).

## Architecture
- New: `IWritingEvaluationPipeline` (scoped) — same shape as `ISpeakingEvaluationPipeline`.
- New: `IWritingEntitlementService` — same shape as `IGrammarEntitlementService`.
- New: `IWritingDraftService` — same shape as `IGrammarDraftService`.
- New: `WritingOptions` runtime singleton row + `IWritingOptionsProvider`.
- Reuse: `Attempt` + `Evaluation` entities, `WritingCoachSuggestion`, `IAiGatewayService`.

## Acceptance criteria
- [x] `npx tsc --noEmit` → 0 errors in latest completion sweep.
- [x] Focused Vitest includes baseline, profession, and coverage-matrix gates.
- [x] Focused backend tests include rule engine, Writing evaluation, coach, coverage validator, and exam reading-window enforcement.
- [x] AI grading runs through gateway with `WritingGrade` feature code; `AiUsageRecord` row written by the standard gateway path.
- [x] Admin can set per-Writing-feature provider routes via `/admin/ai-providers`.
- [x] Admin can toggle Writing kill-switch + free-tier quota at `/admin/writing/options`.
- [x] Playwright spec submits a violating letter and asserts rule citations.
- [x] Admin Writing rulebook import/publish is coverage-gated against the canonical 172-rule baseline.
- [x] Learner rule detail reads the active backend rulebook to avoid DB/static drift.
- [x] Exam-mode Writing draft/submit APIs enforce the five-minute reading-only window server-side.
