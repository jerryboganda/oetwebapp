# Source to Reading schema mapping

Verified against `ReadingStructureService`, `ReadingEntities`, `lib/reading-authoring-api.ts`, and `ReadingAnswerSheetBuilder`. Do not use `lib/types/admin/reading-authoring.ts` (stub).

## Paper

| Source | Schema | Notes |
| --- | --- | --- |
| Paper title / slug | `ContentPaper.title`, `slug` | `subtestCode` must be `reading` |
| Official booklet PDFs | `ContentPaperAsset` role `QuestionPaper`, part `A` / `B` / `C` | Publish requires exactly one primary ready PDF or image per part |
| Answer key PDF | `AnswerKey` asset | Optional for structure; not a substitute for `correctAnswerJson` |

## Parts

| Official booklet | Persist as | Time | Items | Texts if HTML present |
| --- | --- | --- | --- | --- |
| Part A | `ReadingPart.partCode=A` | 15 | 20 | 4 or 0 |
| Part B | `ReadingPart.partCode=B` | 45 | 6 | 6 or 0 |
| Part C | `ReadingPart.partCode=C` | 45 | 16 | 2 or 0 |

Texts are optional. The official exam player is PDF-first and does not render `bodyHtml`.

- Import requires a `texts` array on every part. Use `[]` for PDF-only.
- If any B/C texts are present, every text must have the official linked count: **B = 1 question per text**, **C = 8 questions per text**, via `readingTextDisplayOrder`.
- If any Part A question is linked, **all** Part A questions must be linked.
- Unlinked B/C texts pass neither this dry-run nor backend `ValidatePaperAsync`.

## Questions

| Booklet item | `displayOrder` | Public number | `questionType` | `optionsJson` | `correctAnswerJson` |
| --- | --- | --- | --- | --- | --- |
| Part A matching Q1-7 | 1-7 | 1-7 | `MatchingTextReference` | `[]` or `["Text A",…]` | `"A"` / `"B"` / `"C"` / `"D"` |
| Part A short answer Q8-14 | 8-14 | 8-14 | `ShortAnswer` | `[]` | JSON string, e.g. `"ORT"` |
| Part A sentence completion Q15-20 | 15-20 | 15-20 | `SentenceCompletion` | `[]` | JSON string |
| Part B extract 1-6 | 1-6 | 1-6 | `MultipleChoice3` | 3 strings | letter `"A"`-`"C"` |
| Part C Q7-22 | 1-16 | 7-22 | `MultipleChoice4` | 4 strings | letter `"A"`-`"D"` |

Internal C order is 1-16. Public numbers add 6 (`lib/reading-display-number.ts`).

## Do not map

| Source appearance | Why |
| --- | --- |
| Labeled boxes / multi-blank | `ShortAnswerLabeled` is practice-only and fails publish |
| 2/5/6-option MCQ | `MultipleChoiceFlexible` is practice-only |
| Cloze / gap-fill as a distinct exam type | `FillInBlank` is practice-only; official completion is `SentenceCompletion` |
| Matching as a set of letters | Publish matching is a **single** letter, not an array |
| HTML passage as the candidate booklet | Player shows the QuestionPaper PDF |

## Required publish metadata

Every question: `points=1` (do not omit; C# deserializes missing points as 0), `OptionsJson` present, `ExplanationMarkdown`, `EvidenceSentence`, `ReviewState=Published`.
Importer default review state is `Draft` unless the manifest sets `Published`.

**Import gap:** `ReadingQuestionManifest` / `ImportManifestAsync` persist explanation, options, answers, review state, and `readingTextDisplayOrder`. They do **not** persist `EvidenceSentence`. A dry-run-green bundle still fails live publish until evidence is written through the admin UI/API. Do not assume OCR JSON `evidenceSentence` survives import.
