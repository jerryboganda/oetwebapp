# 07 — RCA: `case_note_pages_unreadable`

## Candidate-facing symptom

Grading fails on tasks whose case-note PDF is perfectly readable on screen,
with missing-input code `case_note_pages_unreadable`.

## Actual root cause (traced, not speculated)

`WritingAssessmentPreflightService.ValidateAsync`
(`backend/src/OetLearner.Api/Services/Writing/WritingAssessmentPreflightService.cs:78-83`)
builds `caseNotesSnapshot` EXCLUSIVELY from
`WritingScenarioStructuredSentences`. The PDF-driven authoring path
(`WritingTaskAuthoringService`, used for the bulk of tasks) NEVER writes that
table — case notes live only as the stimulus PDF blob
(`WritingScenario.StimulusPdfMediaAssetId`). No runtime OCR exists in grading
(by design), so the snapshot is empty and preflight emits
`case_note_pages_unreadable` (PDF attached) or `case_note_pages` (no PDF).

Contributing cause: the old publish gate required only
title + profession + letter type, so PDF-only tasks published as "ready" and
candidates discovered the defect at grading time.

Secondary defect found while tracing: modern LT-* letter-type codes never
matched legacy-token assessment packs (`lt_rr` ≠ `routine_referral`), so
LT-* tasks were ALSO release-blocked after any case-note fix. Fixed with the
canonical `ToPackLetterType` bridge (preflight + governance + understanding).

## Fix (this change)

1. Publish gate now requires canonical sentences + exact task
   (`case_notes_required`, `written_task_required`) — new broken tasks cannot
   publish.
2. Prep-time canonicalization endpoint (`extract-from-pdf`) converts stimulus
   PDFs to structured sentences once, with admin review.
3. `ToPackLetterType` bridge fixes pack/rule resolution for LT-* tasks.
4. Preflight stays fail-closed for legacy unprepared tasks (same codes, now
   admin-actionable via `preparation-status`), with structured logging.
5. Operational backfill runbook (`02`, `fetch-preparation-status.ps1`).

## Proof

- `Missing_case_notes_still_fail_closed_with_unreadable_code` (gate preserved
  for unprepared tasks).
- `Catalogue_letter_type_code_matches_legacy_pack_token` (bridge).
- Publish-gate tests (`case_notes_required`, `written_task_required`,
  `model_answer_not_approved`).
- No OCR/PDF reference in the grading path (code trace, §2 artifact).
