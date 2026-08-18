# Reading Module — Save & Upload Playbook

> **Audience:** future coding agents / LLMs. Read this file first. Do **not**
> restart the research loop. This is the operational contract used to import
> official OET Reading papers (Jayden Book JB1–JB5, Anna Hartford AH1–AH3,
> Atlas 01/05/10, and later books).
>
> **Zero-deviation law:** `docs/READING-UPLOAD-ZERO-DEVIATION-CONTRACT.md`
> (read this before any import). **New session start:**
> `docs/READING-UPLOAD-AGENT-HANDOFF.md`
>
> **Status:** authoritative for save / import / validate / publish as of
> 2026-08-20. Older notes in `docs/reading-ingestion/READING-MODULE-KNOWLEDGE-BASE.md`
> and `docs/runbooks/local-reading-sample-import.md` are **stale** on Part A
> last-block, EvidenceSentence import, write path, and learner exam routes.
> This file wins on the contract. The handoff wins on the current live
> inventory and next book.

---

## 0. Start here (90-second brief)

Official Reading is **PDF-first, 42 points, 20/6/16**. You do **not** OCR
passages into HTML. You attach one primary QuestionPaper PDF per Part A/B/C,
then import a structured answer-sheet manifest, then validate, then publish.

| Step | Action | Gate |
| --- | --- | --- |
| 1 | Inventory the booklet + printed answer key | Complete exam = Part A 20 + Part B 6 + Part C 16. Keys live **in the PDF**. Split the booklet into **part-only** PDFs before attach. |
| 2 | Auto-detect Part A layout | Matching is 1–5 / 1–6 / 1–7 / 1–8. Last block starts at **13, 14, 15, or 16**. Middle/last swap SA vs SC. |
| 3 | Build a JSON bundle | Field is `correctAnswerJson` (JSON-encoded string). Include `evidenceSentence`. `texts: []`. |
| 4 | Offline dry-run | `ERROR COUNT 0`, 42 points, layout id matches the booklet headings. |
| 5 | Write import on **production public API** | Owner-approved. Stock importer is localhost-only; use the public-API copy with device headers. Publish live (status 4). |
| 6 | Backend validate + publish | `GET .../reading/validate` then `POST .../publish`. Do not leave drafts. |
| 7 | Compare answers to the printed key | Never invent keys. Exception: JB2 Part C Q7–14 printed key is a paste error. |
| 8 | Confirm learner folders | Full Exam and Part A/B/C must list the paper in the same book folder. |

Do **not**:

- Host API or Postgres locally. Do not tunnel-and-run `dotnet` on the Windows host (no SDK / no Docker).
- Use `--dev-auth` against Production. Debug headers work only when the API `IsDevelopment()`.
- Retry `admin@oet-prep.dev` or any other bootstrap / expert password. Seed admin `FailedSignInCount` is already 1.
- Recreate `oet-api-green` from an old GHCR image. That drops the 15/16 validator overlay.
- Build, `dotnet publish`, or Next compile on the VPS. Heavy work is GitHub Actions only.
- Leave `jerryboganda/oetwebapp` public after an Actions run. Make it public for the run, then private again. Never keep it permanently public.
- Send **Full Reading Exam** (`/reading/exam`) or Part A/B/C (`/reading/parts/a|b|c`) to `/mocks`. Those routes are folder-first paper lists.
- Assume last Part A block is always 15–20.
- Use `correctAnswer` instead of `correctAnswerJson`.
- Attach the combined A+B+C booklet (or the answer-key pages) as the Part A/B/C QuestionPaper. Crop first. Three distinct media ids.
- Put HTML passages in `texts` for official papers.
- Use practice-only types (`FillInBlank`, `ShortAnswerLabeled`, `MultipleChoiceFlexible`).
- Enable fuzzy / Levenshtein marking.
- Re-import or mutate Jayden JB1–JB5, Anna Hartford AH1–AH3, or Atlas 01/05/10 unless the owner asks.
- Touch Doctor Marriage Bureau (`Modernized-Platform/`). This work is **oetwebapp** only.
- Work in `D:\Projects\OET with Dr Hesham\...`. Canonical checkout is `E:\Projects\OET with Dr Hesham\Web App` on `main`.

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
- Learner `GET /v1/reading-papers/papers/{id}/structure` **must** include `paper.questionPaperAssets` for every primary Part A/B/C (and B1–B6 / C1–C2) QuestionPaper. That is independent of `allowPaperReadingMode`, which stays **false** (legacy paper-simulation UI). If the player says “No Part A document is attached”, the assets are missing from the structure payload or media access is denied — do **not** re-import the booklet until you have queried `ContentPaperAssets`.
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
        { "role": "QuestionPaper", "part": "A", "sourcePath": "JB1-PartA.pdf", "makePrimary": true },
        { "role": "QuestionPaper", "part": "B", "sourcePath": "JB1-PartB.pdf", "makePrimary": true },
        { "role": "QuestionPaper", "part": "C", "sourcePath": "JB1-PartC.pdf", "makePrimary": true }
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

**Never reuse the combined booklet for A/B/C.** The player serves the PDF for the part being attempted. A shared file shows Part B/C (and the answer key) during Part A. Crop first:

- Map pages from booklet headings (`Part A` / `Part B` / `Part C` / `END OF PART` / `ANSWER KEY`).
- Drop every answer-key page. Candidates must never see keys in the viewer.
- If a page contains the end of one part and the start of the next, include that page in **both** part files.
- Attach three **distinct** primary `QuestionPaper` media ids. `makePrimary: true` is required for each part.
- Full Exam already swaps the viewer by `part`. Distinct files are enough; no UI change.

Live AH/JB page maps (1-based, keys omitted):

| Paper | A | B | C | Drop |
| --- | --- | --- | --- | --- |
| AH1 (30p) | 1–7 | 8–16 | 17–27 | 28–30 |
| AH2 (24p) | 1–5 | 6–13 | 14–21 | 22–24 |
| AH3 (25p) | 1–7 | **7–13** | 14–22 | 23–25 |
| JB1 (16p) | 1–4 | **4–7** | 8–14 | 15–16 |
| JB2 (16p) | 1–4 | 5–8 | **8–14** | 15–16 |
| JB3 (16p) | 1–4 | **4–7** | 8–14 | 15–16 |
| JB4 (17p) | 1–4 | 5–8 | **8–15** | 16–17 |
| JB5 (16p) | 1–5 | **5–8** | **8–14** | 15–16 |

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

## 5. Save / upload pipeline

Stock importer = localhost-only dry-run / local-dev writes.
Owner-approved production writes = public API + device headers (section 5.3).

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

# Offline dry-run of the stock importer (no credentials).
# Stock script still refuses non-localhost. Do not use it for prod writes.

node scripts/admin/import-reading-manifests-local.mjs `
  --manifest path\to\bundle.json --dry-run
```

Flags:

| Flag | Meaning |
| --- | --- |
| `--dry-run` | Offline contract only. |
| `--replace-existing` | Update same slug. Unpublishes to draft first. |
| `--no-publish` | Stop after validate. **Do not use on official books** unless the owner asks for a draft. |
| `--dev-auth` | Development debug headers only. Needs `OET_DEBUG_USER_ID` / `--debug-user-id`. **Never against Production.** |
| `--email` / `--password` | Real sign-in. Required for the public API. |
| `--api` | Stock script: localhost only. Production writes: public hostname via the session importer. |

The importer now calls `injectQuestionPaperAssets()` so replace-import does **not** wipe the Part A/B/C PDFs. If you POST a manifest yourself, you must put `questionPaperAsset` on each part or re-attach PDFs after import.

### 5.3 Writing into production (owner-approved, current path)

Owner instruction (2026-08-20): **use the live public API**. Do not host API or database locally. Do not SSH-tunnel Postgres/ClamAV and run `dotnet` on Windows (host has no Docker / no .NET SDK).

Working public-API importer (Anna Hartford):

`C:\Users\Admin\.copilot\session-state\6f7a0022-0c5f-43ab-8ee7-b78856aca49a\files\anna-hartford-import\import-reading-prod.mjs`

That copy allows `api.oetwithdrhesham.co.uk` and sends device headers on every call.

Required headers on **sign-in and every admin call**:

```
X-OET-Device-Id: <any UUID>
X-OET-Client-Platform: desktop
```

Without them, Production returns `403 device_id_required` (TrustedDeviceRequired is on).

Auth:

1. Do **not** retry `admin@oet-prep.dev`. One failed sign-in already recorded.
2. Do **not** retry bootstrap / expert passwords.
3. Create a short-lived temp admin on the VPS (`auth_identities` + `admin_permission_grants` for `content:write` and `content:publish`), sign in with email/password + device headers, import, publish, then **delete the identity, grants, and any local secret files**.
4. `--dev-auth` / `X-Debug-*` only work when the API is Development. Production returns 401.

Publish live. Do not pass `--no-publish`. Confirm `ContentPapers.Status = 4` and `isPublishReady=true` with `counts.totalPoints=42`.

Public-API uploads land on the VPS volume `/var/opt/oet-learner/storage` themselves. You do **not** copy PDFs by hand after a public-API import.

**Legacy Jayden path (do not use unless the owner explicitly asks and the host has a .NET SDK):** SSH tunnels + local Development API + `Bootstrap__SkipSchemaChanges=true` + `--dev-auth`. Never AutoMigrate against prod. Never change prod ClamAV RuntimeSettings.

Do not print connection strings, signing keys, cookies, or temp-admin passwords.

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

## 7. Production live notes (2026-08-20)

| Item | Value |
| --- | --- |
| Product | **oetwebapp** (`E:\Projects\OET with Dr Hesham\Web App`). Not DMB. |
| Public API | `https://api.oetwithdrhesham.co.uk` |
| Member panel | `https://app.oetwithdrhesham.co.uk` (confirm live host before quoting) |
| VPS | `root@185.252.233.186` |
| Router | nginx container `oet-api` proxies `http://learner-api-{slot}:8080` |
| Live API slot | **green** |
| Live web slot | **web-blue** image `ghcr.io/jerryboganda/oetwebapp-web:6eb4f075...` |
| Folder-first exam UI | commit `6eb4f075` on `jerryboganda/oetwebapp` `main` |
| Official deploy | push `main` → GitHub Actions → GHCR → `scripts/deploy/rollout-release.sh` |
| Last successful folder-UI deploy | Actions run `32114213139` (web/API/migrate/deploy all succeeded) |
| Host limits | Windows: Node + Python 3.14. No local Docker. No local `dotnet` SDK. `pdftotext` at `C:\Program Files\Git\mingw64\bin\pdftotext.exe`. |
| Heavy compute | **GitHub Actions only.** Never Next/`dotnet` build on the VPS. |
| Actions instant-fail (~5s, empty steps) | Repo was private and runners could not start. Temporarily making the repo public unblocked this. |
| Trusted devices | Production requires `X-OET-Device-Id` + `X-OET-Client-Platform: desktop`. |
| Postgres / storage | `oet_learner` / `/var/opt/oet-learner/storage` |

**Do not** `docker compose up --force-recreate learner-api-green` from an old GHCR tag. That can drop the 15/16 validator.

**Do not** `docker compose build` / `dotnet publish` / `next build` on the VPS.

If a UI change is not live, check the **web** image digest / commit on the active web slot. API-only deploys do not update `/reading/exam`.

### 7.1 Jayden papers already in prod (Published)

| Slug | Paper id |
| --- | --- |
| `jayden-book-01-bed-bugs` | `54b16f92f74e437b8da0005d87811f84` |
| `jayden-book-02-obstetric-ultrasound` | `4a280332b89c4b3b9422d714800c45d6` |
| `jayden-book-03-skin-lightening` | `ec22383df501484d867a3a8cbcf608fb` |
| `jayden-book-04-tmj-disorders` | `14b1be2a0f4d44e79a06af67eece6599` |
| `jayden-book-05-resveratrol` | `22941dadfe39482481720f5bc9070aef` |

PDFs live under `/var/opt/oet-learner/storage/uploads/published/...` (content-addressed). Each part has its **own** QuestionPaper file (keys stripped). Do not re-attach the combined booklet.

Answer compare after import: **210 / 210** official answers matched. Every question has explanation + evidence.

### 7.2 Anna Hartford papers already in prod (Published)

| Slug | Paper id | Layout | Topic |
| --- | --- | --- | --- |
| `anna-hartford-01-cigarette-smoking-lung-cancer` | `8f78b84f54b649618ac7ecdd2ac2adba` | classic 1–7 / 8–14 / 15–20 | Cigarette Smoking / Lung Cancer |
| `anna-hartford-02-vision-impairment` | `c923296171034cae88c497c4939ee94f` | classic | Vision Impairment |
| `anna-hartford-03-vaccines-immunisation` | `44e58008f91e4a14828696ec06f4d86d` | classic | Vaccines and Immunisation |

Tags: `reading,anna-hartford,official-key`. Status 4. 20/6/16. 42 points. Part-only QuestionPaper PDFs (AH3 p7 is in both A and B).

AH3 Part B letters were reconstructed from option prose in the printed key: `1A 2B 3B 4C 5A 6A`. Do not invent other letters.

Also live: `reading-sample-1`. Do **not** re-import Jayden, Anna Hartford, or Atlas 01/05/10.

### 7.3 Atlas Practice Series papers already in prod (Published 2026-08-18)

| Slug | Paper id | Layout | Topic |
| --- | --- | --- | --- |
| `atlas-practice-series-01-fractures-dislocations-sprains` | `53d261affe2541359f5578b7e25d688d` | classic 1–7 / 8–14 / 15–20 | Fractures, dislocations and sprains |
| `atlas-practice-series-05-cellulitis` | `3d4e36bbdf6747c79ff4f00f078fc5a5` | 1–8 / 9–14 / 15–20 | Cellulitis |
| `atlas-practice-series-10-sedation` | `e85f13d7cf7749c7aa42f3e279bc6d30` | classic | Sedation |
| `atlas-practice-series-11-iron-deficiency` | `d8f4dd5f910645b59e49aaf612bf0e8c` | classic | Iron deficiency |
| `atlas-practice-series-12-opioid-dependence` | `253ae682ccbd431f91af03cda516b462` | classic | Opioid dependence |
| `atlas-practice-series-13-adhd` | `06ee37bacea740a5b56807a210b0399d` | classic | ADHD |
| `atlas-practice-series-14-evaluating-cognitive-function` | `f45c6cc1ebb7462496822c20f8fa2616` | classic | Evaluating cognitive function |
| `atlas-practice-series-23-tuberculosis` | `bee8010b11c84ce18f9e66ee923c7d36` | classic | Tuberculosis |
| `atlas-practice-series-24-deep-vein-thrombosis` | `ae075207261b430189a88933f37afefd` | classic | Deep vein thrombosis |
| `atlas-practice-series-25-schizophrenia` | `69348a2fd0b24959a1f38ef833a20979` | classic | Schizophrenia |
| `atlas-practice-series-02-paracetamol-overdose` | `dca80cd281f54e8cb0544e821141dbd6` | 1–7 / 8–13 / 14–20 | Paracetamol overdose |
| `atlas-practice-series-15-transfusion-reaction` | `18d925210bfd4d3aa574f1cd944a883f` | 1–7 / 8–13 / 14–20 | Transfusion reaction |
| `atlas-practice-series-16-assessing-pain` | `f0fbc15366f545c9a98874941a081dec` | 1–7 / 8–13 / 14–20 | Assessing pain |
| `atlas-practice-series-17-overweight-obese-children` | `d9fc1487a7304334805651b26d87e271` | 1–6 SC 7–13 / SA 14–20 | Overweight and obese children |
| `atlas-practice-series-22-hiv-testing` | `a6a3e6ad96be40bcac16f5f444e6bde3` | 1–7 / 8–13 / 14–20 | HIV testing |
| `atlas-practice-series-26-inguinal-hernia` | `eb813cce22d841ae921c53a8c0691c0d` | 1–6 / 7–14 SA / 15–20 SC | Inguinal hernia |
| `atlas-practice-series-kaplan-asthma-ect` | `f4e5676c5d2a4eb2834b3a18360151d1` | 1–6 SC 7–12 / SA 13–20 | Kaplan asthma and ECT |

Tags: `reading,atlas-practice-series,official-key`. Status 4. 20/6/16. 42 points. Part-only QuestionPaper PDFs.

Page maps (1-based, keys omitted): Atlas01 A1–5 B6–11 C12–19 drop 20–21 · Atlas02 A1–6 B7–14 C**14–21** drop 22–23 · Atlas05 A1–8 B9–15 C16–23 drop 24–28 · Atlas10 A1–5 B6–11 C12–19 drop 20–21 · Atlas11 A1–6 B7–12 C13–20 drop 21–22 · Atlas12 A1–7 B8–10 C11–16 drop 17–18 · Atlas13 A1–6 B7–9 C10–15 drop 16–17 · Atlas14 A1–7 B8–13 C14–21 drop 22–23 · Atlas15 A1–6 B7–12 C13–20 drop 21–22 · Atlas16 A1–6 B7–9 C10–15 drop 16–17 · Atlas17 A1–6 B7–9 C10–17 drop 18–19 · Atlas22 A1–7 B8–13 C14–21 drop 22–23 · Atlas23 A1–6 B7–12 C13–20 drop 21–22 · Atlas24 A1–6 B7–12 C13–20 drop 21–22 · Atlas25 A1–7 B8–13 C14–20 drop 21–22 · Atlas26 A1–7 B8–13 C14–20 drop 21–22 · Kaplan A1–8 B9–14 C15–24 drop 25–26.

Printed Atlas keys (authoritative, from the PDFs):

```
Atlas01 A: C D B A C B A | pillow | 0.2 mg | bony prominences | naloxone | crepe bandage | fentanyl | compartment syndrome | dislocation | sling | jewellery | throbbing | stockinette | 70
Atlas01 B: B C A B A B
Atlas01 C: B C C A D D A B D C A C C B A C

Atlas05 A: C D B A D C D C | topical | morbid obesity | 2.4 g | renal function | 500 mg | 1.2 g | aseptic aspiration | naproxen | hot tubs | deep vein thrombosis | facial cellulitis | chronic lymphoedema
Atlas05 B: C A B B B A
Atlas05 C: A D B C A A D B D B C B C B A B

Atlas10 A: A D A B C A B | benzodiazepines | minimal sedation | emergency intubation | fracture reduction | Phencyclidines | Remifentanil | 5mg | IV | cardiovascular function | verbal commands | epileptic seizures | carbon dioxide | reversal agent
Atlas10 B: C B B B A A
Atlas10 C: B B A B D C D A D A C D A B B D

Atlas11 A: B C A B D A D | <30 mcg/L | excess cow's milk intake | iron polymaltose | evaluate other causes | 1-2 weeks | ferric carboxymaltose | oral iron supplements | low in iron | adult doses of iron | endoscopy and colonoscopy | 3 times per week | in patients with comorbidities | tolerate oral iron therapy
Atlas11 B: C C B C A A
Atlas11 C: D C C B B A B A C B C B A D C A

Atlas12 A: B A C D A D B | pain intensity | buprenorphine-naloxone (sublingual) | 1-3 days | if pain is not managed with non-opioid medication | urine drug screening | alternative interests or pleasures | a single patch | permit | poorer outcomes | central features | treatment agreement | non-opioid combinations | three or more
Atlas12 B: C C A A B C
Atlas12 C: B D B B C D C C C B C D D A B B

Atlas13 A: C D B A A C D | 60 mg/day | multiple follow-up appointments | parent or partner interview | side effects of stimulant medication | at least 40-50% | more spontaneous and adventurous | atomoxetine | building blocks | longstanding / underlying | longer-acting formulations of methylphenidate | comorbid disorders | blood pressure | careful titration
Atlas13 B: C A A C B B
Atlas13 C: A C B D C C B A C B A B A C B C

Atlas14 A: C B A D A C B | major neurocognitive disorder | Alzheimer's disease | executive function | specialist referral | psychiatric or psychogeriatric referral | learning and memory | every 12 months | delirium | ability to live independently | 0 | cognitive | recognition of emotions | day-to-day
Atlas14 B: C A B B C C
Atlas14 C: B D A C C C B A D A C D D B C A

Atlas23 A: D B A B B C A | tuberculin | 10 | Mycobacterium tuberculosis | 20-30 months | pulmonary tuberculosis | adequate ventilation | drug-resistant TB | lungs | organ | swelling | X-ray | 20 | first-line
Atlas23 B: A C C C A A
Atlas23 C: A B B C B C D D A D D C B C B C

Atlas24 A: A B D A C D B | morphine | excessive bleeding | above 40 | throbbing cramp-like pain | leg | older anticoagulants | anticoagulants | elastic compression stockings | pulmonary embolism | blood flow | oestrogen | Fragmin | purpura
Atlas24 B: B A A A A C
Atlas24 C: B C C B D B D D D B B D C D D C

Atlas25 A: D B A A C C B | linkage of services | organising confusing experiences | feedback | outcome measures | evaluate frequently | 0.5-1.5 hours | olanzapine, risperidone and aripiprazole | patient cooperation | intranasal | reevaluate | emotional distress | simple examples | undue pressure
Atlas25 B: B C C C A C
Atlas25 C: B B C B B C B A C A A A B B C C

Atlas02 A: D C B D A B C | headache(s) | hepatitis C | ALF | renal failure | methionine | activated charcoal | speed of absorption | right upper quadrant | nausea | enzyme-inducing | 100 | 12 | supportive
Atlas02 B: C A A B A A
Atlas02 C: A C A D D B B D C B C A D A B B

Atlas15 A: B C C D C A B | 24 hours | an appropriate blood culture bottle | 5 minutes | Critical Care admission | systolic blood pressure | aspirin-containing products | opposite arm | Category 3 / life-threatening | salbutamol | puncture sites or wounds | mild hypersensitivity and non-haemolytic febrile | signs of haemoglobinuria | Category 2 / moderately severe
Atlas15 B: B A B C C A
Atlas15 C: B C B D A C B D D A D B A C C B

Atlas16 A: A B A D C C B | physiological and behavioural | 18 years | under-treating pain | parents and caregivers | on the clinical observation chart | once per shift | gold standard | physiological / clinical | face | cognitive | 0-2 | console | before and after
Atlas16 B: B A B C B C
Atlas16 C: B A D C C D C B A D B B C D B D

Atlas17 A: B A C A C D | diabetes | yearly | measured | growth chart | growth status | complication | long-term | age and gender | weight, length/height and BMI | poor intake | >80% | an action plan | poor self-esteem, social difficulties and depression | drugs and surgery
Atlas17 B: C A B A A C
Atlas17 C: B C D D C A B D C A C B B D A A

Atlas22 A: B D A C A D B | HCG | combination of factors | 20 minutes | lupus and rheumatoid arthritis | sexual and drug-taking history | 3 months after exposure | protein | transactional | finger | eligibility | immune system | reliability | condomless sex
Atlas22 B: B C A B A C
Atlas22 C: D A B B B D B C D B A C B A D D

Atlas26 A: A C D C B C | the groin | acetaminophen | old age | laparoscope | open hernia repair | laparoscopic surgery | men | Trendelenburg's position | fibre | nerves | palpable | pain | laparoscopic | pubic tubercle
Atlas26 B: A C A B B A
Atlas26 C: B A D B D B C A D B C B C A A B

Kaplan A: C B A D A B | arterial saturation | magnesium sulfate | allergies | life-threatening | reversibility testing | smoking | daily | every hour | in severe cases | children | warn ICU | peak expiratory flow rate | a whistling sound | a peak flow meter
Kaplan B: B C C A A B
Kaplan C: B D C A A C D B D A D B C A B C
```

Remaining Desktop Atlas files are **not** imported. Do not invent answers:

- Official Sample 3: A Q10 blank; some B/C letters missing.
- Official Sample 4: B/C key pages are OCR overlay garbage.
- Sample 18: printed key complete and lastStart=13 is live, but B Q5–6 and several C pages are image-only.
- Sample 19–21: B/C image-only.
- Sample 6–8 CamScanner; Sample 9 no printed key.
- Nova: 20 tests, no printed key.
- Very Difficult: B/C answers are prose, not letters.

Printed AH keys (authoritative, from the PDFs):

```
AH1 A: D A C B A B C | around 40% | 131 848 | carbon dioxide | worsens | early stage lung cancer | heart disease and stroke | Victoria | alveoli | breathe | lung cancer | cilia | respiratory illnesses | quit
AH1 B: C A B C B A
AH1 C: C B A B C A B A B D A A A B D B

AH2 A: C D B A B D A | 246 million | Females | Naturally | Introduce others | Blindness | 6/6 | Uncorrected refractive errors | Visual fields | Avoid | Recognise you | Prevented or cured | Low-income settings | Age group
AH2 B: B C A C C A
AH2 C: D A B A B B B C C A C B A C A B

AH3 A: B D A A B C D | Hepatitis B | Strengthen | 1932 | Children under 3 years of age | Two | 12 months | Recognise and clear out | Schedule | Mumps | Exposure | Three years (of age) | Exercise strengthens | Small fraction
AH3 B: A B B C A A
AH3 C: A C B A C C C A D A A D C C B C
```

---

## 8. Bugs already fixed — do not re-open

| Bug | Fix | File |
| --- | --- | --- |
| Last block assumed 15–20 only | `lastStart: 13 \| 14 \| 15 \| 16` | `lib/reading-part-a-layout.ts`, `ReadingPartALayout.cs` |
| `EvidenceSentence` dropped on import | DTO + persist | `ReadingStructureService.cs` |
| Replace-import deleted QuestionPaper PDFs | `injectQuestionPaperAssets()` | `import-reading-manifests-local.mjs` |
| EF `MimeType.StartsWith(..., OrdinalIgnoreCase)` crash | `Include` then in-memory MIME filter | `ValidatePaperAsync` |
| Prod ClamAV host `clamav` fail-closed from Windows | Development remap to `127.0.0.1` | `ClamAvUploadScanner.cs` |
| Local API trying to migrate prod | `Bootstrap__SkipSchemaChanges` | `DatabaseBootstrapper.cs` |
| Live public API still on old validator | Green DLL overlay + router flip (PR #152 on main) | VPS green slot |
| Full Exam / Part A–C redirected to mocks | Folder-first `ReadingExamFolderBrowser`; do not send those routes to `/mocks` | `app/reading/exam/page.tsx`, `app/reading/parts/[part]/page.tsx` |
| UI change committed but live still old | Actions failed while repo was private (~5s empty run). Redeploy via Actions after repo is runnable. Confirm **web** slot commit, not only API. | `.github/workflows/deploy.yml` |
| Public API sign-in 403 | Send `X-OET-Device-Id` + `X-OET-Client-Platform: desktop` on every call | TrustedDeviceRequired |
| Seed admin lockout risk | Do not retry `admin@oet-prep.dev` | `auth_identities` |
| Combined booklet shown for every part | Split A/B/C PDFs; drop keys; shared pages duplicated | live `ContentPaperAssets` 2026-08-18 |

---

## 9. Next book / next PDFs — copy this checklist

```
[ ] Work in E:\Projects\OET with Dr Hesham\Web App on main. Not DMB. Not D:\...
[ ] Confirm each PDF is a full 20/6/16 paper with a printed key
[ ] Detect Part A from booklet headings (matching 5/6/7/8, lastStart 13/14/15/16, SA/SC swap)
[ ] Build bundle with correctAnswerJson, evidenceSentence, reviewState=Published, texts: []
[ ] tagsCsv includes the series slug (atlas-practice-series / nova-practice-series / very-difficult-reading-exams)
[ ] slug includes the same series token so folders match even if tags are thin
[ ] Three primary QuestionPaper assets (A/B/C) from **part-only** PDFs. Never attach the combined booklet. Never include answer-key pages. Shared boundary pages go in both parts.
[ ] Offline validate-reading-manifest.ts → 42 points, 0 errors
[ ] Side-by-side official-key compare (42 answers × N papers)
[ ] If a printed C key is clearly pasted from another paper, stop and use passage evidence (see JB2)
[ ] Import via public API + device headers. Never --dev-auth on Production.
[ ] Do not retry seed/bootstrap passwords. Use a short-lived temp admin; delete it after.
[ ] Publish live (no --no-publish). Confirm Status=4 and isPublishReady=true
[ ] Confirm PDFs exist on /var/opt/oet-learner/storage
[ ] Confirm the paper appears in the same book folder on /reading/exam and /reading/parts/a|b|c
[ ] Do not recreate green from an old image. Do not build on the VPS. Do not reimport JB/AH/Atlas 01-05-10.
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
| Part A/B/C lists | `app/reading/parts/[part]/page.tsx` |
| Shared folder UI | `components/domain/reading/reading-exam-folder-browser.tsx` |
| Book folders | `lib/reading-exam-categories.ts` |
| Exam player | `app/reading/paper/[paperId]/page.tsx` |
| New-session handoff | `docs/READING-UPLOAD-AGENT-HANDOFF.md` |
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

Canonical checkout (in-place `main`):

- `E:\Projects\OET with Dr Hesham\Web App`
- Remote: `jerryboganda/oetwebapp`
- Branch: `main`

Never read or write `D:\Projects\OET with Dr Hesham\OET Project Web App`.
Never implement Reading uploads in Doctor Marriage Bureau (`Modernized-Platform/`).

Ship-it for oetwebapp: targeted check, commit, push `main`. Include
`Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>`.
Do not force-push. Do not create extra branches unless the owner asks.

Rename branches with the Copilot `rename_branch` tool, not `git branch -m`.

---

## 12. Learner Full Exam and Part A/B/C are the same book folders

The Reading hub cards:

| Hub card | Route | What it lists | Start action |
| --- | --- | --- | --- |
| Full Reading Exam | `/reading/exam` | Every published paper, grouped in the five book folders | Full 60-minute exam attempt |
| Part A / B / C | `/reading/parts/a\|b\|c` | The **same** folders, but each card starts only that part | `startReadingPartPracticeAttempt` |

Shared UI: `ReadingExamFolderBrowser`. Both surfaces use `GET /v1/reading-papers/home`.

Folders (screenshot order, do not invent extra series):

1. Anna Hartford — matchers `anna-hartford`, `anna hartford`
2. Atlas Practice Series — `atlas-practice-series`, `atlas practice series`, `atlas-practice`
3. Jayden Book — `jayden-book`, `jayden book`
4. Nova Practice Series — `nova-practice-series`, `nova practice series`, `nova-practice`
5. VERY DIFFICULT READING EXAMS — `very-difficult-reading-exams`, `very difficult reading exams`, `very-difficult`

Grouping uses `tagsCsv`, then slug, then title. Empty series still render. Unmatched published papers go under **Other papers**.

Future papers land automatically if tagged **and** slugged with the series token.

- Full Exam start: `POST /v1/reading-papers/papers/{id}/attempts` then `/reading/paper/{id}?attemptId=…`
- Part start: part-practice attempt for A or B or C only
- Do **not** send either surface to `/mocks`

`/reading/mocks` still redirects to `/mocks?subtest=reading`. That is the mock-bundle surface. An empty Mocks page is expected and is **not** the Full Reading Exam.

Tag new books on import, for example:

- `reading,atlas-practice-series,official-key`
- `reading,nova-practice-series,official-key`
- `reading,very-difficult-reading-exams,official-key`
