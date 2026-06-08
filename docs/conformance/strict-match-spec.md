# StrictMatch — Answer-Matching Contract (decision 4)

**Status:** specification (Phase 0). Implemented per-service in Phase 2.
**Owner:** rulebook-governance. **Drives:** Reading + Listening grading.

## Why

Decision 4 (locked): **hard-lock strict exact-match spelling everywhere — all modules, all modes.** No fuzzy / Levenshtein / synonym auto-expansion may ever cause a misspelled answer to be accepted. This is a single contract that two separate C# services implement identically:

- `backend/.../Services/Reading/ReadingGradingService.cs`
- `backend/.../Services/Listening/ListeningGradingService.cs`

It intentionally overrides Listening **R02.5** (examiner minor-variant discretion) — logged as deviation **D1** in the master plan.

## The contract

`StrictMatch(candidate, answerKey, policy, caseSensitive = false) -> bool`

Normalization pipeline, applied identically to the candidate AND to each authored answer-key entry:

1. **ApplyTextNormalization** — fold smart quotes → ASCII; apply the policy's existing hyphen/unit normalization hooks (e.g. `38.5°C`). No semantic change.
2. **Trim** leading/trailing whitespace.
3. **CollapseWhitespace** — every internal whitespace run → a single space.
4. **Compare** with `Ordinal` (when `caseSensitive`) or `OrdinalIgnoreCase` (default). OET accepts any letter case, so case-insensitive is the default and is NOT a spelling relaxation.

**Accept iff** the normalized candidate equals the normalized form of the correct answer **or any authored alternate** in the answer key.

### Hard prohibitions

- **No** Levenshtein / edit-distance acceptance.
- **No** automatic synonym expansion, stemming, or lemmatization.
- **No** policy flag (e.g. `fuzzy_levenshtein_1`, `ShortAnswerAcceptSynonyms`) may relax matching in ANY mode. Such flags are deleted or coerced to strict.

### Authored alternates (the ONLY permitted multiplicity)

The answer key may legitimately list several distinct correct strings (e.g. `"GP"` and `"general practitioner"` when both are genuinely correct per the source text). These are explicit author intent, not fuzzy matching, and remain supported.

## Miss classification (analytics only — NEVER affects acceptance)

After a non-match, the grader MAY label *why* it missed, for analytics/feedback only. A labeled miss is **still scored 0**.

- `spelling` — Levenshtein distance ≤ small N between normalized candidate and correct answer. (Listening keeps this label for analytics; the label must NOT feed back into acceptance.)
- `number_form` — differs only by singular/plural inflection (Reading **R04.2**). Still wrong.
- `wrong` — otherwise.

The classifier is a pure, side-effect-free function of the (already-rejected) answer. It must be provably incapable of changing the score.

## Conformance hooks (Phase 2/3)

- xUnit golden: a single-letter typo scores **0** in BOTH graders, under EVERY policy including a stale `fuzzy_levenshtein_1`.
- The `fuzzy_levenshtein_1` code path and `LevenshteinDistanceAtMostOne` acceptance are deleted from grading; any residual Levenshtein use is `ClassifyMiss`-only and commented as analytics-only.
