# Inline question text for Listening & Reading Part B/C

- **Date:** 2026-07-09
- **Status:** Approved (owner: build all phases, ship once; future-proof; no live candidates so no retrofit-guard special-casing)
- **Branch:** `feat/inline-bc-question-text`

## Problem

Part B/C question cards in the learner player show `See PDF` as the heading and
`Option A / Option B / Option C` as the answers. The real question text lives only
on an uploaded question-paper PDF, so candidates must scroll up to the PDF to read
each question. Owner directive: the **question heading and answer options must be
written inline at each question**, and the PDF should be dropped for B/C once the
inline text exists.

### Root cause (confirmed)

The learner renderer, the learner API, the stored JSON, the relational projection,
and an **advanced per-question editor already carry real `stem` + `options[]`
end-to-end**. The placeholders exist for one reason only: the *fast* Answer-Sheet
Builder hardcodes them.

- Listening: `app/admin/content/listening/[paperId]/questions/ListeningAnswerSheetBuilder.tsx`
  sets `stem: 'See PDF'` (l.183) and `options: ['Option A','Option B','Option C']`
  (l.65, l.184); persists only the correct letter + rationale.
- The AI Part B/C extractor panel `ListeningPartAiExtraction.tsx` (mis-named — it
  handles Part B/C) hardcodes the same on save (l.43, l.144-145).
- Reading mirrors this exactly: `ReadingAnswerSheetBuilder.tsx` hardcodes stem
  `'See PDF'` (l.235) and `MCQ3_OPTIONS`/`MCQ4_OPTIONS` (l.67-68).

So this is an **authoring + grading-contract** change, not a rendering bug.
`BCQuestionRenderer` renders whatever `prompt`/`options` it is handed — no change.

## Goals

1. Admins can **type** a real stem + 3 (Listening) / 3–4 (Reading) option texts per
   Part B/C question in the fast builder, and **AI-extract** them from the uploaded
   PDF as a prefill to review.
2. Learners see the heading + options **inline** on the question card; the
   question-paper PDF is **hidden for a B/C section once it is fully authored inline**
   (fallback to the PDF when inline text is absent).
3. A per-extract **scenario/intro line** ("You hear a nurse briefing a colleague…")
   is captured and shown once per extract so nothing is lost when the PDF is dropped.
4. **Grading is never affected by option display text** — it keys on the option
   letter/index, and old attempts keep scoring.
5. Applies to **Listening B & C** and **Reading B/C** (Reading Part C is 4-option).

## Non-goals

- No change to Part A (note-completion / gap-fill) authoring or rendering.
- No change to the OET raw→scaled scoring math (`OetScoring.OetRawToScaled`).
- No new heavyweight draft/approve pipeline for B/C — keep the lightweight
  projection path.
- Not touching Speaking/Writing.

## Key architectural decisions

### D1 — MCQ answers submit & grade by option **key/index**, text is display-only (MUST)

Today the Listening learner card submits the option **text** (`BCQuestionRenderer`
`onChange(optionText)`), and `SaveRelationalAnswerAsync` stores it verbatim
(`ListeningLearnerService.cs` l.1171/1178). The V2 grader
(`ListeningGradingService.Evaluate`, MC3, l.370-378) matches the saved value against
`ListeningQuestionOption.OptionKey` — which backfill sets to the positional letter
`"A"/"B"/"C"` (`ListeningBackfillService.cs` l.292-309). Because `IsCorrect` is
computed positionally at backfill, the **letter/index is the stable grading key**;
the option text is incidental. Reading already submits the letter (derived by index
in `toOptionList` / `McqControl`), and `ReadingGradingService.GradeMcq` is letter-based.

**Decision:** make Listening Part B/C submit and grade by the option **key (letter
`A/B/C`) / index**, exactly like Reading. Option text becomes pure display. To keep
existing / in-flight attempts scoring, resolve a saved answer at grade time via
`ListeningOptionIdHelper.ResolveLegacyAnswer` (already exists: maps a stored option
**text or numeric index → stable key**). Net effect: changing option text can never
alter a score.

Concretely:
- Learner DTO exposes options as `{ key, text }` (or a parallel `optionKeys[]`) so the
  client can render `text` but submit `key`. (`LearnerQuestionDto` /
  `MapRelationalQuestion` in `ListeningLearnerService.cs`.)
- `BCQuestionRenderer` selects/submits `key`, displays `text`, keeps the A/B/C badge.
- Grade-time resolution: before the `OptionKey` compare, run the saved value through
  `ResolveLegacyAnswer(saved, questionId, optionTexts)` so text/index/key all resolve.
  Apply in both the V2 grader and the JSON-store grader path.
- **Implementation-time verification:** confirm the JSON-backed (`la-` attempt) grader
  also matches by letter/index (the screenshot paper is a `la-` JSON attempt). Add a
  regression test that a real-prose-option paper grades identically to the placeholder
  version for the same chosen letter.

### D2 — "Inline-ready" signal for dropping the PDF

Prefer an **explicit backend flag** over string-sniffing `'See PDF'`. Add a derived
boolean on the learner section/extract DTO, e.g. `inlineTextReady`, true when every
options-bearing (B/C) question in the section has a non-placeholder stem AND
non-placeholder options. Backend computes it (mirroring `IsPdfBackedItem`,
`ListeningStructureService.cs` l.949-953) so the client never guesses. The player
gates the PDF viewer + empty-state on `!inlineTextReady`; Part A paths are untouched
(gate scoped to `question.options.length > 0`).

### D3 — Per-extract context field (new column + migration)

Add nullable `ContextIntro` (`[MaxLength(2048)]`, plain TEXT) to `ListeningExtract`
(`ListeningEntities.cs`, beside `NotesBodyMarkdown` l.260). It must be carried through
**all** paths and must NOT be gated Part-A-only (the anti-pattern is the `notesBody`
force-null for B/C at `ListeningAuthoringService.cs` l.1136, `NormalizeManifestExtract`
l.763, `ListeningBackfillService.cs` l.223). Reading gets the equivalent per-part/section
context field.

- EF migration (additive nullable column) + `LearnerDbContextModelSnapshot.cs`.
- Contracts: `ListeningAuthoredExtract` + `ListeningExtractPatch` (svc l.148-220),
  `lib/listening-authoring-api.ts` (`ListeningAuthoredExtract` l.135-160,
  `ListeningExtractPatchBody` l.281-297).
- Persist/read: `NormalizeExtractFor/FromStorage`, `ApplyExtractPatch`, backfill
  projection, learner `ListeningExtractMetaDto` (l.3350) + `MapRelationalExtract`.
- Render once per extract above the cards in the player.

### D4 — No-clobber in the fast builder

`handleSaveAll` currently spreads `previous` then overwrites `stem`/`options`
(Listening l.178-184; Reading l.235). The rebuilt builder must **seed from and
preserve** any existing real stem/options (advanced-editor content) and only fall
back to placeholders when a field is genuinely blank.

### D5 — Publish-gate adjustments

- Listening: replacing `'See PDF'` removes the `IsPdfBackedItem` exemption, so the
  transcript-evidence advisory codes begin firing for B/C. They are advisory (warning,
  not blocking — `AdvisoryPublishGateCodes` l.88-99); acceptable, but confirm no B/C
  item flips to a hard block.
- Reading: `ValidatePaperAsync` **requires** a QuestionPaper PDF per part A/B/C as a
  hard ERROR (`ReadingStructureService.cs` l.1021-1045). Relax so a B/C part that is
  complete inline is publish-ready **without** a PDF; keep Part A's PDF requirement.

## Data model / contract changes

| Area | Change |
|---|---|
| `ListeningExtract` (+ Reading equiv) | new nullable `ContextIntro` column + EF migration + snapshot |
| Listening learner DTO | options as `{key,text}` (or parallel `optionKeys[]`); add `contextIntro`; add `inlineTextReady` |
| `stem` / `options[]` | **no change** — already end-to-end |
| Listening Part B/C AI import contract | `ListeningPartBCAnswer` / `ListeningPartBCImportResult` gain per-question `stem` + 3 option texts (`lib` l.442-458) |
| `ListeningPartBCExtractionService` | widen `SystemPrompt`, `ToolSchemaJson`, `BcToolAnswer`, `ValidateAndProject`; raise `max_tokens` (was 4000) |

## Phase design

### Phase 0 — Grading safety (foundation, invisible) → D1

`BCQuestionRenderer` submits key; learner DTO exposes key+text; grade-time
`ResolveLegacyAnswer` in the V2 grader and the JSON grader. Tests: real-prose paper
grades identically by chosen letter; legacy text/index answers still resolve.

### Phase 1 — Manual inline text (the visible win) → D4

Rebuild the row UI in `ListeningAnswerSheetBuilder.tsx`: add a stem `Textarea` + 3
option-text inputs beside the existing correct-letter select + rationale. Seed from
existing values; stop hardcoding placeholders; keep exactly 3 options. Save through
the existing `replaceListeningStructure`. Learner renders inline automatically.

### Phase 2 — Replace the PDF + extract context → D2, D3, D5(Listening)

New `ContextIntro` column + migration + full wiring. Player: render `contextIntro`
once per extract; gate `ListeningQuestionPaperViewer` + empty-state on
`!inlineTextReady` for B/C (page.tsx ~l.1673-1685); keep Part A PDF/overlay/notes
paths. `ListeningPaperSimulation` renders no PDF and shows `question.text` inline
already — verify it shows real text and no `See PDF` sentinel for migrated papers.

### Phase 3 — AI "Read from PDF" for B/C → AI widening

Widen `ListeningPartBCExtractionService` so `emit_part_bc_answers` also returns, per
question, `stem` + `optionA/optionB/optionC`, grounded in the OCR'd question paper.
Raise `max_tokens`. Never hard-fail on weak OCR — degrade to the existing review/stub
warning and keep the letter extraction independent so the answer key still lands.
Extend `ListeningPartAiExtraction.tsx` rows with editable stem + option inputs;
`onSaveAll` uses extracted/edited text, placeholders only when left blank.

### Phase 4 — Reading B/C parity → mirrors 1-3 + D5(Reading)

Reading learner surface (`app/reading/paper/[paperId]/page.tsx`) already renders
stem + options inline beside the PDF. Work: rebuild `ReadingAnswerSheetBuilder.tsx`
rows (stem + 3/4 option texts, no-clobber, keep MCQ3=3 / MCQ4=4 counts); add the
Reading context field; hide `<ReadingPdfViewer>` for B/C when inline-ready (PartBody
~l.1474, widen the grid l.1469; Part A keeps PDF); relax the B/C PDF publish gate
(l.1021-1045). Reading AI extraction (`ReadingExtractionService` already emits full
manifests) can prefill the builder. `ReadingPlayer.tsx` (practice) is passage-based,
no PDF — benefits automatically once text is populated.

## Testing

- **Grading (critical):** unit test — same paper, placeholder options vs real prose
  options, same selected letter → identical score. Legacy saved answer as text and as
  index both resolve. Cover V2 grader and JSON grader.
- Builder: save real stem + options; re-save does not clobber advanced-editor text;
  option count stays 3 (Listening) / 3–4 (Reading).
- AI extract: widened tool returns stem+options; weak-OCR degrades to warning, not
  hard-fail; token budget covers 12 Part C questions.
- Player gate: B/C section with inline text hides PDF + empty-state; without it, PDF
  still shows; Part A unaffected.
- Reading publish gate: B/C-with-inline-text publishes without a PDF; Part A still
  requires its PDF; 4-option Part C round-trips.

## Risks & mitigations

- **Silent grading break from text change** → D1 (grade by key/index + legacy
  resolver) + explicit regression test. Highest priority.
- **Builder clobbers advanced-editor text** → D4 seed-and-preserve.
- **Dropping PDF loses extract context** → D3 context field; gate only when fully
  inline; PDF fallback otherwise.
- **AI token truncation for Part C** → raise `max_tokens`; letter extraction
  independent of text extraction.
- **Reading Part A PDF requirement accidentally relaxed** → keep the gate part-scoped.
- **`la-` JSON store vs `lat-` relational store divergence** (per prior work) →
  verify + test both grader paths.

## Rollout

Backend build/tests + EF migration run on CI (owner directive: heavy compute on CI).
Single release covering all phases. Squash-merge to `main` triggers the blue/green
prod deploy.
