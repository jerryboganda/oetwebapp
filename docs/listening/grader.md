# Listening grader — Part A matching + MissReason

The Listening grader lives at
[`Services/Listening/ListeningGradingService.cs`](../../backend/src/OetWithDrHesham.Api/Services/Listening/ListeningGradingService.cs).
This doc covers the Part A short-answer pipeline: candidate building,
normalisation, matching, and the structured `MissReason` classification
that the review page surfaces.

## Pipeline

```
UserAnswerJson → TryReadString → normalise → match? → MissReason
                                            ↘ yes → (correct, Match)
                                            ↘ no  → ClassifyMiss → reason
```

1. **Build candidates** — canonical answer (`CorrectAnswerJson`) plus
   accepted variants (`AcceptedSynonymsJson`).
2. **Normalise** — strategy is read from
   `ListeningPolicy.ShortAnswerNormalisation` (singleton). Supported:
   - `exact` — no normalisation
   - `trim_only` — leading/trailing whitespace removed
   - `trim_collapse_case_insensitive` (default) — trim + collapse internal whitespace + case-insensitive compare
   - `fuzzy_levenshtein_1` — same as default plus accept any single-edit typo
3. **Match** — `StringsMatch(user, candidate, caseSensitive, normalisation)`.
   For `fuzzy_levenshtein_1` we compare normalised strings using the
   Levenshtein-≤-1 short-circuit (no full DP table needed).
4. **Classify miss** — when no candidate matches, `ClassifyMiss` walks a
   prioritised set of heuristics (see below) to emit a structured
   `ListeningMissReason` value persisted on `ListeningAnswer.MissReason`.

## MissReason heuristics

The classifier short-circuits in this order:

| Order | Reason | Trigger |
|---|---|---|
| 1 | `Empty` | User answer is blank or whitespace-only. |
| 2 | `WrongNumber` | Any candidate carries digits and the user's digit string doesn't match any candidate's digit string. (Checked **before** spelling so digit swaps don't fall into `SpellingError` via a small edit distance.) |
| 3 | `SpellingError` | Levenshtein distance ≤ 2 between the user answer and any candidate. The pass threshold (Levenshtein ≤ 1) is tighter; this is intentionally wider so authors don't have to enumerate every plausible typo. |
| 4 | `ExtraInfo` | User answer contains every token of some candidate plus ≥ 2 extra tokens. |
| 5 | `WrongSection` | User answer matches a canonical or variant of a **different** question on the same paper (built once per grade pass via `BuildPaperAnswerMap`). |
| 6 | `Paraphrase` | None of the structural heuristics fired. |

`Other` is reserved for future expansion. The grader returns `Match` when an
answer passes — the review page uses this to render the green chip.

## Why a separate column instead of recomputing on read

`ListeningAnswer.MissReason` is populated at grade time so the review page
doesn't have to re-run the heuristics on every render. Analytics queries
also benefit: per-paper miss-class breakdowns become a single `GROUP BY`
instead of a join + string parse. Migration:
`20260521210000_AddListeningAnswerMissReason.cs` (nullable int column; legacy
rows graded before this migration carry `NULL` and the review UI treats
that as "no reason recorded").

## Test fixtures

The canonical regression set lives at
[`backend/tests/OetWithDrHesham.Api.Tests/Listening/ListeningGraderMissReasonTests.cs`](../../backend/tests/OetWithDrHesham.Api.Tests/Listening/ListeningGraderMissReasonTests.cs).
Run via `dotnet test backend/OetWithDrHesham.sln --filter ListeningGrader`.

## Cross-references

- The legacy JSON-pathway view model also computes a free-form `errorType`
  via `ObjectiveErrorType` in `ListeningLearnerService.cs`. The review page
  prefers the relational `missReason` and falls back to `errorType` for
  legacy attempts — both feed the same `missReasonChip()` helper at
  [`app/listening/review/[id]/page.tsx`](../../app/listening/review/[id]/page.tsx).
- Frontend annotation persistence (highlights + strikethroughs) is documented
  inline at [`hooks/use-listening-annotations.ts`](../../hooks/use-listening-annotations.ts).
