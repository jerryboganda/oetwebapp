# Reading upload — new-session agent handoff

Read `docs/READING-UPLOAD-ZERO-DEVIATION-CONTRACT.md` first, then this
file, then `docs/READING-MODULE-SAVE-AND-UPLOAD.md`. Follow them with
**no deviations**. Do **not** restart a repo-wide research loop. Do
**not** implement this in Doctor Marriage Bureau.

---

## Who you are and what you are doing

You are continuing **official OET Reading paper uploads on production** for
**oetwebapp**.

- Checkout: `E:\Projects\OET with Dr Hesham\Web App`
- Remote: `jerryboganda/oetwebapp` branch `main`
- Public API: `https://api.oetwithdrhesham.co.uk`
- VPS: `root@185.252.233.186` (SSH / inspect / temp-admin SQL only)
- Learner folders live at `/reading/exam` and `/reading/parts/a|b|c`

Owner standing orders:

1. Upload **on production**. Publish live. **No drafts.**
2. Use the **production public API**. Do not host API or Postgres locally.
3. Do not use the VPS for compute-heavy work. Builds/deploys = **GitHub Actions**.
   Make the repo **public** for the Actions run, then set it **private** again when the run finishes. Never leave it public.
4. Future papers must appear in the **same five book folders** as Reading materials.
5. Full Exam lists whole papers. Part A / B / C lists that part of every paper.
6. Crop **part-only** PDFs before attach. Never serve the combined booklet.

---

## Already live — do not re-import or mutate

| Series | Slugs | Status |
| --- | --- | --- |
| Jayden Book | `jayden-book-01-bed-bugs` … `jayden-book-05-resveratrol` | Published, 210/210 keys matched |
| Anna Hartford | `anna-hartford-01-cigarette-smoking-lung-cancer`, `anna-hartford-02-vision-impairment`, `anna-hartford-03-vaccines-immunisation` | Published 2026-08-20, 20/6/16, 42 pts |
| Atlas | `01` `02` `05` `10`–`17` `22`–`26` plus `atlas-practice-series-kaplan-asthma-ect` | Published. 20/6/16, 42 pts. Do not re-import. |
| Nova | `nova-practice-series-01-skin-cancer` … `06-glandular-fever`, `08`–`17`, `19-back-pain` (17 papers) | Published 2026-08-19. 20/6/16, 42 pts. Green keys after each part. Do not re-import. |
| Sample | `reading-sample-1` | Published |

AH paper ids:

- AH1 `8f78b84f54b649618ac7ecdd2ac2adba`
- AH2 `c923296171034cae88c497c4939ee94f`
- AH3 `44e58008f91e4a14828696ec06f4d86d`

AH layouts are all classic `1–7 / 8–14 / 15–20`. Printed keys are in the playbook §7.2.

---

Atlas paper ids (all Status 4). Layouts vary — see playbook §7.3:

- Atlas 01 `53d261affe2541359f5578b7e25d688d`
- Atlas 02 `dca80cd281f54e8cb0544e821141dbd6`
- Atlas 05 `3d4e36bbdf6747c79ff4f00f078fc5a5`
- Atlas 10 `e85f13d7cf7749c7aa42f3e279bc6d30`
- Atlas 11 `d8f4dd5f910645b59e49aaf612bf0e8c`
- Atlas 12 `253ae682ccbd431f91af03cda516b462`
- Atlas 13 `06ee37bacea740a5b56807a210b0399d`
- Atlas 14 `f45c6cc1ebb7462496822c20f8fa2616`
- Atlas 15 `18d925210bfd4d3aa574f1cd944a883f`
- Atlas 16 `f0fbc15366f545c9a98874941a081dec`
- Atlas 17 `d9fc1487a7304334805651b26d87e271`
- Atlas 22 `a6a3e6ad96be40bcac16f5f444e6bde3`
- Atlas 23 `bee8010b11c84ce18f9e66ee923c7d36`
- Atlas 24 `ae075207261b430189a88933f37afefd`
- Atlas 25 `69348a2fd0b24959a1f38ef833a20979`
- Atlas 26 `eb813cce22d841ae921c53a8c0691c0d`
- Kaplan `f4e5676c5d2a4eb2834b3a18360151d1`

Nova paper ids (all Status 4). Classic Part A 1–7 / 8–14 / 15–20. Green answers only, after each part; picture key pages were rotated:

- Nova 01 `c607e392ec6047229de3a6e7e54bb0ef`
- Nova 02 `46aa1406d5324caf9bfe0d3d97230932`
- Nova 03 `ba5ec527f38d490399348530e06c6c21`
- Nova 04 `458d0df326f7453cb68dc791427582d3`
- Nova 05 `b677bfa7ae1148d5b95af596a200bbb6`
- Nova 06 `14be93ac74ca47728b84ffb6a0ddb884`
- Nova 08 `280bc69664944c4c9b6de8aae728ab22`
- Nova 09 `5ca30a6670764f5d838042bc87d1afcf`
- Nova 10 `1384f80078084c8e91c84dddbd42b980`
- Nova 11 `864ad037e5334f71a693750fcf970db9`
- Nova 12 `e92b671bfc014626925af1d5ddb59c09`
- Nova 13 `4d40083ae25640d489c285c19775674c`
- Nova 14 `cfb5cf359cea42fbba2eadb50b7cadf7`
- Nova 15 `47bff86fc47d494ca829500192aa6c84`
- Nova 16 `6f80386316804630a7489d6b90eac5fc`
- Nova 17 `5b0364f3a32649f4b488ccd0b9abd211`
- Nova 19 `bf13b54206c343bbad825e2b95574550`

Not imported (do not invent): Test 7 no Part A key page; Test 18 Part C Q22 printed A–C only; Test 20 no B/C key page.

---

## What remains (folder order)

1. **Rest of Atlas Practice Series** — 01/02/05/10–17/22–26 and Kaplan are live. Remaining Desktop files still fail the contract:
   - Official Sample 3: Part A Q10 blank on the printed key; several B/C letters missing. Do not invent.
   - Official Sample 4: Part A keys complete; B/C key pages are two-paper overlay garbage.
   - Sample 18: printed key complete, lastStart=13 is now allowed, but B Q5–6 and several C pages are picture-only. Need a text dump from the owner.
   - Sample 19–21: B/C pages picture-only.
   - Sample 6–8: CamScanner, no extractable printed key.
   - Sample 9: no printed ANSWER KEY.
2. **Nova Practice Series** — 17 complete tests are live. Left unpublished: Tests 7, 18, 20 only. Do not invent those keys.
3. **VERY DIFFICULT READING EXAMS** — Part A letters exist; B/C answers are prose, not A/B/C. Skip unless a 20/6/16 letter key exists.

Do **not** re-import Jayden, Anna Hartford, live Atlas slugs, live Nova slugs, or `reading-sample-1`.

If this session has **no new contract-valid booklets**, do not invent papers. Ask the owner.

---

## First 10 minutes in a fresh session

1. Confirm cwd is `E:\Projects\OET with Dr Hesham\Web App` on `main`.
2. `git pull --ff-only origin main` if needed. Do not switch to DMB.
3. Read the zero-deviation contract + this file + the playbook. Do not
   re-derive the 20/6/16 contract.
4. Inventory owner-uploaded PDFs. Each must be a full exam + printed key.
5. Crop Part A / B / C PDFs. Drop answer-key pages. Duplicate shared
   boundary pages. Confirm three files and no `ANSWER KEY` text.
6. Extract text with `pdftotext` at
   `C:\Program Files\Git\mingw64\bin\pdftotext.exe`.
7. Build a JSON bundle. Offline validate (`ERROR COUNT 0`, 42 points).
   Import + publish on the public API. Confirm three distinct media ids.

---

## Manifest contract (non-negotiable)

- Official papers are **PDF-first**. `texts: []` on every part.
- One primary `QuestionPaper` PDF per part. **Never** reuse the combined A+B+C booklet. Crop to the part being attempted. Drop answer-key pages. Duplicate shared boundary pages into both parts.
- Field is `correctAnswerJson` (JSON-encoded string), not `correctAnswer`.
- Every question needs explanation, `evidenceSentence`, `reviewState=Published`.
- Counts must be 20 / 6 / 16 = **42 points**.
- Detect Part A from booklet headings. Do not hard-code 1–7 / 8–14 / 15–20.
- Never invent answers. Keys come from the printed key in the PDF.
- Exception already handled: JB2 Part C Q7–14 printed key is a paste of JB1.
  If a new book has a pasted key, stop and use passage evidence. Ask the owner.

`tagsCsv` **and** slug must include the series token:

| Series | `tagsCsv` example | slug prefix |
| --- | --- | --- |
| Atlas | `reading,atlas-practice-series,official-key` | `atlas-practice-series-01-…` |
| Nova | `reading,nova-practice-series,official-key` | `nova-practice-series-01-…` |
| Very Difficult | `reading,very-difficult-reading-exams,official-key` | `very-difficult-reading-exams-01-…` |

Folders match `tagsCsv` then slug then title (`lib/reading-exam-categories.ts`).
Wrong tags land the paper in **Other papers**.

Offline gate:

```powershell
node --experimental-strip-types --no-warnings=ExperimentalWarning `
  scripts/admin/validate-reading-manifest.ts --manifest path\to\bundle.json
```

Must print `ERROR COUNT 0` and 42 points before any write.

Keep new booklets and generated JSON **out of git** unless the owner asks.

---

## Production write path

Stock script `scripts/admin/import-reading-manifests-local.mjs` **refuses**
the public hostname. That is intentional.

Working public-API importer from the Anna Hartford session:

`C:\Users\Admin\.copilot\session-state\6f7a0022-0c5f-43ab-8ee7-b78856aca49a\files\anna-hartford-import\import-reading-prod.mjs`

Reuse or copy that file. It allows `api.oetwithdrhesham.co.uk` and sends:

```
X-OET-Device-Id: <UUID>
X-OET-Client-Platform: desktop
```

on **sign-in and every admin call**. Without those headers Production returns
`403 device_id_required`.

Do **not**:

- run a local API or local Postgres
- SSH-tunnel the DB and `dotnet run` on Windows
- pass `--dev-auth` / `X-Debug-*` at the public API (Production = 401)
- pass `--no-publish`
- retry `admin@oet-prep.dev` (one failed sign-in already recorded)
- retry any bootstrap / expert password

Temp-admin pattern that worked:

1. SSH to the VPS.
2. Insert a new `auth_identities` row with a bcrypt hash generated locally.
3. Grant `content:write` and `content:publish`.
4. Sign in on the public API with email/password + device headers.
5. Import + validate + publish.
6. Delete the identity, grants, and any local secret files.

Public-API uploads write PDFs to `/var/opt/oet-learner/storage` themselves.

---

## Learner UX you must preserve

| Hub card | Route | Behaviour |
| --- | --- | --- |
| Full Reading Exam | `/reading/exam` | Five book folders. Starting a card starts a **full** 60-minute exam. |
| Part A / B / C | `/reading/parts/a\|b\|c` | **Same folders.** Starting a card starts **only that part** of that paper. |

Shared component: `components/domain/reading/reading-exam-folder-browser.tsx`.

Do **not** redirect those routes to `/mocks`. `/reading/mocks` is a different
surface (mock bundles). An empty Mocks page is expected.

Live web-blue at handoff time: image from commit `6eb4f075`.
Last good Actions deploy: run `32114213139`.

If a UI change is not live, the web slot is stale. Redeploy with GitHub
Actions. Confirm the **web** image commit, not only the API slot.

---

## Deploy rules

- Push `main` → `.github/workflows/deploy.yml` → GHCR → VPS pull.
- If Actions dies in ~5 seconds with empty steps, the repo is likely **private**
  and runners cannot start. Owner previously made it temporarily public.
- Never `docker compose build`, `dotnet publish`, or `next build` on the VPS.
- Never `docker compose up --force-recreate learner-api-green` from an old
  GHCR tag (drops the 15/16 validator).
- Windows host: Node + Python. Prefer Actions for anything heavy.
- `pdftotext` is available; do not stand up Docker for extraction.

---

## Verify after every import

On the public API, for each new paper:

- `GET /v1/admin/papers/{id}/reading/validate` → `isPublishReady=true`, 20/6/16, 42
- `ContentPapers.Status = 4`
- PDFs exist under `/var/opt/oet-learner/storage`
- Signed-out `/reading/exam` and `/reading/parts/a` **307 to sign-in with
  `next=` those routes**, not `/mocks`
- After sign-in, the paper is in the correct folder on Full Exam **and** on
  each Part A/B/C list
- Learner structure has **three distinct** QuestionPaper media ids. Part A
  PDF starts with Part A and has no answer key. Same for B and C.

---

## Hard do-not list

- Do not re-import or mutate Jayden / Anna Hartford / Atlas 01, 05, 10.
  Do not re-attach their original combined booklets.
- Do not invent keys or HTML passages.
- Do not use practice-only question types or fuzzy marking.
- Do not work in `D:\Projects\OET with Dr Hesham\...`.
- Do not implement anything in DMB `Modernized-Platform/`.
- Do not print passwords, connection strings, cookies, or hashes.
- Do not commit secrets, PDFs, or generated answer bundles unless asked.

---

## Previous-session artifacts (not in git)

`C:\Users\Admin\.copilot\session-state\6f7a0022-0c5f-43ab-8ee7-b78856aca49a\files\`

- `anna-hartford-import\` — AH PDFs, printed keys, published JSON, prod importer
- `anna-hartford-extract\` — `pdftotext` output
- `READING-MODULE-UNDERSTANDING.md` — earlier locked understanding

Use them as examples. Do not re-publish AH from them.

Atlas 01/05/10 session (not in git):

`C:\Users\Admin\.copilot\session-state\a5cfda67-ead6-4944-98fe-e3b1fc27f2d9\files\`

- `atlas-import\` — published JSON, part-only PDFs, prod importer
- `atlas-extract\` — `pdftotext` + page maps

Do not re-publish Atlas 01/05/10 from them.

---

## When you finish a book

1. Update §7 of the playbook with the new slugs / paper ids / layouts / keys.
2. Update this handoff: move the series from “remains” to “already live”.
3. Keep `.github/agent-state.local.md` to a short next-step pointer.
4. Append one compact line to `PROGRESS.md`.
5. Ship-it: targeted check, commit docs, push `main`.
6. Stop. Owner verifies on live production.
