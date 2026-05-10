# Writing Rulebook 100% — Progress Log

> **Status: COMPLETE — 2026-05-10.** All 13 tasks shipped.

## Wave 1 — Foundations
- [x] W1A: `AdminWritingDraft` added to `AiFeatureCodes` + `PlatformOnlyFeatures` + `KnownFeatureCodes`.
- [x] W1B: `DbBackedRulebookLoader` registered in `Program.cs` (line 749).
- [x] W1C: 15 missing detectors ported to `WritingRuleEngine` (R05.2/5/6, R06.8/12, R07.1, R09.7/8/9, R10.5/6/8/10, R12.10/11). Engine now 41/41 vs TS.
- [x] W1D: Vitest baseline-count assertion locks 172 rules per profession.

## Wave 2 — AI grading core
- [x] W2A: `IWritingEvaluationPipeline` (+ DI) replaces hardcoded placeholder in `BackgroundJobProcessor`. Routes ALL writing AI grading through `IAiGatewayService` (`Kind=Writing`, `FeatureCode=WritingGrade`). Country-aware `OetScoring.GradeWriting` + `ClampScaled`.
- [x] W2B: `IWritingEntitlementService` + `IWritingOptionsProvider` (kill switch, free-tier monthly quota, window). IMemoryCache backed. Admin GET/PUT endpoints.

## Wave 3 — Coach + draft + admin
- [x] W3A: `WritingCoachService` rewritten — deterministic `WritingRuleEngine` + AI suggestions through gateway, deduped by `ruleId+anchor`, kill-switch honored.
- [x] W3B: `IWritingDraftService` + `POST /v1/admin/writing/ai-draft` + admin AI-draft page.
- [x] W3C: Admin Writing Options page (kill switches, free-tier entitlement, preferred providers).

## Wave 4 — UI + E2E
- [x] W4A: `/writing/feedback` renders `[Rxx.n]` rule-id badges linking to `/writing/rulebook/<ruleId>`.
- [x] W4B: Playwright E2E `tests/e2e/learner/writing-rule-engine-violations.spec.ts` (38 tests across 6 projects).
- [x] W4C: TS↔.NET engine parity fixture + tests.

## Discoverability
- [x] Two new admin sidebar entries under "AI & automation": **Writing AI Options**, **Writing AI Draft**.

## Bug fixes
- [x] `app/writing/feedback/page.test.tsx` — `next/link` mock now spreads `...rest` so `data-testid` reaches `<a>`.

## Final — Verification
- [x] `npx tsc --noEmit` → 0 errors.
- [x] `npx eslint` on all touched paths → clean.
- [x] `npx vitest run` (full) → **170 files / 1095 tests passed**.
- [x] `dotnet test --filter "Writing|RulebookEngine|WritingEngineParity"` → **160/160 passed**.
- [x] `npx playwright test --list <new spec>` → 38 tests discovered.

## Final compliance closure — 2026-05-10
- [x] Source-of-truth decision recorded: embedded 172-rule JSON is operational canonical for v1; attachment byte-fidelity is a separate evidence review.
- [x] Generated coverage matrix added in `lib/rulebook/writing-coverage.ts`; every rule receives deterministic, forbidden-pattern, or structured-AI coverage status.
- [x] Admin Writing rulebook import/publish now runs a backend coverage gate for canonical IDs, severities, detector IDs, forbidden patterns, and critical-rule coverage.
- [x] Learner rule detail page reads the active backend rulebook, preventing static frontend/backend drift when DB rulebooks are published.
- [x] Exam-mode Writing draft/submit APIs enforce the R02.2 five-minute reading-only window server-side.
- [x] Writing grading and coach linting derive case-note markers server-side for marker-dependent rules.

## Acceptance gates
- All AI calls route through `IAiGatewayService.BuildGroundedPrompt` + `CompleteAsync`. ✓
- 172-rule baseline locked by Vitest. ✓
- 41/41 deterministic detectors in .NET. ✓
- Free-tier entitlement enforces monthly cap + window. ✓
- Admin pages discoverable via sidebar. ✓
- Coach uses two-source pattern (deterministic + AI). ✓
- Coverage-gated rulebook compliance and DB publish/import drift gate. ✓
