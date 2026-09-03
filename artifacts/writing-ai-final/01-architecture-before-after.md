# 01 — Architecture Before / After

## Discovered stack

- Frontend: Next.js 16 (App Router), React 19, TypeScript, Tailwind v4.
  Writing Practice: `app/writing/practice/session/[scenarioId]/page.tsx`,
  paper mode `app/writing/paper/session/[id]/page.tsx`, grading page
  `app/writing/submissions/[id]/grading/page.tsx`, API helpers `lib/writing/api.ts`.
- Backend: ASP.NET Core Minimal API, EF Core, PostgreSQL.
  Endpoints under `backend/src/OetLearner.Api/Endpoints/`
  (`WritingSubmissionEndpoints`, `WritingScenarioEndpoints`,
  `WritingTaskAdminEndpoints`, `WritingTaskModelAnswerAdminEndpoints`,
  `WritingAssessmentGovernanceEndpoints`).
- Database: `WritingScenarios` + `WritingScenarioStructuredSentences`
  (canonical case notes) + `WritingSubmissions`/`WritingGrades` +
  `WritingAssessmentReportsV11`/`WritingAssessmentModelAnswers` (per-submission)
  + `WritingTaskModelAnswers` (one reusable pre-generated answer per task) +
  `WritingAssessmentPackVersions` (per profession/letter-type release packs).
- AI: `IAiGatewayService` (grounded prompts, `writing.score.v1` rubric,
  `writing.model-answer.v1` fallback, `writing.model-answer-pregen.v1` prep),
  `AiRetryPolicy` (429/5xx → max 3 retries, exp backoff + Retry-After + jitter),
  two-phase credit reservation (`AiCreditReservationService`, idempotent on
  business reference `writing-grade:{submissionId}`), one `AiUsageRecord` per
  physical provider call.
- OCR/PDF: `IPdfTextExtractor` = `AutoPdfTextExtractor`
  (PdfPig → Azure DocIntel → Mistral OCR). Used at preparation/import time and
  for paper-OCR *input*; never in the candidate grading path.

## End-to-end lifecycle (after)

Admin authoring (`POST /v1/admin/writing/tasks`, PDF attach)
→ preparation: case-note canonicalization
  (`PUT …/case-notes`, or `POST …/case-notes/extract-from-pdf`)
→ Model Answer pregen (`POST /v1/admin/writing/model-answers/generate-missing`,
  sequential, bounded) → admin approve
→ publish gate (`POST …/publish`: title, profession, valid LT code, exact
  task, ≥1 case-note sentence, resolvable rulebook, approved Model Answer)
→ candidate Practice → Submit (stable idempotency key)
→ `POST /v1/writing/submissions` (AiScoring 2/min, PerUser 5000/min)
→ persist submission → content-hash dedupe → claim → grade-reuse check →
  preflight (canonical snapshots) → ONE rubric provider call (or deterministic
  zero for blank, or zero-call grade reuse) → canon → report → reuse saved
  Model Answer for display → `graded`
→ grading page polls → results page shows assessment + saved exemplar.
Retry after transient failure: `POST …/retry-grade` on the SAME submission.

## Before → After (per normal candidate Submit)

| Aspect | Before | After |
|---|---|---|
| Provider calls (fresh grade) | 1 rubric + 0/1 canon-LLM + 0/1 live model-answer fallback | 1 rubric + 0/1 canon-LLM + **0 model-answer** (pregen reused) |
| Provider calls (blank letter) | rejected pre-submit (`writing_submission_empty`) | **0** (deterministic zero grade, no credit hold) |
| Provider calls (double-tap / retry) | up to 2 submissions → 2 rubric calls (different random keys) | **1** (content-hash guard + stable key collapse to one row) |
| Provider calls (identical re-grade in TTL) | 0 (reuse path existed) | 0 (unchanged) |
| Live OCR/PDF extraction during grading | 0 (already snapshot-based) | 0 (unchanged; extraction is now a first-class prep endpoint) |
| Model Answer generations per Submit | 0 when approved pregen exists, else 1 live fallback | 0 when approved pregen exists (publish gate requires it for new tasks), else 1 legacy fallback |
| Duplicate paid workflows on double-tap | possible (2 rows, 2 charges) | impossible (1 row; reservation idempotent per submission) |
| Empty/short submit | blocked (400 validation / empty rejection) | allowed; blank → deterministic zero, short → normal AI grade |
| Failed grading recovery | re-submit (new row, new charge) or revise | `retry-grade` on same row (reservation + provider-result resume, no double charge) |
| LT-* task pack/rule resolution | release-blocked (`lt_rr` ≠ `routine_referral`) + generic-only rules | canonical bridge `ToPackLetterType` (packs match, letter-type rules apply) |
| Overridden legacy rules in force | 26 OVERRIDDEN_OR_CORRECTED rules active as critical/major (JSON bodies + 6 live detectors) | bodies corrected verbatim to FINAL MASTER decisions (v1.0.1); 6 detectors neutralized |
