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
| Sample | `reading-sample-1` | Published |

AH paper ids:

- AH1 `8f78b84f54b649618ac7ecdd2ac2adba`
- AH2 `c923296171034cae88c497c4939ee94f`
- AH3 `44e58008f91e4a14828696ec06f4d86d`

AH layouts are all classic `1–7 / 8–14 / 15–20`. Printed keys are in the playbook §7.2.

---

## What remains (folder order)

1. **Atlas Practice Series** ← start here when the owner uploads PDFs
2. **Nova Practice Series**
3. **VERY DIFFICULT READING EXAMS**

Anna Hartford and Jayden Book folders are done. Their Part A/B/C QuestionPaper PDFs were split and re-attached on 2026-08-18 (distinct media per part, answer-key pages removed). Do **not** re-import questions/keys. Do **not** re-attach the original combined booklet.

If this session has **no new booklets**, do not invent papers. Ask the owner to
upload the next series PDFs (same way they uploaded Anna Hartford).

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

- Do not re-import or mutate Jayden / Anna Hartford answers. Do not re-attach
  their original combined booklets.
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

---

## When you finish a book

1. Update §7 of the playbook with the new slugs / paper ids / layouts / keys.
2. Update this handoff: move the series from “remains” to “already live”.
3. Keep `.github/agent-state.local.md` to a short next-step pointer.
4. Append one compact line to `PROGRESS.md`.
5. Ship-it: targeted check, commit docs, push `main`.
6. Stop. Owner verifies on live production.
