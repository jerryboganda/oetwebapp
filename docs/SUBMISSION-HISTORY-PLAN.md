# Submission History — Spec & Enhancement Plan

**Status:** Implemented (all 5 phases shipped)
**Location (learner UI):** `/submissions`, `/submissions/[id]`, `/submissions/compare`
**Location (backend):**
- `backend/src/OetLearner.Api/Services/Submissions/SubmissionHistoryService.cs`
- `backend/src/OetLearner.Api/Contracts/Submissions/SubmissionContracts.cs`
- `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs` (routes)
- `backend/src/OetLearner.Api/Data/Migrations/20260421000000_AddHiddenByUserAtToAttempt.cs`

---

## 1. Purpose

Submission History is the learner's single source of truth for past
attempts and the evidence workflow (reopen, compare, request expert
review, revise). It is NOT an activity log — every row surfaces an
OET-relevant piece of evidence with a canonical scaled score and an
explicit pass/fail determination.

## 2. Non-negotiable invariants

1. **Scaled-score display only.** Every card renders scores on the
   0–500 OET scale via `lib/scoring.ts::summarizeCardScore` /
   `OetScoring.ResolvePassState`. Rendering a percentage anywhere in
   this subsystem is a regression. Enforced by unit test
   `app/submissions/page.test.tsx` (_"never renders a percentage sign"_)
   and by `SubmissionHistoryTests.ExportCsv_EmitsCsvHeaderAndNoPercentage`.
2. **Country-aware Writing pass.** Writing pass state resolves through
   `OetScoring.GradeWriting(scaled, country)` with country read from
   `Goal.TargetCountry`. Missing country → `country_required` pass
   state (never silently assumes Grade B).
3. **Evidence-only listing.** `SubmissionHistoryService` only ever
   returns attempts in `{ Submitted, Evaluating, Completed, Failed }`.
   `Abandoned / Paused / NotStarted` attempts never appear in the UI.
   Enforced by `SubmissionHistoryTests.List_NeverIncludesAbandonedOrPausedStates`.
4. **No fabricated comparison summaries.** Comparison output is a
   deterministic diff over scaled scores + criterion scores. The
   legacy hardcoded sentence
   `"The more recent submission shows stronger structure and slightly
   improved score confidence."` is explicitly banned by
   `SubmissionHistoryTests.Compare_NeverReturnsHardcodedPlaceholder`.
5. **Single round-trip detail.** The detail endpoint composes everything
   server-side. The frontend issues exactly one `GET /v1/submissions/{id}`.
   No client-side N+1 stitching across writing/speaking/reading/listening
   result endpoints.
6. **Soft-hide never affects analytics.** `HiddenByUserAt` is a learner
   preference column on `Attempt`. The Submission History list excludes
   hidden rows by default; Progress, Readiness, Cohort analytics, and
   Study Plan keep counting them.

## 3. Endpoints

| Method | Path | Purpose |
| --- | --- | --- |
| GET  | `/v1/submissions` | Keyset-paginated list with facets + sparkline |
| GET  | `/v1/submissions/{id}` | Single-call detail (evidence + lineage) |
| GET  | `/v1/submissions/compare` | Deterministic compare (scaled + criterion deltas) |
| GET  | `/v1/submissions/export.csv` | CSV export respecting current filters |
| POST | `/v1/submissions/{id}/hide` | Soft-hide from history |
| POST | `/v1/submissions/{id}/unhide` | Restore to history |
| POST | `/v1/reviews/requests/batch` | Bulk review request (max 5) |

### List query parameters
`cursor`, `limit` (default 20, max 50), `subtest` (writing|speaking|reading|listening),
`context` (practice|mock|diagnostic|revision), `reviewStatus`
(not_requested|pending|reviewed), `from`, `to` (ISO 8601), `passOnly`,
`q` (task title search), `sort` (date-desc|date-asc|score-desc|score-asc),
`includeHidden`.

### List response envelope
```jsonc
{
  "items": [
    {
      "submissionId": "...",
      "contentId": "...",
      "taskName": "...",
      "subtest": "writing",
      "context": "practice",
      "attemptDate": "2026-04-12T10:00:00Z",
      "state": "completed",
      "reviewStatus": "not_requested",
      "evaluationId": "...",
      "scaledScore": 380,
      "scoreLabel": "380 / 500",
      "passState": "pass",
      "passLabel": "Pass (Grade B)",
      "requiredScaled": 350,
      "grade": "B",
      "comparisonGroupId": null,
      "parentAttemptId": null,
      "revisionDepth": 0,
      "canRequestReview": true,
      "isHidden": false,
      "actions": {
        "reopenFeedbackRoute": "/submissions/...",
        "compareRoute": "/submissions/compare?leftId=...",
        "requestReviewRoute": "/submissions/...?requestReview=1"
      }
    }
  ],
  "nextCursor": "base64:...",
  "total": 137,
  "facets": {
    "bySubtest":     { "writing": 40, "speaking": 30, "reading": 35, "listening": 32 },
    "byContext":     { "practice": 80, "mock": 30, "diagnostic": 4, "revision": 23 },
    "byReviewStatus":{ "not_requested": 100, "pending": 20, "reviewed": 17 }
  },
  "sparkline": {
    "writing":   [ { "at": "...", "scaled": 320 }, ... ],
    "speaking":  [ ... ],
    "reading":   [ ... ],
    "listening": [ ... ]
  }
}
```

### Cursor contract
Opaque base64 of `submittedAtUtc|attemptId|sort`. Not a permalink.

## 4. Frontend architecture

- **Page state is URL-synced.** Every filter, sort and hidden toggle
  lives in the query string so pages are shareable and survive
  back/forward navigation.
- **Canonical scoring helper:** `lib/scoring.ts::summarizeCardScore`.
  Used by `ScoreWithPassBadge`. No UI surface ever formats a score by
  hand.
- **Components (all under `components/domain/submissions/`)**:
  - `SubmissionFilterBar` — header search/tabs/chips/date-range/sort.
  - `SparklineStrip` — 4-tile per-subtest trend with pass line at 350.
  - `SubmissionCard` — row with compare-pick mode + overflow menu for hide/unhide.
  - `ScoreWithPassBadge` — scaled score + grade + pass/fail badge.
  - `RevisionLineageChip` — horizontal chain widget across parent/children.
  - `ReviewLineageCard` — expert review status, turnaround, credits.
  - `CompareSelector` — dual-slot picker on `/submissions/compare`.

## 5. Analytics events

Emitted via `lib/analytics.ts`:

- `submissions_filter_applied`
- `submissions_sort_changed`
- `submissions_compare_started`
- `submissions_compare_selected`
- `submissions_review_requested_from_history`
- `submissions_hidden`, `submissions_unhidden`
- `submissions_exported`
- `submissions_bulk_review_requested`
- `submissions_sparkline_tile_clicked`

## 6. Tests

**Frontend (Vitest):** `app/submissions/page.test.tsx` — dashboard shell,
ISO date formatting, no-percentage guard, compare-mode pick flow.

**Backend (xUnit):** `backend/tests/OetLearner.Api.Tests/SubmissionHistoryTests.cs`
- envelope shape (items + total + facets + sparkline + nextCursor)
- every item has `scaledScore` + `passState` + no `%` in score label
- never includes Abandoned/Paused/NotStarted
- subtest filter narrows results
- detail composes in one round-trip
- detail 404 for unknown id
- compare never returns the banned hardcoded placeholder
- compare exposes `canCompare`, `scaledDelta`, `criterionDeltas`
- hide/unhide round-trip including `includeHidden` flag semantics
- CSV export header + no percentage
- bulk review empty batch → 400

## 7. Migrations

- `20260421000000_AddHiddenByUserAtToAttempt` — adds nullable
  `HiddenByUserAt` timestamptz column and index
  `IX_Attempts_UserId_HiddenByUserAt_SubmittedAt`. Safe rollback: drop
  column.

## 8. Operational notes

- Sparkline data is bounded to the latest 12 scaled scores per sub-test.
- Keyset pagination is ordered stably on `(SubmittedAtUtc, AttemptId)`.
- Bulk review request is hard-capped at 5 attempts per call, with
  de-duplication by `attemptId`.
- CSV export walks pages server-side up to 200 pages × 50 rows
  (10,000-row ceiling) to avoid memory blow-up.
