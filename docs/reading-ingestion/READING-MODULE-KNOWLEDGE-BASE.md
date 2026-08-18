# Reading Module Knowledge Base

Status: **CONDITIONALLY READY** for future ingestion.

This document reverse-engineers the live Reading module from code. It is the
gate before any OCR pipeline or content insert. No Reading papers were inserted
as part of this work.

## 1. Verdict

The official 42-item Reading paper contract is verified in backend publish
validation and the exam player. The local insert path already exists. First
source booklets have not been uploaded in this session, and replace-import can
wipe QuestionPaper PDFs. Do not start OCR until a real booklet is present and
the dry-run validator passes on a transcribed manifest.

## 2. Systems of record

- Persistence: PostgreSQL via EF Core `LearnerDbContext`.
- Curatorial unit: `ContentPaper` with `subtestCode=reading`.
- Structure: `ReadingPart` A/B/C → optional `ReadingText` / `ReadingSection` →
  `ReadingQuestion`.
- Attempts: `ReadingAttempt` → `ReadingAnswer`.
- Live TS contract: `lib/reading-authoring-api.ts`.
- Stub, do not use: `lib/types/admin/reading-authoring.ts`.
- Authoritative official insertion UI: `ReadingAnswerSheetBuilder`.
- Authoritative server gate: `ReadingStructureService.ValidatePaperAsync`.

There is a second, incompatible pathway stack (`lib/reading-pathway-api.ts` +
`components/reading/ReadingPlayer.tsx`) that uses HTML passages and plain-string
answers. Official exam papers do not go through that stack.

## 3. Canonical published shape

| Part | Items | Points | Minutes | Texts if present | Official types |
| --- | --- | --- | --- | --- | --- |
| A | 20 | 20 | 15 | 4 | Matching 1-7 **or** 1-8; remaining two blocks are ShortAnswer / SentenceCompletion and may swap. Last block is always 15-20. |
| B | 6 | 6 | 45 | 6 | MultipleChoice3, one per extract |
| C | 16 | 16 | 45 | 2 | MultipleChoice4, 8 per article |

Total raw = 42. Each item is exactly 1 point. `30/42 ≡ 350` via owner lookup
only. Fuzzy / Levenshtein marking is forbidden (R04.5).

Texts are optional. Official papers should use `texts: []` (PDF-only). If any
texts exist for a part, counts must be exactly 4 / 6 / 2 **and** B/C questions
must be linked: 1 per B extract, 8 per C article (`readingTextDisplayOrder`).
Part A links are all-or-nothing. Import requires the `texts` array to be
present even when empty.

## 4. IDs and ordering

- Entity IDs: `Guid.NewGuid().ToString("N")` (32 hex).
- Option IDs: `opt-` + first 12 hex of SHA256(`${questionId}:${index}`).
- `DisplayOrder` must be unique and contiguous from 1 inside each part.
- Public numbers: A and B unchanged; C internal + 6 → 7-22.

## 5. Payloads

`OptionsJson` and `CorrectAnswerJson` are JSON strings.

- MCQ options: string array or objects whose keys are only
  `id|value|label|text|title|letter`.
- MCQ / matching correct answers: a **letter string**, not option text.
- Short answers / sentence completion: JSON string. Synonyms: string array.
- Matching at publish: one of `A|B|C|D`, not a set.
- Learner DTOs never include answers, explanations, or synonyms
  (`ReadingLearnerSafeProjection`).

## 6. Practice-only types

`FillInBlank`, `ShortAnswerLabeled`, and `MultipleChoiceFlexible` exist in the
enum and advanced editor. They are not allowed by
`IsQuestionTypeAllowedForPart` and fail publish. Do not approximate official
booklet items as these types.

## 7. Player

Canonical exam player: `app/reading/paper/[paperId]/page.tsx`.

- Always PDF-first (`ReadingPdfViewer`). `bodyHtml` is not shown to candidates.
- Dispatch: MCQ3/4/Flexible → radios; Matching → A-D (fallback to texts if
  options empty); labeled → box inputs; everything else → text box.
- Student answers are saved as JSON via `saveReadingAnswer`.
- Stem is often `See PDF` for Part A; booklet text lives in the PDF.

## 8. Scoring

`ReadingGradingService` strategies:

- Matching / short text: strict compare. Synonyms only if policy
  `ShortAnswerAcceptSynonyms`.
- MCQ: letter, with `OPT-` dual-read for older option IDs.
- No fuzzy credit.
- Scaled score is an owner lookup, not a local formula beyond the documented
  30/42 = 350 calibration point.

## 9. Insert path (already exists)

1. `POST /v1/admin/papers`
2. Chunked `POST /v1/admin/uploads` and attach assets
3. `POST .../reading/ensure-canonical`
4. `POST .../reading/manifest` `{ replaceExisting, manifest }`
5. `GET .../reading/validate`
6. `POST .../publish`

Local script: `scripts/admin/import-reading-manifests-local.mjs` (localhost
only). Offline gate: `scripts/admin/validate-reading-manifest.ts` and importer
`--dry-run`.

Default imported `ReviewState` is `Draft` unless the manifest sets `Published`.
Publish still requires every question `Published` plus explanation and evidence.

`EvidenceSentence` is required by `ValidatePaperAsync` but is **not** a field on
`ReadingQuestionManifest` and is dropped by `ImportManifestAsync`. After a write
import, set evidence in admin before publish. Dry-run publish-ready is not
import publish-ready until that backend gap is closed.

## 10. Known risk: replace import wipes PDFs

`ImportManifestAsync(replaceExisting:true)` deletes existing QuestionPaper
assets and only re-adds them if the manifest itself includes
`QuestionPaperAsset`. The local importer uploads assets first, then calls
replace import, which can leave the paper without PDFs and fail publish.
Dry-run warns. After a replace import, re-attach Part A/B/C primary PDFs and
re-validate.

## 11. Existing extraction

`ReadingExtractionService` is draft-only, human-approval required, and must not
invent keys. Policy currently prefers PDF-first authoring; the global
extraction dashboard is legacy. This work does **not** start a new OCR
importer.

## 12. Readiness

| Question | Answer |
| --- | --- |
| Is the contract known? | Yes, from live validate + player + builder. |
| Can we validate a manifest offline? | Yes, `lib/reading-manifest-contract.ts`. |
| Have source booklets been supplied? | No, not in this session. |
| Should we insert content now? | No. |
| Should we build OCR now? | No. |
| Ready for first real booklet? | **CONDITIONALLY READY** after dry-run + human review + post-import evidence entry. Prefer PDF-only `texts: []` unless B/C links are complete. |

## 13. Operator commands

```powershell
node --experimental-strip-types --no-warnings=ExperimentalWarning `
  scripts/admin/validate-reading-manifest.ts --manifest path/to/bundle.json

node scripts/admin/import-reading-manifests-local.mjs `
  --manifest path/to/bundle.json --dry-run
```

Do not run the importer without `--dry-run` until a reviewed bundle exists.
Never point the importer at a non-local API.

## 14. Code map

| Concern | File |
| --- | --- |
| Entities / enums | `backend/src/OetLearner.Api/Domain/ReadingEntities.cs` |
| Import + validate | `backend/.../ReadingStructureService.cs` |
| Marking | `backend/.../ReadingGradingService.cs` |
| Option IDs | `backend/.../ReadingOptionIdHelper.cs` |
| Learner projection | `backend/.../ReadingLearnerSafeProjection.cs` |
| Admin/learner API types | `lib/reading-authoring-api.ts` |
| Public numbers | `lib/reading-display-number.ts` |
| Offline contract | `lib/reading-manifest-contract.ts` |
| Exam player | `app/reading/paper/[paperId]/page.tsx` |
| Official builder | `app/admin/content/reading/[paperId]/questions/ReadingAnswerSheetBuilder.tsx` |
| Local importer | `scripts/admin/import-reading-manifests-local.mjs` |

## 15. Uncertainties (not guessed)

- Whether review DTOs already public-number Part C.
- Whether `part.instructions` is shown anywhere in the exam player.
- Whether `ReadingStructureEditor` is still mounted on a live admin route.
- Pathway `partCode` numeric encoding in production.
