# Reading Module — Save & Upload Playbook

> **Audience:** future coding agents / LLMs. Read this file first. Do **not**
> restart the research loop. This is the operational contract used to import
> official OET Reading papers (Jayden Book JB1–JB5 and later books).
>
> **Status:** authoritative for save / import / validate / publish as of
> 2026-08-18. Older notes in `docs/reading-ingestion/READING-MODULE-KNOWLEDGE-BASE.md`
> and `docs/runbooks/local-reading-sample-import.md` are **stale** on Part A
> last-block and EvidenceSentence import. This file wins.

---

## 0. Start here (90-second brief)

Official Reading is **PDF-first, 42 points, 20/6/16**. You do **not** OCR
passages into HTML. You attach one primary QuestionPaper PDF per Part A/B/C,
then import a structured answer-sheet manifest, then validate, then publish.

| Step | Action | Gate |
| --- | --- | --- |
| 1 | Inventory the booklet + printed answer key | Complete exam = Part A 20 + Part B 6 + Part C 16. Keys live **in the PDF**. |
| 2 | Auto-detect Part A layout | Matching is 1–7 **or** 1–8. Last block starts at **15 or 16**. Middle/last swap SA vs SC. |
| 3 | Build a JSON bundle | Field is `correctAnswerJson` (JSON-encoded string). Include `evidenceSentence`. `texts: []`. |
| 4 | Offline dry-run | `ERROR COUNT 0`, 42 points, layout id matches the booklet headings. |
| 5 | Write import via **local API only** | `scripts/admin/import-reading-manifests-local.mjs` refuses non-localhost. |
| 6 | Backend validate + publish | `GET .../reading/validate` then `POST .../publish`. |
| 7 | Compare answers to the printed key | Never invent keys. Exception: JB2 Part C Q7–14 printed key is a paste error. |

Do **not**:

- Point the importer at `https://api.oetwithdrhesham.co.uk`.
- Retry bootstrap / admin passwords (lockout after 5).
- Recreate `oet-api-green` from the old GHCR image while the 2026-08-18 DLL overlay is live.
- Send **Full Reading Exam** (`/reading/exam`) to `/mocks`. That route lists published papers by book folder. Mock bundles are a separate surface.
- Assume last Part A block is always 15–20.
- Use `correctAnswer` instead of `correctAnswerJson`.
- Put HTML passages in `texts` for official papers.
- Use practice-only types (`FillInBlank`, `ShortAnswerLabeled`, `MultipleChoiceFlexible`).
- Enable fuzzy / Levenshtein marking.
- Touch the main checkout at `D:\Projects\OET with Dr Hesham\...`. Work only in the Copilot worktree.

---

## 1. What a complete Reading exam is

| Part | Items | Points | Minutes | Official types | Texts if present |
| --- | ---: | ---: | ---: | --- | ---: |
| A | 20 | 20 | 15 | Matching A–D, then ShortAnswer / SentenceCompletion | 4 |
| B | 6 | 6 | 45 (shared with C) | `MultipleChoice3`, one per extract | 6 |
| C | 16 | 16 | 45 (shared with B) | `MultipleChoice4`, 8 per article | 2 |
| **Total** | **42** | **42** | **60** | | |

- Each item is exactly 1 point. `30/42 ≡ 350/500` via `lib/scoring.ts` / `OetScoring`. Never inline a formula.
- Public numbers: A and B unchanged. Part C internal 1–16 is shown to learners as **7–22** (`internal + 6`).
- Official papers are **PDF-only**: `texts: []` on every part. The player is `ReadingPdfViewer`. Stem is often the booklet prompt; booklet body lives in the PDF.
- If any texts exist for a part, counts must be exactly 4 / 6 / 2 **and** B/C questions must be linked (`readingTextDisplayOrder`: 1 per B extract, 8 per C article). Part A links are all-or-nothing. Import still requires the `texts` array to be present even when empty.
- Publish needs: explanation, evidence, `ReviewState=Published` on every question, `SourceProvenance` on the paper, one **primary** `QuestionPaper` PDF for Part A, B, and C.

Persistence: `ContentPaper` (`subtestCode=reading`) → `ReadingPart` A/B/C → `ReadingQuestion`. Assets: `ContentPaperAsset` → `MediaAsset`. Status: `0=Draft`, `4=Published`. Question `ReviewState` `5=Published`.

---

## 2. Part A layouts — detect automatically, never hard-code 1–7 / 8–14 / 15–20

Official Part A is always 20 items, but the three task blocks move.

```
matchingEnd ∈ {7, 8}
lastStart   ∈ {15, 16}
middleType  ∈ {ShortAnswer, SentenceCompletion}
lastType    = the other of those two
```

That is **8 official layouts**. Source of truth:

- TS: `lib/reading-part-a-layout.ts`
- C#: `backend/src/OetLearner.Api/Services/Reading/ReadingPartALayout.cs`
- Unique publish error: `Part A last block must start at question 15 or 16.`

### 2.1 Booklet cues (classify in this order)

1. **Matching** = “decide which text A–D” / “which text” / “A, B, C or D” / “information comes from”.
2. **SentenceCompletion** = “Complete each of the sentences” / “Complete the following sentences”. Classify **complete** before the generic “word or short phrase” line.
3. **ShortAnswer** = “Answer each of the questions” / “Answer the following” / leftover “word or short phrase”.

Headings look like `Questions 1-8`, `Questions 1- 8`, `Questions 8-15`, `Questions 16-20`.

### 2.2 Known live papers

| Paper | Matching | Middle | Last | Layout id |
| --- | --- | --- | --- | --- |
| JB1 Bed Bugs | 1–7 | 8–15 ShortAnswer | 16–20 SentenceCompletion | `1-7-matching/8-15-ShortAnswer/16-20-SentenceCompletion` |
| JB2 Obstetric Ultrasound | 1–7 | 8–15 SA | 16–20 SC | same |
| JB3 Skin Lightening | 1–7 | 8–15 SA | 16–20 SC | same |
| JB4 TMJ Disorders | 1–7 | 8–14 SA | 15–20 SC | classic |
| JB5 Resveratrol | 1–7 | 8–14 SA | 15–20 SC | classic |
| Sample 5 (historical) | 1–8 | 9–14 | 15–20 | matchingEnd=8, lastStart=15 |

JB1–JB3 are why lastStart **16** exists. A live API that still says last block must be 15–20 will reject them.

### 2.3 Authoring / import rule

- Prefer `detectPartALayoutFromBookletText(questionPaperText)`.
- At publish, `detectPartALayoutFromQuestions` is strict: contiguous 1–20, one of the 8 layouts.
- While typing, `suggestPartALayout` may fall back to classic 1–7 / 8–14 / 15–20. Do not trust the suggestion once all 20 types are present.

---

## 3. Manifest contract (the thing you save)

Offline oracle: `lib/reading-manifest-contract.ts`.
CLI: `scripts/admin/validate-reading-manifest.ts`.
Backend authority after write: `ReadingStructureService.ValidatePaperAsync`.

### 3.1 Bundle shape

```json
{
  "papers": [
    {
      "slug": "jayden-book-01-bed-bugs",
      "title": "Jayden Book 1 — Bed Bugs",
      "subtestCode": "reading",
      "difficulty": "standard",
      "estimatedDurationMinutes": 60,
      "tagsCsv": "reading,jayden-book,official-key",
      "sourceProvenance": "Jayden Book JB1 official PDF and printed answer key",
      "paper": { "slug": "...", "title": "...", "subtestCode": "reading", "sourceProvenance": "..." },
      "assets": [
        { "role": "QuestionPaper", "part": "A", "sourcePath": "JB1.pdf", "makePrimary": true },
        { "role": "QuestionPaper", "part": "B", "sourcePath": "JB1.pdf", "makePrimary": true },
        { "role": "QuestionPaper", "part": "C", "sourcePath": "JB1.pdf", "makePrimary": true }
      ],
      "manifest": {
        "parts": [
          { "partCode": "A", "timeLimitMinutes": 15, "texts": [], "questions": [] },
          { "partCode": "B", "timeLimitMinutes": 45, "texts": [], "questions": [] },
          { "partCode": "C", "timeLimitMinutes": 45, "texts": [], "questions": [] }
        ]
      }
    }
  ]
}
```

One booklet PDF reused for A/B/C is allowed and is what Jayden used. `makePrimary: true` is required for each part.

### 3.2 Question fields that agents get wrong

| Field | Rule |
| --- | --- |
| `correctAnswerJson` | **This is the field name.** It is a JSON-encoded string, not a raw letter. Matching / MCQ: `"\"B\""`. Short text: `"\"DDT\""`. Synonyms: a JSON array string. There is **no** `correctAnswer` field on the import DTO. |
| `optionsJson` | Matching: `"[\"A\", \"B\", \"C\", \"D\"]"`. MCQ3: A–C. MCQ4: A–D. Objects may only use keys `id\|value\|label\|text\|title\|letter`. |
| `explanationMarkdown` | Required to publish. Cite the official key. |
| `evidenceSentence` | Required to publish. **Is persisted** by `ImportManifestAsync` (this was a gap; it is closed). |
| `reviewState` | Set `"Published"` in the manifest or publish will fail even if answers exist. Default import state is Draft. |
| `displayOrder` | Unique, contiguous from 1 inside each part. |
| `points` | Always 1. |
| `readingTextDisplayOrder` | `null` for official PDF-only papers. |
| IDs | Entity IDs = `Guid.NewGuid().ToString("N")`. Option IDs = `opt-` + first 12 hex of SHA256(`${questionId}:${index}`). Do not invent ad-hoc option ids. |
| Matching answers | One of `A\|B\|C\|D`, never a set, never the passage title. |
| MCQ answers | A letter, never the option prose. |
| Short answers | Copy the printed key **word-for-word**. No paraphrasing. Synonyms only if the printed key lists them. |

### 3.3 Allowed official types

- Part A: `MatchingTextReference`, `ShortAnswer`, `SentenceCompletion` in the detected block positions.
- Part B: `MultipleChoice3` only.
- Part C: `MultipleChoice4` only.

Practice-only types exist in the enum and fail `IsQuestionTypeAllowedForPart`. Do not use them for booklet papers.

---

## 4. How to extract a booklet without guessing

1. Read the PDF. The printed answer key is usually at the back of the same file.
2. Detect Part A headings first (section 2). Type every A item to match that layout.
3. Part B = 6 extracts, 6 × 3-option MCQ, public numbers 1–6.
4. Part C = 2 articles × 8 × 4-option MCQ. Store internal 1–16. Learners see 7–22.
5. Transcribe stems from the question paper, answers from the **printed key**, evidence from the matching sentence in the passage.
6. Dry-run. Then compare 42 answers to the printed key in a side-by-side table. `ERROR COUNT` must be 0.

### 4.1 Jayden official keys (do not invent)

**JB1 Bed Bugs**
- A matching 1–7: B, D, C, D, A, A, C
- A 8–15 SA: `0%`, `DDT`, `skin reactions`, `95%`, `iron deficiency`, `international travel`, `40`, `allergens`
- A 16–20 SC: `DDT`, `human immunodeficiency virus`, `vision`, `bullous eruptions`, `pseudoscent`

**JB2 Obstetric Ultrasound**
- A matching 1–7: B, A, D, C, D, B, C
- A 8–15 SA: `10 MHz`, `millennium development goals`, `1744`, `lower frequencies`, `MDG 5`, `sonography`, `620 per 100,000`, `83`
- A 16–20 SC: `Ultrasound training`, `concern`, `obstetric care`, `Higher frequencies`, `maternal-child bonding`
- **Part C Q7–14 printed key is a paste of JB1 Apo E answers.** Questions are “Eye Damages in Divers”. Overrides from the JB2 passage: `7C, 8D, 9A, 10A, 11B, 12C, 13A, 14B`. C 15–22 (Plumbism) matches the printed key.

**JB3 Skin Lightening**
- A matching 1–7: A, D, C, B, D, C, B
- A 8–15 SA: `topical`, `unstaffed coin-operated`, `100`, `Cataract`, `hydroquinone`, `artificial tanning methods`, `melanin`, `recurring`
- A 16–20 SC: `Steroids`, `malignant melanoma`, `health effects`, `highly potent steroids`, `Eye protection`

**JB4 TMJ Disorders**
- A matching 1–7: B, D, A, A, B, C, D
- A 8–14 SA: `dentist`, `dull aching pain`, `Visual Analogue Scale`, `36.5 years`, `jaw locking`, `individual specific physiotherapy`, `jaw`
- A 15–20 SC: `joints`, `restricted mandibular movements`, `teeth grinding`, `non-invasive treatment`, `chronic`, `neck or shoulders`

**JB5 Resveratrol**
- A matching 1–7: C, B, B, C, A, D, D
- A 8–14 SA: `Resveratrol`, `apoptosis`, `693`, `Bacillus cereus`, `endothelial cells`, `blood vessel function`, `injuries`
- A 15–20 SC: `French paradox`, `heart health`, `drinking habits`, `Beer drinking`, `anti-carcinogenic`, `middle age`

Session builder (not in git): Copilot session `files/build_jayden_manifests.py` + `files/jayden-import/jayden-book-reading.json`. Keep new booklets out of git unless the owner asks.

---

## 5. Save / upload pipeline (the only supported write path)

### 5.1 API sequence

```
POST /v1/admin/papers
POST /v1/admin/uploads          (chunked; use server chunkSizeBytes)
POST /v1/admin/papers/{id}/assets
POST /v1/admin/papers/{id}/reading/ensure-canonical
POST /v1/admin/papers/{id}/reading/manifest   { replaceExisting, manifest }
GET  /v1/admin/papers/{id}/reading/validate
POST /v1/admin/papers/{id}/publish
```

Auth:

- Validate/structure group requires `AdminContentWrite` (`content:write`).
- Publish requires `AdminContentPublish` (`content:publish`).
- Debug headers (`X-Debug-Role`, `X-Debug-UserId`, `X-Debug-AdminPermissions`) work **only** when the API `IsDevelopment()`. Production returns 401.

### 5.2 Local importer

```powershell
# Offline first — no API, no credentials
node --experimental-strip-types --no-warnings=ExperimentalWarning `
  scripts/admin/validate-reading-manifest.ts --manifest path\to\bundle.json

node scripts/admin/import-reading-manifests-local.mjs `
  --manifest path\to\bundle.json --dry-run

# Write. Host must be localhost / 127.0.0.1.
# Default API is :8080. Jayden prod-via-tunnel used :5198.
node scripts/admin/import-reading-manifests-local.mjs `
  --manifest path\to\bundle.json `
  --api http://127.0.0.1:5198 `
  --dev-auth `
  --debug-user-id auth_admin_local_001 `
  --replace-existing
```

Flags:

| Flag | Meaning |
| --- | --- |
| `--dry-run` | Offline contract only. |
| `--replace-existing` | Update same slug. Unpublishes to draft first. |
| `--no-publish` | Stop after validate. |
| `--dev-auth` | Development debug headers. Needs `OET_DEBUG_USER_ID` / `--debug-user-id`. |
| `--email` / `--password` | Real sign-in when not using debug auth. |
| `--api` | Must be local. Script refuses the public hostname. |

The importer now calls `injectQuestionPaperAssets()` so replace-import does **not** wipe the Part A/B/C PDFs. If you POST a manifest yourself, you must put `questionPaperAsset` on each part or re-attach PDFs after import.

### 5.3 Writing into the production database (owner-approved only)

The importer cannot call the public API. The safe pattern used for Jayden:

1. SSH tunnels from the Windows host:
   - Postgres `127.0.0.1:15433` → VPS `172.20.0.3:5432`
   - ClamAV `127.0.0.1:3310` → VPS ClamAV `172.20.0.6:3310`
2. Run the **updated** API on the host: `dotnet run --no-build --no-launch-profile` on `http://127.0.0.1:5198`.
3. Required env (do not commit):
   - `Bootstrap__SkipSchemaChanges=true` — **mandatory**. Never run Development+AutoMigrate against prod (EnsureCreated or Migrate will damage the schema).
   - Connection string points at `127.0.0.1:15433`.
   - `ASPNETCORE_ENVIRONMENT=Development` so `--dev-auth` and the ClamAV hostname remap work.
4. `ClamAvUploadScanner` in Development remaps host `clamav` → `127.0.0.1`. Do **not** change prod RuntimeSettings. Prod is fail-closed on ClamAV.
5. Copy prod DataProtection key XMLs into local `App_Data` (gitignored) if cookie/token unprotect is needed.
6. Import with `--dev-auth --debug-user-id auth_admin_local_001 --api http://127.0.0.1:5198`.
7. After publish, copy the five content-addressed PDFs onto the VPS volume `oetwebsite_oet_learner_storage` (`/var/opt/oet-learner/storage`) if the local API wrote files only on the Windows host.

Do **not** retry the live bootstrap password. Do not print connection strings, signing keys, or cookies.

### 5.4 Admin UI path (same contract)

`app/admin/content/reading` → paper → `ReadingAnswerSheetBuilder`.
Paste/generate must call `resolvePartALayout`. Publish still goes through `ValidatePaperAsync`.

There is a second, incompatible pathway stack (`lib/reading-pathway-api.ts` + HTML passages). Official exam papers do **not** go through it.

---

## 6. Validate / publish meaning

`GET /v1/admin/papers/{id}/reading/validate` returns:

```json
{ "isPublishReady": true, "issues": [], "counts": { "partACount": 20, "partBCount": 6, "partCCount": 16, "totalPoints": 42 } }
```

Hard errors include:

- Wrong 20/6/16 counts or points ≠ 42
- Last Part A block not starting at 15 or 16
- Middle/last blocks not opposite SA/SC
- Missing explanation / evidence / Published review state
- Missing primary QuestionPaper PDF per part (MIME check is **in-memory** after `Include`; do not put `MimeType.StartsWith` in an EF `Where` — it throws)
- Practice-only types, bad letters, empty answers

`POST /v1/admin/papers/{id}/publish` re-runs the same validator and sets `ContentPapers.Status = 4`. Re-publishing an already-published paper is allowed and is the way to prove a new validator against live data.

Learner endpoints never serialize answers, explanations, or synonyms.

---

## 7. Production live API notes (2026-08-18)

| Item | Value |
| --- | --- |
| Public API | `https://api.oetwithdrhesham.co.uk` |
| VPS | `root@185.252.233.186` |
| Router | nginx container `oet-api` proxies `http://learner-api-{slot}:8080` |
| Active slot after the layout fix | **green** (`/opt/oetwebapp/.deploy/active-slot.env`) |
| Code on `main` | squash `af049e9a997cca24e9abae1355250bf2ee165ff8` (PR #152) |
| Official deploy | push `main` → `.github/workflows/deploy.yml` → GHCR → `scripts/deploy/rollout-release.sh` |
| Current blocker | GitHub Actions billing / spending limit. Workflows fail before an image is built. |
| Emergency overlay | Linux `OetLearner.Api.dll` copied onto idle green, then router flipped. Snapshot tag: `oetwebsite-learner-api:part-a-laststart-af049e9a` |
| Proof the new validator is loaded | green DLL UTF-16 string `last block must start` / `15 or 16`. Old backup DLL does not contain it. |
| Rollback | point nginx `learner-api-green` back to `learner-api-blue` and `nginx -s reload`, **or** restore `/tmp/OetLearner.Api.dll.green.bak` and restart green |

**Do not** `docker compose up --force-recreate learner-api-green` while the overlay is the only copy of the new validator. That recreates green from the old GHCR tag and the 15/16 rule disappears from live.

**Do not** `docker compose build` / `dotnet publish` on the VPS. Windows host has no Docker. VPS source-build requires `ALLOW_VPS_SOURCE_BUILD=owner-approved-emergency`.

When billing is fixed, the next successful `main` deploy will build an image that already contains this validator. Until then the overlay + local image tag are the live copy.

### 7.1 Jayden papers already in prod (Published)

| Slug | Paper id |
| --- | --- |
| `jayden-book-01-bed-bugs` | `54b16f92f74e437b8da0005d87811f84` |
| `jayden-book-02-obstetric-ultrasound` | `4a280332b89c4b3b9422d714800c45d6` |
| `jayden-book-03-skin-lightening` | `ec22383df501484d867a3a8cbcf608fb` |
| `jayden-book-04-tmj-disorders` | `14b1be2a0f4d44e79a06af67eece6599` |
| `jayden-book-05-resveratrol` | `22941dadfe39482481720f5bc9070aef` |

PDFs live under `/var/opt/oet-learner/storage/uploads/published/...` (content-addressed). Same file is reused for A/B/C per paper.

Answer compare after import: **210 / 210** official answers matched. Every question has explanation + evidence.

---

## 8. Bugs already fixed — do not re-open

| Bug | Fix | File |
| --- | --- | --- |
| Last block assumed 15–20 only | `lastStart: 15 \| 16`, 8 layouts | `lib/reading-part-a-layout.ts`, `ReadingPartALayout.cs` |
| `EvidenceSentence` dropped on import | DTO + persist | `ReadingStructureService.cs` |
| Replace-import deleted QuestionPaper PDFs | `injectQuestionPaperAssets()` | `import-reading-manifests-local.mjs` |
| EF `MimeType.StartsWith(..., OrdinalIgnoreCase)` crash | `Include` then in-memory MIME filter | `ValidatePaperAsync` |
| Prod ClamAV host `clamav` fail-closed from Windows | Development remap to `127.0.0.1` | `ClamAvUploadScanner.cs` |
| Local API trying to migrate prod | `Bootstrap__SkipSchemaChanges` | `DatabaseBootstrapper.cs` |
| Live public API still on old validator | Green DLL overlay + router flip (PR #152 on main; GHA image not built) | VPS green slot |

---

## 9. Next book / next 5 PDFs — copy this checklist

```
[ ] Work only in the Copilot worktree, not D:\Projects\...
[ ] Confirm each PDF is a full 20/6/16 paper with a printed key
[ ] Detect Part A from booklet headings (1-7 vs 1-8, last 15 vs 16, SA/SC swap)
[ ] Build bundle with correctAnswerJson, evidenceSentence, reviewState=Published, texts: []
[ ] Three primary QuestionPaper assets (A/B/C); one PDF file may be reused
[ ] Offline validate-reading-manifest.ts → 42 points, 0 errors
[ ] Side-by-side official-key compare (42 answers × N papers)
[ ] If a printed C key is clearly pasted from another paper, stop and use passage evidence (see JB2)
[ ] Import via local API only; --dev-auth only on Development
[ ] If targeting prod DB: tunnels + SkipSchemaChanges + ClamAV tunnel; never AutoMigrate
[ ] Validate isPublishReady=true on the API that will serve learners
[ ] Publish; confirm Status=4; confirm PDFs exist on the VPS storage volume
[ ] Do not recreate green from the old image; do not retry bootstrap passwords
```

---

## 10. Code map (open these, not a repo-wide search)

| Concern | Path |
| --- | --- |
| Part A detector | `lib/reading-part-a-layout.ts` |
| Offline import contract | `lib/reading-manifest-contract.ts` |
| Public Part C numbers | `lib/reading-display-number.ts` |
| Admin TS client | `lib/reading-authoring-api.ts` |
| Official builder UI | `app/admin/content/reading/[paperId]/questions/ReadingAnswerSheetBuilder.tsx` |
| Full Reading Exam list | `app/reading/exam/page.tsx` |
| Book folders | `lib/reading-exam-categories.ts` |
| Exam player | `app/reading/paper/[paperId]/page.tsx` |
| Entities | `backend/src/OetLearner.Api/Domain/ReadingEntities.cs` |
| Import + validate | `backend/.../Services/Reading/ReadingStructureService.cs` |
| Layout (C#) | `backend/.../Services/Reading/ReadingPartALayout.cs` |
| Marking | `backend/.../Services/Reading/ReadingGradingService.cs` |
| Learner-safe DTO | `backend/.../Services/Reading/ReadingLearnerSafeProjection.cs` |
| Admin routes | `backend/.../Endpoints/ReadingAuthoringAdminEndpoints.cs` |
| Paper publish | `backend/.../Endpoints/ContentPapersAdminEndpoints.cs` + `Services/Content/ContentPaperService.cs` |
| Local importer | `scripts/admin/import-reading-manifests-local.mjs` |
| Offline CLI | `scripts/admin/validate-reading-manifest.ts` |
| Scoring | `lib/scoring.ts`, `docs/SCORING.md` |
| Upload/storage rules | `docs/CONTENT-UPLOAD-PLAN.md`, `AGENTS.md` Storage Persistence |
| This playbook | `docs/READING-MODULE-SAVE-AND-UPLOAD.md` |

---

## 11. Collection / git locality

This Copilot collection has two worktrees. Reading lives in **oet-project-web-app**:

- Worktree: `...\manwara575-star-fluffy-guacamole\oet-project-web-app`
- Branch used for the layout/import work: `manwara575-star-fluffy-guacamole` (merged via PR #152, remote branch may already be deleted)
- Never read or write `D:\Projects\OET with Dr Hesham\OET Project Web App`

Rename branches with the Copilot `rename_branch` tool, not `git branch -m`.

---

## 12. Learner Full Reading Exam is a paper list, not Mocks

The Reading hub card **Full Reading Exam** goes to `/reading/exam`. That page lists published `ContentPaper`s (`GET /v1/reading-papers/home`) grouped by the five official book folders from the complete Reading library:

1. Anna Hartford
2. Atlas Practice Series
3. Jayden Book
4. Nova Practice Series
5. VERY DIFFICULT READING EXAMS

Grouping uses `tagsCsv`, then slug, then title (`jayden-book`, `anna-hartford`, …). Empty series still render so the library shape stays visible. Unmatched published papers go under **Other papers**.

Starting a card calls `POST /v1/reading-papers/papers/{id}/attempts` (full Exam mode, 60 minutes) and opens `/reading/paper/{id}?attemptId=…`. Do **not** start a part-practice attempt from this page.

`/reading/mocks` still redirects to `/mocks?subtest=reading`. That is the mock-bundle surface. Prod currently has no published mock bundles; an empty Mocks page is expected and is **not** the Full Reading Exam.

Tag new books on import (`tagsCsv` must include the series slug, e.g. `reading,jayden-book,official-key`) so they land in the right folder.
