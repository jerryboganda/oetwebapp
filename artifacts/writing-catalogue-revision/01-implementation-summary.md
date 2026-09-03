# Writing Catalogue Revision — Implementation Summary

Date: 2026-09-04. Scope: candidate-facing Writing catalogue taxonomy only
(library, showcase, onboarding focus, admin task tooling, backend taxonomy,
classifier normalizers, AI authoring prompt, data migration). Later
AI-grading / model-answer requirements are explicitly out of scope and were
not implemented here.

## Decisions

1. **Difficulty**: the candidate-facing Writing catalogue (practice library,
   showcase) already presented only Profession + Letter Type + Search — no
   Difficulty filter, no Level 1–5 badges exist anywhere in the Writing
   catalogue (verified by repo-wide search). No catalogue UI change was
   needed. `Difficulty` (int 1–5) is retained as an internal sequencing field
   (adaptive practice ±1 band, DB column, admin authoring metadata) because
   removing it would break adaptive practice and it is never catalogue-visible.
   Drill `difficulty` (`core`/`exam`) is a separate concept, also retained.
2. **Response (LT-RP)**: retired. The frontend never contained it; four
   backend services still produced/accepted it. All now route through a new
   single source of truth.
3. **Other Letters (LT-OT)**: already present in frontend contracts; added to
   every backend allowlist/default/normalizer and to the two catalogue
   surfaces that lacked it (showcase, onboarding focus).
4. **Source of truth**: new `WritingLetterTypeTaxonomy` (backend,
   `Services/Writing/`). All normalizers delegate to it; valid set is exactly
   `{LT-RR, LT-UR, LT-DG, LT-TR, LT-NM, LT-OT}`. Unknown / ambiguous /
   retired-Response inputs normalize to `LT-OT` — never to a guessed known
   type, never to `LT-RP`.
5. **Grading-lens heuristics left untouched**: `inferWritingLetterType`
   (`lib/rulebook/context.ts`), `WritingTaskUnderstandingService`, the
   rulebook `LetterType` union and ContentPaper canonical ids feed AI
   grounding / rule selection, not catalogue classification. Changing their
   defaults would alter grading for all existing tasks — out of scope, high
   risk. Documented in `04-classification-logic.md`.
6. **Data migration**: hand-authored EF data migration
   `20261216090000_ReclassifyWritingResponseLetterType` (repo convention:
   no Designer file for data-only migrations). Idempotent, column-scoped to
   `WritingScenarios.LetterType`. Runs automatically on deploy
   (`AUTO_MIGRATE=true`).
7. **Concurrent work observed**: a second active session is editing the same
   tree (model-answer / grading flows + test fixtures). Its changes to shared
   files are compatible (it extended the new taxonomy with `ToPackLetterType`
   and fixed an EF GroupBy issue). Details + risk note in
   `07-final-verification.md`. stray test-output files of mine were removed.

## Files I changed

Backend (`backend/src/OetLearner.Api/`):
- `Services/Writing/WritingLetterTypeTaxonomy.cs` — NEW source of truth.
- `Services/Writing/WritingLearnerPathwayService.cs` — normalize/legacy/defaults via taxonomy; pharmacy default `LT-RP→LT-OT`.
- `Services/Writing/WritingOnboardingService.cs` — focus allowlist `LT-RP→LT-OT`.
- `Services/Writing/WritingPathwayGenerator.cs` — pharmacy default `LT-RP→LT-OT`.
- `Services/Writing/WritingSubmissionEvaluationPipeline.cs` — rulebook normalizer: `LT-RP→advice_to_patient` removed, `LT-OT→other` added (later refactored by peer session to delegate to `ToPackLetterType`; ban preserved).
- `Services/Writing/WritingTaskAuthoringService.cs` — `ApplyUpsert` normalizes non-empty letter types; publish gate rejects unsupported codes (`letter_type_unsupported`); import `MapTaskTypeToLetterType` rewritten to catalogue codes with `LT-OT` fallback (this file also carries the peer session's publish-gate/preparation-status work).
- `Services/Writing/WritingScenarioService.cs` — legacy admin `ToScenarioView` normalizes letter type (file also carries peer session's GroupBy fix).
- `Prompts/Writing/WritingPromptTemplates.cs` — `ScenarioGenerateV1` allowed-list + `LT-RP` ban + `LT-OT` fallback instruction.
- `Services/Rulebook/WritingPromptTemplateRegistrar.cs` — scenario-generate output schema enumerates the six codes.
- `Data/Migrations/20261216090000_ReclassifyWritingResponseLetterType.cs` — NEW data migration.

Frontend:
- `app/writing/showcase/page.tsx` — letter-type filter gains `LT-OT`.
- `app/writing/profile-setup/focus/page.tsx` — focus options gain `LT-OT`.
- `app/admin/writing/analytics/page.tsx` — label fixes (`Referral→Routine referral`, `New management→Non-medical referral`).
- `components/domain/writing/admin/builder-state.ts` — NEW exported `writingLetterTypesForProfession()` (OT for every profession; vet excludes only LT-NM).
- `components/domain/writing/admin/WritingTaskBuilder.tsx` — uses the shared helper (behavior identical).

Tests:
- `backend/tests/.../Writing/WritingLetterTypeTaxonomyTests.cs` — NEW, 36 tests.
- `backend/tests/.../Writing/WritingExamClosureTests.cs` — import/clone expectations updated to catalogue codes; NEW import→`LT-OT` theory (7 cases) + publish-gate-blocks-`LT-RP` test.
- `lib/writing/letter-type-taxonomy.test.ts` — NEW, 7 tests.

## Verification

- `dotnet build` (solution): 0 errors.
- New backend taxonomy tests: 36/36 pass. Targeted Writing set (taxonomy + pack + closure + understanding + pathway + scenario-list): 97/97 pass.
- Frontend: `tsc --noEmit` clean; eslint on touched files 0 errors; vitest suites (new taxonomy, zod, rulebook context, TaskBuilder, admin analytics): all pass.
- Pristine-HEAD baseline worktree confirmed all unrelated failures pre-exist (19/19 sampled).
- Remaining suite failures are pre-existing/environmental (CriticalFlows 409/entitlement integration, pre-existing AuthorId fixtures, rule-engine data drift) — none caused by this change; full matrix in `05-acceptance-test-evidence.md`.
