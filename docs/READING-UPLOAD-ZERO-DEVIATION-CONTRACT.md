# Reading upload — zero-deviation contract

**HARD GATE.** Read this before touching any Reading paper. If one item
cannot be satisfied, **stop**. Do not import, attach, or publish a partial
or “good enough” paper.

This file is the short law. Detail lives in
`docs/READING-MODULE-SAVE-AND-UPLOAD.md`. Live inventory / next book lives
in `docs/READING-UPLOAD-AGENT-HANDOFF.md`.

Product is **oetwebapp** (`E:\Projects\OET with Dr Hesham\Web App`,
`jerryboganda/oetwebapp`, `main`). Not Doctor Marriage Bureau.

---

## A. What a paper is

1. One official paper = **Part A 20 + Part B 6 + Part C 16 = 42 points**.
    Owner 2026-08-19 exception: published Atlas 09 Head injuries may run
    the **same 60-minute Full Exam** with C1 only (20/6/8). Do **not**
    invent C2. Scoring stays `/42`. `isPublishReady` stays false until
    C2 exists.
2. Official papers are **PDF-first**. Every part has `texts: []`.
   Do not OCR passages into HTML.
3. Answers come **only** from the printed key in the source PDF.
   Never invent, guess, or copy a key from another paper.
4. Field name is `correctAnswerJson` (JSON-encoded string). There is no
   `correctAnswer` field.
5. Every question needs `explanationMarkdown`, `evidenceSentence`, and
   `reviewState=Published`.
6. Detect Part A layout from booklet headings. Do not hard-code
   `1–7 / 8–14 / 15–20`. Matching may be 1–5, 1–6, 1–7, or 1–8; last
   block may start at 13, 14, 15, or 16.
7. If a printed Part C key is clearly pasted from another paper, **stop**.
   Use passage evidence and ask the owner. (JB2 C Q7–14 already handled.)

---

## B. PDFs the candidate sees — no combined booklets

Attach **one primary QuestionPaper PDF per Part A, B, and C**. The learner
player must show that part booklet on the left. Part B is **one collective
PDF for all 6 questions**; only the questions on the right are one-by-one.
Do not key the left pane to B1–B6 extract files when a Part B booklet exists.

8. The source file is usually **one** A+B+C booklet plus the answer key.
   The candidate must **never** see that combined file.
9. Before any attach/import, crop three files: `*-PartA.pdf`,
   `*-PartB.pdf`, `*-PartC.pdf`.
10. Map pages from headings: `Part A` / `Part B` / `Part C` /
    `END OF PART` / `ANSWER KEY` / `END OF KEY`.
11. **Drop every answer-key page.** Keys must not appear in the viewer.
12. If a page contains the end of one part and the start of the next,
    include that page in **both** part files.
13. Attach three **distinct** primary `QuestionPaper` media assets
    (`part` = `A` / `B` / `C`, `makePrimary: true`).
14. Fail the upload if any two parts share the same `mediaAssetId`.
15. Fail the upload if a part PDF contains `ANSWER KEY` or `END OF KEY`.
16. Full Exam already swaps the viewer by part. Distinct files are enough.
    Do not change player UI to “fix” a combined PDF.

Live AH / JB maps (1-based, keys omitted). Copy this method for new books:

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
| Atlas01 (21p) | 1–5 | 6–11 | 12–19 | 20–21 |
| Atlas05 (28p) | 1–8 | 9–15 | 16–23 | 24–28 |
| Atlas10 (21p) | 1–5 | 6–11 | 12–19 | 20–21 |
| Atlas11 (22p) | 1–6 | 7–12 | 13–20 | 21–22 |
| Atlas12 (18p) | 1–7 | 8–10 | 11–16 | 17–18 |
| Atlas13 (17p) | 1–6 | 7–9 | 10–15 | 16–17 |
| Atlas14 (23p) | 1–7 | 8–13 | 14–21 | 22–23 |
| Atlas23 (22p) | 1–6 | 7–12 | 13–20 | 21–22 |
| Atlas24 (22p) | 1–6 | 7–12 | 13–20 | 21–22 |
| Atlas25 (22p) | 1–7 | 8–13 | 14–20 | 21–22 |
| Atlas02 (23p) | 1–6 | 7–14 | **14–21** | 22–23 |
| Atlas15 (22p) | 1–6 | 7–12 | 13–20 | 21–22 |
| Atlas16 (17p) | 1–6 | 7–9 | 10–15 | 16–17 |
| Atlas17 (19p) | 1–6 | 7–9 | 10–17 | 18–19 |
| Atlas22 (23p) | 1–7 | 8–13 | 14–21 | 22–23 |
| Atlas26 (22p) | 1–7 | 8–13 | 14–20 | 21–22 |
| Kaplan (26p) | 1–8 | 9–14 | 15–24 | 25–26 |

Do **not** re-import or re-attach Jayden / Anna Hartford / live Atlas slugs
unless the owner asks. Their part files are already live.

---

## C. Folders and learner routes

17. `tagsCsv` **and** slug must include the series token or the paper
    lands in **Other papers**.
    - Atlas → `reading,atlas-practice-series,official-key` /
      `atlas-practice-series-01-…`
    - Nova → `reading,nova-practice-series,official-key` /
      `nova-practice-series-01-…`
    - Very Difficult → `reading,very-difficult-reading-exams,official-key` /
      `very-difficult-reading-exams-01-…`
    - Anna Hartford / Jayden already live: do not invent new tokens for them.
18. Folder matchers live in `lib/reading-exam-categories.ts`.
19. `/reading/exam` = same five book folders, whole papers, 60-minute start.
20. `/reading/parts/a|b|c` = **same folders**, that part of every paper.
21. Never send those routes to `/mocks`. Mocks are a different surface.

---

## D. Where and how you write

22. Write on **production**: `https://api.oetwithdrhesham.co.uk`.
23. Do not host API or Postgres locally. Do not SSH-tunnel and `dotnet run`.
24. Publish live. **No drafts.** Status must be `4` / `Published`.
25. Stock `scripts/admin/import-reading-manifests-local.mjs` refuses the
    public hostname. Use the session public-API importer (device headers).
26. Every public-API call needs:
    - `X-OET-Device-Id: <uuid>`
    - `X-OET-Client-Platform: desktop`
    Missing headers → `403 device_id_required`.
27. Never `--dev-auth` / `X-Debug-*` on Production (401).
28. Do not retry `admin@oet-prep.dev` or any bootstrap / expert password.
    Create a short-lived temp admin, then delete it.
29. Offline validate first:
    `validate-reading-manifest.ts` must print `ERROR COUNT 0` and 42 points.
30. Then `GET /v1/admin/papers/{id}/reading/validate` → `isPublishReady=true`.
31. Then `POST .../publish`. Confirm `ContentPapers.Status = 4`.

---

## E. Verify before you say done

32. Three distinct QuestionPaper media ids on the admin paper payload.
33. Learner `GET /v1/reading-papers/papers/{id}/structure` returns
    `questionPaperAssets` for A, B, and C.
34. Downloaded Part A PDF starts with Part A and has **no** answer key.
    Same check for B and C.
35. Paper appears in the correct folder on `/reading/exam` **and** on
    `/reading/parts/a`, `/b`, `/c`.
36. Signed-out those routes 307 to sign-in with `next=` those routes,
    not `/mocks`.

---

## F. Deploy / repo / safety

37. Heavy builds = **GitHub Actions** only. Never `docker compose build`,
    `dotnet publish`, or `next build` on the VPS (`185.252.233.186`).
38. For every Actions run: make `jerryboganda/oetwebapp` **public**, run,
    then set **private** again. Never leave it public.
39. Do not recreate `oet-api-green` from an old GHCR tag.
40. Do not commit secrets, source PDFs, or generated answer bundles
    unless the owner asks.
41. Keep new booklets out of git.

---

## G. Stop conditions

Stop and ask the owner if:

- the PDF is not a full 20/6/16 + printed key
- Part A/B/C page cuts are ambiguous after reading the headings
- a printed key is pasted / unreadable / missing
- folder tags would not match `lib/reading-exam-categories.ts`
- you would have to invent an answer, a passage, or a page range

Do **not** improvise around this list.
