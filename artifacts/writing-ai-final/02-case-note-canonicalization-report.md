# 02 — Case-Note Canonicalization Report

## Storage locations (no live OCR in grading)

- Canonical case-note text: `WritingScenarioStructuredSentences`
  (`ScenarioId, Ordinal, SentenceText, RelevanceLabel relevant|maybe|irrelevant`).
- Exact Writing Task: `WritingScenario.TaskPromptMarkdown`.
- Source PDF reference: `WritingScenario.StimulusPdfMediaAssetId`
  (`MediaAssets.StoragePath` via `IFileStorage`; display-only).
- Grading reads ONLY the two canonical fields via
  `WritingAssessmentPreflightService` snapshots (`TaskSnapshot`,
  `CaseNotesSnapshot`). `AutoPdfTextExtractor`/OCR is not referenced anywhere
  in the grading path (verified by code trace).

## Preparation-time tooling (new)

- `PUT /v1/admin/writing/tasks/{id}/case-notes` — replace sentences (existing,
  unchanged).
- `POST /v1/admin/writing/tasks/{id}/case-notes/extract-from-pdf` (new) —
  runs `IPdfTextExtractor` (PdfPig → Azure → Mistral) over the stimulus PDF
  once, stores ≤500 line-sentences as `relevant`, returns counts/truncation.
  Throws `case_note_extraction_empty` when the PDF yields nothing (admin must
  transcribe instead of publishing a broken task).
- `GET /v1/admin/writing/tasks/preparation-status` (new) — paginated audit:
  task prompt presence, sentence count, rulebook resolvability, Model Answer
  status/staleness, publish-readiness + exact blocking codes. Backs the CSV
  runbook (`fetch-preparation-status.ps1`).

## Publish gate (blocking)

`WritingTaskAuthoringService.ValidateAsync/PublishAsync` now require:
`title_required`, `profession_required`, `letter_type_required`,
`letter_type_unsupported` (retired LT-RP can never publish),
`written_task_required`, `case_notes_required`, `rulebook_unresolvable`,
`model_answer_not_approved`, `word_guide_invalid`.
`PublishAsync` additionally heals legacy letter-type tokens to catalogue
codes (unclear → Other Letters) before validating. Bulk publish skips
unready tasks instead of failing the batch.

## Backfill status

- Total tasks audited in this environment: NOT MEASURABLE IN CURRENT
  ENVIRONMENT (no production database access from the dev host; catalogue
  lives in Postgres, not in the repo).
- Operational sequence (runbook in `fetch-preparation-status.ps1`):
  1. `GET preparation-status?status=published` → export CSV.
  2. For `case_notes_required`: run `extract-from-pdf`, admin-review sentences.
  3. For `written_task_required`: supply exact task text via task update.
  4. For `model_answer_not_approved`: run `generate-missing`, review, approve.
  5. Re-run status until every published row is `publishReady: true`.
- Tasks already valid / repaired / still blocked: to be filled from the live
  run of step 1 (columns defined in the CSV template below).

## CSV template (live data via endpoint)

`scenarioId,title,profession,letterType,status,hasTaskPrompt,caseNoteSentenceCount,rulebookResolvable,modelAnswerStatus,modelAnswerApproved,modelAnswerStale,publishReady,blockingCodes`
