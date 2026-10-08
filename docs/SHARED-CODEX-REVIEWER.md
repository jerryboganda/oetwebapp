# Shared Codex Reviewer Pipeline (Writing + Speaking)

Owner directive (8 Oct 2026): **neither Writing nor Speaking may ever stay pending because GPT-6.1 Sol /
Codex quota is exhausted.** Codex is the reviewer of first choice for both assessment types; when it
cannot take or complete the review inside bounded budgets, the same review prompt runs automatically on
the configured API reviewer route.

## Shape

```
Writing grade (Claude Max → Claude API → Codex)      Speaking grade (Claude Max → API)
        │                                                        │
        ▼                                                        ▼
  secondary review required (writing.grade.review)      secondary review (speaking.grade.review)
        └──────────────┬─────────────────────────────────────────┘
                       ▼
              CodexReviewerGate   (one FIFO queue, one capacity limit, bounded wait)
                       ▼
              SharedReviewerRunner (Codex attempts → bounded → classify → API fallback)
                       ▼
       writing.grade.review on `anthropic`      speaking.grade.review on `anthropic`
                       │                                                        │
                       ▼                                                        ▼
   Applied by WritingReviewApplier (bounded)          Merged by SpeakingGradeReviewer (±1 band)
                       │                                                        │
                       ▼                                                        ▼
              Final Writing result (15-min release window)      Final Speaking assessment
```

Code:
- `backend/src/OetLearner.Api/Services/Ai/Review/CodexReviewerGate.cs` — the ONE capacity limit, FIFO across both types.
- `backend/src/OetLearner.Api/Services/Ai/Review/SharedReviewerRunner.cs` — gate wait → Codex phase → API fallback.
- `backend/src/OetLearner.Api/Services/Ai/Review/SharedReviewerOptions.cs` — the one policy object (section `Reviewer:Shared`).
- `backend/src/OetLearner.Api/Services/Ai/Review/ReviewerQueueMetrics.cs` — counters/gauges (admin: `GET /v1/admin/ai/reviewer-queue`).
- Callers: `Services/Writing/Review/WritingGradeReviewer.cs` (Enforce/Shadow modes unchanged) and
  `Services/Speaking/SpeakingGradeReviewer.cs`.

## Behaviour

| Situation | Behaviour |
| --- | --- |
| Codex healthy, slot free | Review runs on Codex. The API route is not touched. |
| Codex lane saturated | The job waits in the shared FIFO queue for at most `MaxQueueWaitSeconds`, then goes to the API reviewer. |
| Codex quota exhausted / auth failure / invalid request | Immediate API fallback (no Codex retry: retrying cannot help inside the pass). |
| Codex timeout, network, overloaded, rate limit, 5xx | One bounded retry inside the Codex phase, then API fallback. |
| Codex phase budget exhausted (no attempt or all attempts used) | API fallback with reason `codex_budget_exhausted`. |
| Both routes fail | Writing: the reviewer throws, the letter is HELD (`writing_review_unavailable`) and re-queued — bounded by `Writing:GradeChain:ReviewMaxHolds` / `ReviewGiveUpMinutes`, after which the letter completes on its primary result flagged for a tutor (`rv_unresolved`). Speaking: Claude's grade stands untouched and the trace records `status=failed`. |
| Local feature/quota/budget/grounding refusal | Never failed over (a second provider cannot fix a control-plane refusal). |

Bounded worst case per review: `MaxQueueWaitSeconds + CodexBudgetSeconds + ApiAttemptSeconds`
(default 45 + 480 + 240 = 765 s), which stays inside the Speaking inline assessment ceiling
(20 min) and the Writing letter claim lease (25 min − margin).

## Fairness

One FIFO queue for both types: a slot is handed to the longest-waiting job regardless of assessment
type, so Writing cannot starve Speaking or vice versa. There is deliberately no per-type reservation —
that would idle capacity — and no separate queue per type.

## Recovery / idempotency

- The gate holds no durable state: a restart clears it, and every in-flight review is already covered by
  the existing reclaim paths (Writing claim lease + `WritingBatchGradingCron`; the Speaking
  `AiOperation` lease + `AiOperationLeaseClaimer`).
- Every review call is an `AiOperation` with its own slot (`WritingGradeChain.ReviewResourceVersion`
  for Codex, `ReviewApiResourceVersion` for the API fallback), so a duplicate delivery, retry or a late
  Codex reply can never produce a second finalisation or a second charge.
- Writing resumes a persisted review pass without a second provider call (`WritingReviewStageRecord`).

## Configuration (`Reviewer:Shared`, all optional)

| Key | Env | Default | Meaning |
| --- | --- | --- | --- |
| `Reviewer:Shared:MaxConcurrency` | `Reviewer__Shared__MaxConcurrency` or `CODEX_REVIEWER_MAX_CONCURRENCY` | 2 | Simultaneous Codex reviews (per process; both types share it). |
| `Reviewer:Shared:MaxQueueWaitSeconds` | `Reviewer__Shared__MaxQueueWaitSeconds` | 45 | Longest FIFO wait before the API fallback takes the job. |
| `Reviewer:Shared:CodexAttempts` | … | 2 | Codex attempts per review pass. |
| `Reviewer:Shared:CodexAttemptSeconds` | … | 300 | Per-attempt budget. |
| `Reviewer:Shared:CodexBudgetSeconds` | … | 480 | Whole Codex phase budget. |
| `Reviewer:Shared:CodexRetryDelaySeconds` | … | 10 | Pause between transient Codex retries. |
| `Reviewer:Shared:ApiAttempts` | … | 1 | Fallback attempts. |
| `Reviewer:Shared:ApiAttemptSeconds` | … | 240 | Per-fallback-attempt budget. |
| `Reviewer:Shared:ApiFallbackProvider` | … | `anthropic` | Fallback provider row (the same Anthropic API row the Writing grade chain uses as L2). |
| `Reviewer:Shared:ApiFallbackModel` | … | `claude-opus-5-5` | Fallback model. |

Missing configuration is never fatal: the in-class defaults above apply, and if the Codex provider row is
missing/inactive the Writing reviewer reports `Off` (grading runs unreviewed, never held) exactly as before.

## Observability

- Structured logs: `reviewer.gate.saturated`, `reviewer.codex.attempt_failed`, `reviewer.codex.fallback`,
  `reviewer.codex.success`, `reviewer.api_fallback.attempt_failed`, `reviewer.api_fallback.success`, each
  carrying assessment type, assessment id, queue wait, attempt number, failure class, provider and duration.
- Admin: `GET /v1/admin/ai/reviewer-queue` (per type: waiting, in-flight, gate timeouts, Codex
  success/quota/timeout/unavailable counts, API fallback count, average queue wait, average duration,
  last fallback reason).
- Result provenance: Writing admin notes carry `provider`, `model` and the `codex_api_fallback` flag; the
  Speaking review trace carries `provider` and `fallbackReason` (persisted under the reserved `_review`
  key of the rationales payload, never shown to a candidate).

## Owner rules that still bind

- **Max is never turned off.** This pipeline only chooses the REVIEWER route. The Writing grade chain
  still starts every run on Claude Max and only fails over after Max actually fails; the Speaking chain
  still pins Claude Max first.
- The reviewer is not a paid route in normal operation (Codex subscription). The API fallback exists so an
  assessment completes when Codex capacity is gone; it reuses the existing `anthropic` provider row and
  costs the same as the existing L2 grading hop.

## Source grounding (owner directive 9 Oct 2026, Physiotherapy calibration)

No finding may tell a candidate that a fact is invented or "not in the case notes" when that fact is in the
full extracted source (`CaseNotesSnapshot` plus `TaskSnapshot`), in any written form (date formats, DOB / D.O.B.,
degree sign / degrees, R / right, label and value on separate lines).

- One pure verifier, `WritingSourcePresence.IsFalseAbsenceClaim`, enforces it. It is FAIL-SAFE: any doubt keeps the
  finding. It proves only three classes: a date (a DOB first of all), a range of movement in degrees, and a vital
  sign / body measurement (mmHg, bpm, degrees C, kg, cm, mm). A dose, frequency, duration, diagnosis or name is
  NEVER suppressed by code (it goes to the reviewer with `ValueLookup` evidence). The proof is strict: a plain
  "this value is absent" message (no interpretation, no omission wording); a quote that is letter wording whose
  every word the notes explain, with no unexplained number; the proof holds for EVERY occurrence of the quote; the
  value is in the notes with the SAME label (side, joint, DOB, measurement) and the SAME event (admission,
  discharge, active, passive ...); and the message names nothing beyond that label. A value recorded for another
  side, joint or event is a real error and is kept. Known limits (all keep the finding): unit conversion, two-digit
  years in written dates, a letter that omits a side the notes state, a side-only or event-qualified source label,
  abbreviations outside the vital-sign map.
- `WritingSourcePresence.ValueLookup` is the non-suppressing sibling: when the strict proof is not met it shows the
  reviewer the source lines that carry the same value, so the model judges attribution with evidence.
- `WritingReviewApplier.Apply` removes such grader/reviewer findings before the verdict loop (and refuses to add
  one), relieving the criterion so the reviewer may raise it. The pipeline runs the same check for grades the
  reviewer did not decide (review off, shadow, skipped, outage fallback). Deterministic rule findings are never touched.
- The reviewer prompt (`writing-review.v2`) and the shared grader prompt (`CandidateGradingRules`) both instruct
  "search the whole notes in every form before alleging absence".
- The candidate rule-engine lane is deliberately NOT given `CaseNotesText`: that would switch on score-bearing
  source detectors whose date/age handling has known gaps. Do not add it without replacing those regexes with the verifier.
- If the stored case-note rows genuinely lack the fact (an extraction or authoring loss, as happened with the Weir
  DOB), the claim is TRUE for that snapshot and the verifier correctly keeps it: repair the rows in the admin
  case-notes editor, then re-submit.