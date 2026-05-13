# Mocks — Reading / Listening Option-ID Migration Plan

> Date: 2026-05-12 (Track B closure addendum)
> Owner: Mocks Platform team
> Source: `docs/mocks/PROGRESS.md` V2 Wave 8 — Medium #6
> Status: **planned wave (v1.1)** — not blocking v1 launch

## Why this exists

The Mocks V2 reviewer pass surfaced that enabling per-attempt randomisation of Reading and Listening multiple-choice options requires a structural change to the on-the-wire DTO. Today the DTO is positional / letter-keyed:

- `ReadingQuestion.OptionsJson = ["First option", "Second option", ...]`
- `ReadingAnswer.UserAnswerJson = "A"` / `"B"` / `"C"` / `"D"` (or `0`-indexed integer in some legacy rows)
- `ReadingQuestion.CorrectAnswerJson = "B"`

Shuffling positions without changing the contract would invalidate grading. This document is the migration plan that unblocks `RandomisationHelper.SeededShuffle` for the Reading and Listening grading-side projections.

## Goals

1. Make every multiple-choice option **carry a stable ID** that survives shuffles.
2. Switch the wire contract for `userAnswer` and `correctAnswer` from `letter` / position to `optionId`.
3. Keep backwards compatibility for learners with in-flight attempts and for already-graded historical answers.
4. Enable per-attempt shuffle without breaking grading, analytics, or admin item analysis.

## Schema change (additive)

| Entity | Current | After migration |
| ------ | ------- | --------------- |
| `ReadingQuestion.OptionsJson` | `["A", "B", "C", "D"]` (just text) | `[{"id":"opt-...","text":"...","letter":"A"}]` |
| `ReadingAnswer.UserAnswerJson` | `"A"` (letter) | `"opt-..."` (id) — letter retained as a backwards-compat read path |
| `ReadingQuestion.CorrectAnswerJson` | `"B"` (letter) | `"opt-..."` (id) |
| `ListeningItem.OptionsJson` | same shape as Reading | same shape change |
| `ListeningAnswer.UserAnswerJson` | same | same |

The author-supplied **letter** (`A`/`B`/`C`/`D`) is preserved alongside the ID as a stable display anchor when no shuffle is active. The **ID** is the new grading key.

## Migration steps (sequential)

### Wave 1.1.0 — Option ID generation (DB-only, no wire change)

- New migration `20260612100000_AddReadingOptionIds` adds a generated `OptionsJson` projection that materialises `{ id, text, letter }` per option, using a deterministic hash of `(questionId, displayOrder)` as the option id. No reads change.
- Backfill: read each row, parse the existing `OptionsJson`, write back the same options with assigned IDs.
- Guard: schema defines a CHECK constraint `OptionsJson IS NOT NULL AND jsonb_typeof(OptionsJson) = 'array'`.
- Test gates: ReadingAuthoring + ListeningAuthoring publish gates accept both old and new shapes.

### Wave 1.1.1 — Dual-write the answer DTO

- Backend reads accept `userAnswer = "A"` (legacy) **or** `userAnswer = "opt-..."` (new) and resolve to the option ID server-side.
- Backend writes always emit the option ID.
- Frontend continues to send the letter; backend translates.

### Wave 1.1.2 — Frontend opts into option-ID writes

- Reading + Listening players read the new `OptionsJson[].id` field and submit `userAnswer = optionId` directly. Letter is shown to the learner as a display anchor (or hidden when shuffle is active).
- Mock-wizard authoring flow ships the new `optionsJson` shape on import.

### Wave 1.1.3 — Wire `RandomisationHelper.SeededShuffle` into the projection

- `ReadingLearnerService.GetAttemptStructureAsync` / `ListeningLearnerService.GetAttemptStructureAsync` apply `SeededShuffle(options, seed: attempt.RandomisationSeed, saltKey: RandomisationHelper.SaltKeyFromString("reading.q.{id}.options"))` to each question's options array before returning.
- Grading is unaffected because the answer is keyed on option ID, not position.

### Wave 1.1.4 — Sunset legacy letter writes

- Backend rejects `userAnswer` values that look like letters once 100% of in-flight attempts have completed.
- Migration removes the dual-write path.

## Backwards compatibility matrix

| Scenario | Wave 1.1.0 | Wave 1.1.1 | Wave 1.1.2 | Wave 1.1.3 | Wave 1.1.4 |
| -------- | ---------- | ---------- | ---------- | ---------- | ---------- |
| Old client writes `"A"` | accepted | accepted | accepted | accepted | rejected |
| New client writes `"opt-..."` | accepted | accepted | accepted | accepted | accepted |
| Old graded answer (DB) | unchanged | unchanged | unchanged | unchanged | unchanged |
| Per-attempt shuffle | disabled | disabled | disabled | enabled | enabled |

## Risks

1. **In-flight attempts at cutover.** The dual-write window in Wave 1.1.1 must outlast the longest possible attempt session (≈4 hours for full mock + 1 day for paused attempts). 1 week dual-write is the recommended floor.
2. **Admin item analysis.** Analytics queries that group by option **letter** (e.g. "how many learners chose 'A' on question 12?") need to migrate to grouping by **option ID** with the letter projected for display.
3. **Backfill performance.** Each Reading + Listening question gets its `OptionsJson` rewritten once. On Postgres this is an `UPDATE ... SET OptionsJson = jsonb_build_array(...)` per row, batched by 1000 rows.
4. **Speaking role-play cards.** Out of scope for this migration — cards are not grading-keyed and the existing `RandomisationHelper.SeededShuffle` already shuffles them safely.

## Test plan

- New `ReadingOptionIdBackfillTests` (SQLite + Postgres) verifying every existing row gets a deterministic ID and the row count is unchanged.
- New `ListeningOptionIdBackfillTests` (same).
- New `ReadingGradingServiceTests.AcceptsBothLetterAndOptionIdAnswers` — Wave 1.1.1 dual-write parity.
- New `ReadingShuffleTests.GradeIndependentOfDisplayOrder` — Wave 1.1.3 invariant.
- Existing `ReadingGradingService` test corpus runs unchanged.

## Cross-links

- [`MOCKS-RANDOMISATION.md`](./MOCKS-RANDOMISATION.md) — the helper that this migration unblocks.
- `backend/src/OetLearner.Api/Services/Reading/ReadingGradingService.cs`
- `backend/src/OetLearner.Api/Services/Listening/ListeningGradingService.cs`
- `docs/mocks/PROGRESS.md` Gap Register — V2 Med #6.

## Decision

This migration is **deferred to v1.1**. Mock V1 ships without per-attempt option randomisation; the deterministic shuffle helper exists in code but is gated to display-only paths.
