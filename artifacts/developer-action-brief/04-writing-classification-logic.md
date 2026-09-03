# Writing classification logic (exact, current)

## Architecture: rule-based ONLY (no AI classifier)
There is NO AI Profession / Letter Type classifier in the backend. Classification is deterministic and folder/filename-driven, executed in this order:

```
source path + folder name + filename
  → ContentConventionParser.TryClassify (subtest + profession scope + letter/card hint)
  → LetterTypeRules regexes (writing) / CardTypeRules (speaking)
  → RealContentFolderImporter hint map (WritingLetterTypeMap) for zip/folder imports
  → canonical-folder map for the Tutor Book Writing 1–6 seed (scripts/extract-writing-pdfs)
  → normalization (trim + lowercase; stored as snake_case codes)
  → validation (WritingContentStructure.Validate: must be one of 6 canonical codes to publish)
  → persistence (ContentPaper.LetterType; projected to WritingScenario.LetterType)
  → task-serving + model-answer pipeline reads the stored code (never re-classifies)
```

AI is used ONLY downstream for grading/feedback (Prompts/Writing/*, evaluation pipeline) — never to decide Profession or Letter Type. Hence `ai_confidence` is empty for every row in the QA export, `review_flag`/`pending_review` derive from the `Other` fallback, and there are no model/prompt/threshold/retry knobs for classification.

## 1. Profession
- `ContentConventionParser.TryClassify()`:
  - `same for all professions` in path → `AppliesToAllProfessions=true`.
  - `(Medicine only)` in path → `ProfessionId=medicine`.
  - Listening/Reading → `AppliesToAll=true` (shared).
  - Writing/Speaking → `profession ??= "medicine"` (default when no hint).
  - `DetectProfession()` (references path) currently only detects `medicine`; all other professions come from the explicit folder segment (`Dentistry/Medicine/Nursing/Pharmacy/Physiotherapy/Radiography`) or the `OET Materials & Videos Data/Writing/{Arabic,Medicine,Nursing,Pharmacy}` root (Arabic → `medicine-ar` in the export; backend treats Arabic sets as profession-scoped Medicine content).
- `RealContentFolderImporter.ParseWritingGroup()`: `ProfessionId="medicine"` for Writing papers (Medicine-only source); other professions arrive via the `OET/Materials ( To be Uploaded )/Writing/{Profession}/` roots.
- Stored: `ContentPaper.ProfessionId` (+ `AppliesToAllProfessions`), projected to `WritingScenario.Profession`.

## 2. Letter Type
Canonical set (`WritingContentStructure.CanonicalLetterTypes`):
`routine_referral`, `urgent_referral`, `non_medical_referral`, `update_discharge`, `update_referral_specialist_to_gp`, `transfer_letter`.

Detection (first match wins):
1. Canonical Writing 1–6 folders (`scripts/extract-writing-pdfs`): Writing 1→routine_referral, 2→non_medical_referral, 3→urgent_referral, 4→update_discharge, 5→update_referral_specialist_to_gp, 6→transfer_letter.
2. `ContentConventionParser.LetterTypeRules` regexes on `folder + filename`:
   - `routine\s*referral` → routine_referral
   - `urgent\s*referral` → urgent_referral
   - `non[\s-]*medical|occupational\s*therapist` → non_medical_referral
   - `update.*discharge|discharge.*gp` → update_discharge
   - `update.*referral|specialist.*gp` → update_referral_specialist_to_gp
   - `transfer\s*letter` → transfer_letter
3. `RealContentFolderImporter.WritingLetterTypeMap` substring hints: routine, urgent, non medical/non-medical, update & discharge/discharge, update & referral/specialist → same codes; default `routine_referral` when the `Writing N (hint)` pattern matches but no hint hits.
4. Fallback: `Other` (not canonical; publish blocked until an admin sets a canonical code).

Normalization: trim + lowercase snake_case. Validation: `WritingContentStructure.Validate()` errors when LetterType missing or non-canonical (`letter_type` error); criteria-focus missing is a warning. Persistence: `ContentPaper.LetterType` (lowercased on write in `ContentPaperService`), `WritingScenario.LetterType`.

## "Other" mapping + review thresholds
- No confidence scores, no retry, no AI fallback. `Other` = deterministic fallback when no rule matches (reference PDFs like Grammar Rules/Criteria/Booklets, videos, or case notes without a letter hint in the path).
- Review rule: `Other` → `needs_review` / `pending_review=yes` in the QA export; admin must set a canonical Letter Type in the Task Builder before publish. Non-Other → no flag.
- Rejection/default: unpublished (`Status=draft` and/or `CandidateVisible=false`) until the gate passes.

## Import mappings (roles)
- Filename contains `answer sheet`/`model answer`/`corrected` → `ModelAnswer`; `case notes`/`case-notes` → `CaseNotes`; `criteria`/`grammar`/`rulebook`/`booklet` → `Reference`; other PDFs/DOCXs → `CaseNotes-candidate`; images → supporting.

## Code locations
- Rules: `backend/src/OetLearner.Api/Services/Content/ContentConventionParser.cs:114-122,374-422,502-503`
- Hint map + grouping: `backend/src/OetLearner.Api/Services/Content/RealContentFolderImporter.cs:371-439`
- Canonical set + gate: `backend/src/OetLearner.Api/Services/Content/WritingContentStructure.cs:16-27,49-84`
- Persistence: `backend/src/OetLearner.Api/Services/Content/ContentPaperService.cs:210,250,515,569-570`
- Seed map: `scripts/extract-writing-pdfs/Program.cs:23-31`
- Export reproduction: `scripts/writing-qa-export.mjs` (mirrors the above rules; run `pnpm run writing:qa-export`)
