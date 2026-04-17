# OET Scoring — Canonical Specification

> **Mission-critical.** This document is the single source of truth for all OET
> scoring in the platform. Every calculation, UI display, API response, report,
> certificate, refund trigger, analytics event, and AI feedback string **must**
> conform exactly. Any deviation is a critical system failure.

---

## 1. Rules (verbatim)

### Listening

- Passing score: **350 / 500 (Grade B)**
- Raw equivalent: **30 / 42 correct ≡ 350 / 500 EXACTLY**
- `< 30 / 42` → fail
- `≥ 30 / 42` → pass (Grade B or better)

### Reading

- Passing score: **350 / 500 (Grade B)**
- Raw equivalent: **30 / 42 correct ≡ 350 / 500 EXACTLY**
- `< 30 / 42` → fail
- `≥ 30 / 42` → pass (Grade B or better)

### Writing (country-dependent)

| Target country                                                       | Pass mark             |
| -------------------------------------------------------------------- | --------------------- |
| **United Kingdom, Ireland, Australia, New Zealand, Canada**          | **350 / 500 (Grade B)** |
| **United States of America, Qatar**                                  | **300 / 500 (Grade C+)** |

- Country is **mandatory**. Without it, Writing pass/fail **must not** be
  determined — surface a `country_required` / `country_unsupported` state.

### Speaking

- Passing score: **350 / 500 (Grade B)** — universal, never varies by country.

---

## 2. Grade bands (0–500 scale)

| Grade | Scaled range | Label          |
| ----- | ------------ | -------------- |
| A     | 450 – 500    | Very High      |
| B     | 350 – 449    | High (Pass)    |
| C+    | 300 – 349    | Satisfactory   |
| C     | 200 – 299    | Adequate       |
| D     | 100 – 199    | Limited        |
| E     |   0 –  99    | Minimal        |

---

## 3. Canonical helpers (use these — never re-implement)

### Frontend / shared TypeScript — `lib/scoring.ts`

```ts
import {
  // raw ↔ scaled
  oetRawToScaled,                      // 30 → 350 exactly
  // pass checks
  isListeningReadingPassByRaw,         // raw ≥ 30
  isListeningReadingPassByScaled,      // scaled ≥ 350
  isSpeakingPass,                      // scaled ≥ 350
  // full grading results
  gradeListeningReading,               // ('listening'|'reading', raw) → result
  gradeWriting,                        // (scaled, country) → result | country-required
  gradeSpeaking,                       // (scaled) → result
  // country resolution
  normalizeWritingCountry,             // "USA" → "US", "UK" → "GB" …
  getWritingPassThreshold,             // → { threshold, grade, country } | null
  // grade utilities
  oetGradeFromScaled,                  // scaled → 'A'|'B'|'C+'|'C'|'D'|'E'
  oetGradeLabel,                       // 'B' → 'Grade B'
  // display
  formatRawLrScore,                    // "35/42"
  formatScaledScore,                   // "380/500"
  formatListeningReadingDisplay,       // "35/42 • 380/500 • Grade B"
} from '@/lib/scoring';
```

### Backend / .NET — `OetLearner.Api.Services.OetScoring`

```csharp
using OetLearner.Api.Services;

OetScoring.OetRawToScaled(30);                          // 350
OetScoring.IsListeningReadingPassByRaw(30);             // true
OetScoring.IsListeningReadingPassByScaled(350);         // true

OetScoring.GradeListeningReading("listening", 30);      // ListeningReadingResult
OetScoring.GradeWriting(320, "UK");                     // Passed = false
OetScoring.GradeWriting(320, "USA");                    // Passed = true
OetScoring.GradeWriting(400, null);                     // Passed = null, Reason = "country_required"
OetScoring.GradeSpeaking(350);                          // Passed = true

OetScoring.NormalizeWritingCountry("United Kingdom");   // "GB"
OetScoring.GetWritingPassThreshold("Qatar");            // 300 / "C+" / "QA"
OetScoring.OetGradeLetterFromScaled(349);               // "C+"
OetScoring.FormatListeningReadingDisplay(30);           // "30/42 • 350/500 • Grade B"
```

Both implementations are **behaviourally identical**. The TypeScript module is
tested by `lib/scoring.test.ts` (72 assertions). The .NET module is tested by
`backend/tests/OetLearner.Api.Tests/OetScoringTests.cs` (98 assertions).

---

## 4. Invariants (never violate)

| Invariant                                                    | Enforced by                                |
| ------------------------------------------------------------ | ------------------------------------------ |
| `oetRawToScaled(30) === 350`                                 | self-check in `lib/scoring.ts` on import   |
| `oetRawToScaled(0)  === 0`                                   | self-check + tests                         |
| `oetRawToScaled(42) === 500`                                 | self-check + tests                         |
| raw→scaled is monotonically non-decreasing over `[0..42]`    | tests                                      |
| Writing pass REQUIRES a resolved country                     | `gradeWriting` returns discriminated union |
| Grade-B vs Grade-C+ country sets never overlap               | tests                                      |
| Listening/Reading/Speaking use 350 exactly, no country logic | tests                                      |

---

## 5. Country normalization

Both `normalizeWritingCountry` (TS) and `OetScoring.NormalizeWritingCountry`
(.NET) accept ISO alpha-2 codes **and** common English names. Examples:

```
GB · UK · United Kingdom · Great Britain · England · Scotland · Wales  → GB
IE · Ireland · Republic of Ireland                                     → IE
AU · Australia                                                         → AU
NZ · New Zealand                                                       → NZ
CA · Canada                                                            → CA
US · USA · United States · United States of America · America          → US
QA · Qatar                                                             → QA
```

Any other input (e.g. `Germany`, `India`, `DE`, empty, null) → `null`. The
Writing graders then return an explicit `country_required` or
`country_unsupported` state — callers MUST surface this, not default silently.

---

## 6. UI / output requirements

For Listening and Reading, **every** user-facing display must show raw,
scaled, and grade together so the `30/42 ≡ 350/500` anchor is verifiable at a
glance:

```
Listening   35 / 42  •  380 / 500  •  Grade B
Reading     29 / 42  •  338 / 500  •  Grade C+
```

Use `formatListeningReadingDisplay(raw)` (TS) or
`OetScoring.FormatListeningReadingDisplay(raw)` (.NET).

For Writing, any UI that renders a pass/fail label **must** also render the
target country the determination was made against:

```
Writing     340 / 500  •  Grade C+  •  PASS (USA)
Writing     340 / 500  •  Grade C+  •  FAIL (UK — needs 350)
Writing     400 / 500  •  Grade B   •  ⚠ Country required
```

---

## 7. Call-site policy

- **Do not** compare `score >= 350` or `score >= 300` directly in UI,
  business-logic, or SQL. Route through the canonical helpers.
- **Do not** compute raw→scaled with ad-hoc arithmetic. Use `oetRawToScaled`.
- **Do not** treat Writing pass/fail as universal. Always resolve country.
- **Do not** apply the Writing country rule to Listening, Reading, or Speaking.
- **Do not** cache grade labels as plain strings across the boundary — derive
  them from the scaled score with `oetGradeFromScaled`.

---

## 8. Reference material

The authoritative content for this specification lives in
`Project Real Content/Scoring System.txt`. External calculators used for
cross-checking the 30/42 ≡ 350/500 mapping:

- <https://edubenchmark.com/blog/oet-score-calculator-guide/>
- <https://www.geniusclass.co.uk/oet-calculator>

---

## 9. If these rules change

1. Update `Project Real Content/Scoring System.txt` first.
2. Update this file (`docs/SCORING.md`) to match.
3. Update `lib/scoring.ts` and `backend/src/OetLearner.Api/Services/OetScoring.cs` together.
4. Update both test files and make sure all tests pass.
5. Note the change in `AGENTS.md` under the "Common Gotchas" section.

No other location should need to change — every consumer routes through the
canonical helpers.
