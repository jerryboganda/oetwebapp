# Listening grader â€” Part A matching + MissReason

The Listening grader lives at
[`Services/Listening/ListeningGradingService.cs`](../../backend/src/OetLearner.Api/Services/Listening/ListeningGradingService.cs).
This doc covers the Part A short-answer pipeline: candidate building,
normalisation, matching, and the structured `MissReason` classification
that the review page surfaces.

## Pipeline

```
UserAnswerJson â†’ TryReadString â†’ normalise â†’ match? â†’ MissReason
                                            â†˜ yes â†’ (correct, Match)
                                            â†˜ no  â†’ ClassifyMiss â†’ reason
```

1. **Build candidates** â€” canonical answer (`CorrectAnswerJson`) plus
   accepted variants (`AcceptedSynonymsJson`).
2. **Normalise** â€” strategy is read from
   `ListeningPolicy.ShortAnswerNormalisation` (singleton). Supported:
   - `exact` â€” no normalisation
   - `trim_only` â€” leading/trailing whitespace removed
   - `trim_collapse_case_insensitive` (default) â€” trim + collapse internal whitespace + case-insensitive compare
   - legacy or unknown profiles — invalid and fail closed to exact matching
3. **Match** — `StringsMatch(user, candidate, caseSensitive, normalisation)`. Levenshtein is used only by `ClassifyMiss` for analytics labelling; it never awards a mark.
4. **Classify miss** — when no candidate matches, `ClassifyMiss` walks a
   prioritised set of heuristics (see below) to emit a structured
   `ListeningMissReason` value persisted on `ListeningAnswer.MissReason`.

## MissReason heuristics

The classifier short-circuits in this order:

| Order | Reason | Trigger |
|---|---|---|
| 1 | `Empty` | User answer is blank or whitespace-only. |
| 2 | `WrongNumber` | Any candidate carries digits and the user's digit string doesn't match any candidate's digit string. (Checked **before** spelling so digit swaps don't fall into `SpellingError` via a small edit distance.) |
| 3 | `SpellingError` | Levenshtein distance â‰¤ 2 between the user answer and any candidate. The pass threshold (Levenshtein â‰¤ 1) is tighter; this is intentionally wider so authors don't have to enumerate every plausible typo. |
| 4 | `ExtraInfo` | User answer contains every token of some candidate plus â‰¥ 2 extra tokens. |
| 5 | `WrongSection` | User answer matches a canonical or variant of a **different** question on the same paper (built once per grade pass via `BuildPaperAnswerMap`). |
| 6 | `Paraphrase` | None of the structural heuristics fired. |

`Other` is reserved for future expansion. The grader returns `Match` when an
answer passes â€” the review page uses this to render the green chip.

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
[`backend/tests/OetLearner.Api.Tests/Listening/ListeningGraderMissReasonTests.cs`](../../backend/tests/OetLearner.Api.Tests/Listening/ListeningGraderMissReasonTests.cs).
Run via `dotnet test backend/OetLearner.sln --filter ListeningGrader`.

## Cross-references

- The legacy JSON-pathway view model also computes a free-form `errorType`
  via `ObjectiveErrorType` in `ListeningLearnerService.cs`. The review page
  prefers the relational `missReason` and falls back to `errorType` for
  legacy attempts â€” both feed the same `missReasonChip()` helper at
  [`app/listening/review/[id]/page.tsx`](../../app/listening/review/[id]/page.tsx).
- Frontend annotation persistence (highlights + strikethroughs) is documented
  inline at [`hooks/use-listening-annotations.ts`](../../hooks/use-listening-annotations.ts).
