# Writing Authoring & Admin CRUD — Implementation Plan

> **Status:** in implementation. Companion to `docs/CONTENT-UPLOAD-PLAN.md`,
> `docs/RULEBOOKS.md`, `docs/SCORING.md`, and `docs/AI-USAGE-POLICY.md`.
>
> This document governs Writing content papers and admin CRUD only. It does not
> replace the rulebook, scoring, grounded-AI, or content-upload contracts.

## Product Goal

Admins must be able to create, edit, validate, publish, archive, and import OET
Writing tasks from the real source structure: one case-notes stimulus plus one
model-answer/reference sheet, tagged by profession and canonical letter type.
Learners must then see those published tasks through the existing Writing
practice workflow without model-answer leakage before submission.

## Current Flow

- Generic `ContentPaper` CRUD exists under `/v1/admin/papers` and supports
  `SubtestCode = "writing"`, `LetterType`, `CaseNotes`, and `ModelAnswer` assets.
- The active learner Writing player still consumes legacy `ContentItem` tasks
  from `/v1/writing/tasks` and `/v1/writing/tasks/{contentId}`.
- Before this plan, publishing a Writing `ContentPaper` did not create a learner
  task, so real admin-authored Writing papers could remain invisible to learners.

## Target Flow

1. Admin creates a Writing paper from `/admin/content/writing` or
   `/admin/content/papers?subtest=writing`.
2. Admin assigns profession scope, source provenance, access tier, difficulty,
   duration, and canonical letter type.
3. Admin attaches primary `CaseNotes` and `ModelAnswer` assets through the
   existing chunked upload / `IFileStorage` path.
4. Admin authors reviewed structure in `ContentPaper.ExtractedTextJson` under
   `writingStructure`: task prompt, case notes text, recipient, purpose, writer
   role, criteria focus, and model answer text.
5. Publish gate requires source provenance, required primary assets, letter type,
   task prompt, case notes text, and model answer text.
6. Publish projects the `ContentPaper` into a compatible `ContentItem`, keeping
   existing learner attempts, drafts, submissions, model answers, and expert
   review flows stable.
7. Archive hides both the canonical paper and the projected learner task.

## Mission-Critical Invariants

- Writing tasks are authored as `ContentPaper` rows with `SubtestCode = "writing"`.
- Writing CRUD must use `/v1/admin/papers/*`, upload endpoints, and `IFileStorage`.
- Required Writing assets are primary `PaperAssetRole.CaseNotes` and
  `PaperAssetRole.ModelAnswer`.
- Learner task endpoints may expose case notes and task metadata only; model
  answers are post-submit study material.
- UI and endpoints must not read rulebook JSON directly or hard-code rule text.
- Any Writing AI grading, feedback, coaching, or extraction must use the grounded
  gateway with canonical scoring context, profession, candidate country, and
  `LetterType`.

## Implementation Slices

- **Slice 1 complete:** Backend `WritingContentStructure`, admin structure
  endpoints, publish validation, publish-to-learner projection, archive hiding,
  dedicated admin Writing workspace, and paper-detail Writing editor.
- **Slice 2 next:** Replace placeholder Writing evaluation with grounded AI
  gateway flow and country-aware scoring helpers.
- **Slice 3 next:** Add PDF text extraction review/backfill so uploaded case-note
  and answer-sheet PDFs can prefill authored structure safely.
- **Slice 4 next:** Add browser E2E coverage proving admin-created Writing papers
  appear in the learner Writing library/player and model answer remains gated.

## Acceptance Criteria

- Admin can create, edit, upload, author, publish, and archive Writing papers.
- Published Writing papers appear through the existing learner Writing task APIs.
- Draft, in-review, and archived Writing papers do not appear to learners.
- Publish fails when letter type, required assets, source provenance, task prompt,
  case notes text, or model answer text are missing.
- Learner player shows case notes and task metadata but not model answers.
- Post-submit model-answer study flow can show the authored model answer payload.

## Validation Plan

- Focused backend tests: `ContentPaperServiceTests` Writing publish gate,
  projection, and archive behavior.
- Focused frontend tests: admin Writing page query/filter/readiness rendering.
- Broader checks before release: `npx tsc --noEmit`, `npm run lint`, focused
  Vitest tests for Writing/admin pages, focused backend tests, then E2E admin and
  learner browser smoke tests.