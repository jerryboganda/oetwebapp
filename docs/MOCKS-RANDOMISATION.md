# Mocks — Randomisation Helper

> Date: 2026-05-12 (Track B closure addendum)
> Source: `docs/mocks/PROGRESS.md` V2 Wave 8 — Medium #4

## Purpose

`RandomisationHelper.SeededShuffle<T>` provides a **deterministic, learner-keyed** Fisher-Yates shuffle that mock authoring + learner delivery surfaces can use to vary item order without losing reproducibility for grading and analytics.

The helper lives in `backend/src/OetLearner.Api/Services/RandomisationHelper.cs` and ships with 9 unit tests. **It is intentionally not yet wired into the live Reading / Listening DTO projections** — see "Why deferred" below.

## API

```csharp
public static IReadOnlyList<T> SeededShuffle<T>(
    IEnumerable<T> items,
    string seed,
    string saltKey);
```

| Parameter | Meaning |
| --------- | ------- |
| `items` | Source list to shuffle. Never null. Empty list returns `[]` unchanged. |
| `seed` | The deterministic seed string. Typical value: `attemptId` for learner-side shuffling, or `bundleId` for content-side preview. |
| `saltKey` | Per-call salt: distinguishes the shuffle within the same seed. Example: `"reading.partA.text-a.options"`, `"listening.partB.q3.choices"`. |

The helper hashes `seed + ":" + saltKey` with FNV-1a (64-bit), uses the result as the Fisher-Yates RNG seed, and returns a new list. **Same `(seed, saltKey)` always produces the same permutation.**

## Threat model

| Goal | How the helper meets it |
| ---- | ----------------------- |
| Two learners see different question / option order. | Each learner has a unique `attemptId` so their shuffle differs. |
| The same learner re-opening the attempt sees the same order. | `attemptId` is stable; salt is stable; FNV-1a is deterministic. |
| Grading does not depend on display order. | Backend grading uses option **ID**, not position. (Pre-requisite: option-ID migration — see [`MOCKS-OPTION-ID-MIGRATION.md`](./MOCKS-OPTION-ID-MIGRATION.md).) |
| Cohort analytics still group by item. | Analytics keys on `questionId`, not `displayOrder`. |
| Authoring preview is deterministic for content review. | Use `bundleId` (not `attemptId`) as the seed to inspect a stable preview shuffle. |

## When to use

**Safe today:**

- Listening / Reading **mock-mode-only** UI shuffling of multiple-choice **options** (not questions), because grading is keyed on the literal `correctAnswer` string per question and is not affected by option order.
- Speaking role-play card variant ordering (no scoring impact).

**Not safe yet (do NOT enable):**

- Reading question shuffling, because the DTO currently uses `letter`-keyed answer matching (`A` / `B` / `C`) on the wire. Shuffling options without a letter-to-ID lookup table would invalidate grading.
- Listening item ordering for graded sub-tests, for the same reason.

## Why deferred

The audit's V2 Medium #4 + #6 found that:

1. The on-the-wire DTO for Reading + Listening multiple-choice answers is positional / letter-keyed (e.g. `userAnswerJson` stores `"A"` for the first option).
2. Reading short-answer answers are also keyed by question position via `displayOrder`, not a stable ID.
3. Grading projections in `ReadingGradingService` and `ListeningGradingService` rely on this contract.

Therefore enabling shuffle on the live grading-side projection would break grading without a structural migration to **option IDs**. That migration is documented in [`MOCKS-OPTION-ID-MIGRATION.md`](./MOCKS-OPTION-ID-MIGRATION.md).

Until that migration ships, the helper exists for:

- **Learner-side display shuffling of options** (where the position-to-letter mapping is rebuilt on the client + server-rendered display only, and grading still uses the canonical answer string).
- **Speaking / Conversation card variant ordering** (no grading impact).
- **Authoring preview**.

## Validation

```powershell
dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~RandomisationHelper"
# → Passed: 9, Failed: 0, Total: 9
```

## Cross-links

- [`MOCKS-OPTION-ID-MIGRATION.md`](./MOCKS-OPTION-ID-MIGRATION.md) — structural migration plan (Wave 1.1).
- [`docs/mocks/PROGRESS.md`](./mocks/PROGRESS.md) — V2 Wave 8 entry.
- `backend/src/OetLearner.Api/Services/RandomisationHelper.cs`.
- `backend/tests/OetLearner.Api.Tests/RandomisationHelperTests.cs`.
