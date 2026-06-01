# Reading Part B / Part C Stacked Student Layout — Design

Date: 2026-06-01
Status: Approved (clarifications captured via popup)

## Goal

Change the reading **student view** so that:

- **Part B** — each of the 6 short workplace extracts is shown as a **pair**: the
  extract (left) and its single 3-option MCQ (right). All 6 pairs are stacked
  vertically on one scroll (no pagination).
- **Part C** — each of the 2 long passages is shown with the **passage on the
  left** (sticky on desktop `lg+`) and its **8 four-option MCQs stacked on the
  right**. Two passages → two sections, stacked.

Responsive rule (Part C): sticky passage on `lg+`; on mobile/tablet the passage
stacks **above** its questions.

## Scope (approved)

Two surfaces, made consistent:

1. **Exam paper simulation** — `components/domain/reading-paper-simulation.tsx`
   + `lib/reading-paper-simulation.ts`, used by `/reading/exam` and
   `/reading/paper/[paperId]`.
2. **Practice player** — `components/reading/ReadingPlayer.tsx`, used by
   `/reading/practice/[sessionId]`. Full conversion of Part B/C presentation to
   the stacked layout (Option B).

Out of scope (flagged follow-up): `/reading/diagnostic` is a separate **adaptive**
one-at-a-time engine; a stacked all-questions layout does not fit its sampling
model. Left unchanged this pass.

## Architecture

Shared presentational components (pure layout; no scoring/autosave logic):

- `ReadingPartBStack` — maps each Part B extract to its single question; renders
  stacked `[extract | question]` pairs.
- `ReadingPartCStack` — per long text renders `[sticky text | 8 questions]`;
  responsive collapse on small screens.

Both surfaces adapt their data into these components:

- Exam sim feeds `ReadingLearnerStructureDto` parts/texts/questions; existing
  `PaperQuestionControl` / `ReadingPaperMcqCircles` remain the answer controls.
- `ReadingPlayer` groups its flat `ReadingQuestionDto[]` by `passageId` to form
  the extract/text → questions grouping, reusing its existing option button +
  feedback + mark affordances inside the stacked layout.

## Data flow / invariants (unchanged)

- Answer storage, autosave, `onAnswerChange` / `onComplete`, deadlines,
  Part A answer sheet, and annotations stay exactly as today. This is a
  presentation-only change.
- Part B = 3-option MCQ, Part C = 4-option MCQ contracts unchanged (backend
  publish gate still enforces cardinality).

## Page-model change (exam sim)

`buildPartBCBookletPages` currently paginates Part B per extract and splits
Part C into separate text-only and questions-only pages. New model:

- Part B → a single stacked section (all 6 pairs), not paginated.
- Part C → one combined page per text carrying both `textIds` and `questionIds`
  (drop the questions-only page), rendered as sticky-text + stacked questions.

## Testing

- `lib/reading-paper-simulation.test.ts` — updated page-model expectations.
- `components/domain/reading-paper-simulation.test.tsx` — Part B pairing + Part C
  text/8-question stacking + HTML sanitisation retained.
- `ReadingPlayer` — add a test for grouped stacked rendering of Part B/C.
- `tests/e2e/reading/part-c-four-options.spec.ts` — keep green.

## Validation & deploy

- Docker only: `npm run docker:tsc`, `docker:lint`, `docker:test`,
  `docker exec oet-local-web npm run build`.
- Commit + push to `main`; deploy to production with fresh container rebuild.
- Separately, last: review + commit + deploy the pre-existing uncommitted
  admin-redesign working-tree changes.
