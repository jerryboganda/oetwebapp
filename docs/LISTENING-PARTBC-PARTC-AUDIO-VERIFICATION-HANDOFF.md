# Handoff — Listening Part B/C headings, Separate Part C, and Atlas 8 audio fixes on production

**Release commits (all on `origin/main`, all deployed, in order):**
- `ac499562b` — restore Part B/C printed questions (mechanism) + unblock separate Part C (first fix)
- `2cdf9b86d` — this handoff doc, first version
- `6f49876b8` — Part B/C recovery: forced PDF re-extraction fallback + fleet-wide sweep endpoint
- `dc7645d22`-adjacent `3284ad0d9` (landed as `f3edbc9cd` on main) — PdfPig now reconstructs real page
  layout from word boxes instead of `page.Text`'s unstructured glyph run (this is what made recovery
  actually work at scale, not just in unit tests)
- `325ca4523` (landed as `3ff32d633`) — fixed silent option-text corruption on indefinite-article
  options ("A blended course...") and a slice-boundary bug on third-party paper layouts
- `fdee0b505` (landed as `f3c0ff400`) — Part C audio-sibling detection is now URL-based, not
  key-based, closing a second real bug found by live-testing Atlas Sample Test 8 as the actual
  student account

**Bug report being closed:** "Critical Listening Bug Report", 28 Aug 2026 (Atlas + Nova; Part B/C
headings, Separate Part C C1→C2, Atlas Sample 8 audio)

Production app: `https://app.oetwithdrhesham.co.uk` · API: `https://api.oetwithdrhesham.co.uk`

---

## 0. Status — DEPLOYED, DATA APPLIED, VERIFIED AS THE REAL STUDENT

Every commit above is live. The Part B/C recovery sweep has **already been run for real** against
production (not a dry run) using the admin account. This is not "the mechanism shipped, go run it" —
the data is fixed.

**Verified end-to-end with the actual credentials, not just server-side reports:**

- Signed in as `mindreader420123@gmail.com` (real learner), started a real Part B practice attempt
  on Nova Practice Series Listening Test 12, and read back the full session payload: **every one of
  Q25–Q42 shows its own distinct, correct, source-verbatim question and three distinct options.**
  No shared heading anywhere, no blank text, no `See PDF`. Q30 specifically — the item this whole bug
  report started from — reads *"You hear part of a training for GPs. What kind of course is being
  described?"* with options *"A blended course for GPs interested in dermatology" / "A traditional
  face-to-face general education course for GPs" / "A blended, general education course for GPs"*,
  exactly as printed on the source paper.
- Signed in as the same student, started Part C on Atlas Sample Test 8, and read back its session:
  confirmed the exact broken data shape (`audioUrlByPart.C1` and `.C2` both resolving to the same
  media asset) that the URL-based sibling-detection fix (`f3c0ff400`) now handles correctly.

**Sweep numbers (fleet-wide, run for real, `dryRun=false`), 36 published Listening papers:**

| Metric | Value |
|---|---|
| Papers scanned | 36 |
| Papers fully clean (every item shows a question) | 16 |
| Items recovered and written | **264** |
| Items still needing manual entry | 54 |
| Papers needing manual entry | 3 — all three are the same root cause, see below |

**The 54 remaining items are a hard floor, not a bug.** All 54 are on three papers — Atlas Sample
Test 6, 7, and 8's *original* question-paper PDF — that were produced by scanning printed pages with
CamScanner: the PDF has **no text layer at all** for the question content, only a cover page. No
text-based parser, however good, can read pixels. Confirmed directly:
```
$ pdftotext "Atlas Sample Test 8 question paper.pdf" -
...
Page 91  Scanned by CamScanner
Page 92  Scanned by CamScanner
...
```
Closing this needs actual OCR (image → text), which is a categorically different task from what
shipped here. The codebase already has an AI OCR pipeline built for exactly this
(`ListeningPartBCExtractionService`, Mistral OCR + Claude) — see section 7.

**Re-confirm the deploy and the applied data at any time:**

```bash
gh run list --workflow "Build & Deploy (web + API)" --limit 1 --json databaseId,status,conclusion,headSha
curl -s https://api.oetwithdrhesham.co.uk/health/ready
curl -s https://app.oetwithdrhesham.co.uk/sw.js | grep CACHE_VERSION   # expect oet-v5

TOKEN=$(curl -s -X POST https://api.oetwithdrhesham.co.uk/v1/auth/sign-in \
  -H 'Content-Type: application/json' \
  -d '{"email":"ADMIN_EMAIL","password":"ADMIN_PASSWORD","rememberMe":false}' \
  | python -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])')
curl -s -H "Authorization: Bearer $TOKEN" \
  "https://api.oetwithdrhesham.co.uk/v1/admin/listening/part-bc/source-audit?publishedOnly=true" \
  | python -c 'import json,sys; d=json.load(sys.stdin); print(d["totalRecoverable"], "recoverable,", d["totalUnrecoverable"], "unrecoverable")'
# Expect: 0 recoverable (everything that could be written already was), 54 unrecoverable
```

> **Project rule for any deploy on this repo:** GitHub Actions runs require the repo to be
> **public** for the duration of the run. Flip it public, run the workflow, then flip it back to
> private immediately. **Never leave it public.**
> (`gh repo edit jerryboganda/oetwebapp --visibility public|private --accept-visibility-change-consequences`,
> confirm with `gh repo view --json visibility` before finishing.) Expect the repo's Actions
> concurrency group to cancel an in-flight run if a *different* push lands on `main` while yours is
> running — re-check `gh run list` for the newest run against the SHA you actually pushed before
> concluding a deploy failed.

---

## 0b. What I could not personally verify — needs one real click-through

Everything above was verified via the real learner/admin **API** as the actual accounts, which
proves the *data* the frontend receives is correct. I do not have interactive browser automation in
this session, so I could not click through the actual React player UI pixel-by-pixel. Specifically
still open:

- **Visually confirm** Separate Part C on Atlas Sample Test 8 advances from C1 to C2 in the browser
  (the code fix is unit-tested against this exact production data shape and deployed, but has not
  been watched happen in a live tab).
- **Listen** to Atlas Sample Test 8's Part A/B/C/Full audio to confirm it's the *correct* recording
  content-wise (verified only by SHA/asset metadata in an earlier session, never by ear).

Sections 5 and 6 below are exactly the checklist for this. If you have real browser automation
(agent-browser, Playwright, or a human), this is the one thing actually left to close.

---

## 1. What changed, and why

### Issue 1 — Part B/C items show the same heading (in fact: no heading)

**Root cause, confirmed in the migration source.** Migration
`20261128000000_FixAllListeningPartBAndCStems` rewrote every Listening question whose stem was a
sentinel (`See PDF`, `CPDF`, `PDF`, `View PDF`, empty) to **one hardcoded string**:

- Q25–Q30 → `What does the speaker identify as the main clinical priority?`
- Q31–Q42 → `What is the speaker's main point in this extract?`

The `UPDATE` was scoped by question number only — **no paper, series, or status predicate** — so
it hit every Listening paper in the database. That is exactly the repeated heading in the tester's
Q25 and Q28 screenshots. It was never a heading-to-question *mapping* bug; the real wording was
overwritten in the database.

The follow-up migration `20261129000000_RestoreListeningPartBCSourceStems` hand-restored one Nova
paper (`77114cbc020347858619a88928ed0e32`) and set `Stem = ''` for every other paper's Q25–Q42. So
**today's live symptom is a blank heading above three live options**, not the repeated heading.
(The backend also blanks the generic heading at read time, so even an un-migrated row renders
empty.) Note the two migrations disagree on that Nova paper's Q30 wording — treat that paper as
unverified too.

**What the release adds.** The printed wording cannot be retyped from memory or generated — it is
exam content that has to match the source paper item by item. It *is* recoverable: every paper's
question-paper PDF text is already cached in `ContentPapers.ExtractedTextJson`, keyed by asset id
(written by `ContentTextExtractionService`).

- `ListeningPartBCSourceParser` — deterministic Q25–Q42 recovery of stem + options A/B/C.
  Precision-first: it refuses interleaved extraction, option prose containing a standalone letter
  ("Hepatitis B", "vitamin C"), and the poisoned generic heading itself. Anything it cannot
  attribute safely is **reported**, never guessed.
- `ListeningPartBCSourceRecoveryService` + admin routes. Fills only unreadable stems/options,
  never one a human authored, and never touches an answer key, option letter, or numbering. It
  runs outside the authoring attempts-guard on purpose — every affected paper is published with
  live learner attempts, which the normal authoring routes refuse to write.
- Admin panel on the **Listening → Questions** tab drives it and lists items needing manual entry.
- `ListeningBackfillService` no longer re-projects the generic heading from authored JSON.
- `BCQuestionRenderer` now states the question is unavailable instead of rendering an empty box.

> **The code ships the mechanism, not the repaired data.** Section 3 below is a required manual
> step per paper.

### Issue 2 — Separate Part C practice never opened C2

**Root cause.** `ListeningLearnerService.ApplyQuestionScope` nulled the combined paper MP3 for
every scoped part-practice attempt. A paper whose audio exists only as one combined file therefore
reached the player with the C1/C2 **cue windows but no audio URL at all**. The advance gate
(`canOpenReviewWindow` → `audioGateSatisfied`) could never be satisfied, and
`confirmNextFromAudio()` returned silently — the "Lock & continue" modal closed and nothing
happened. The full exam was unaffected because `ApplyQuestionScope` is only reached for part
practice.

A second variant affected papers that *do* have a single parent-level `C` upload (which is Atlas
Sample 8 after its audio replacement): both C1 and C2 resolved the same file, so C1 would only
hand over after the whole ~17-minute Part C had played, and C2 then restarted it at 0:00.

**Fixes:** keep the combined file for a scoped attempt when a scoped section has no per-section
audio of its own; treat a parent-key file as spanning sibling sections so the boundary is the
authored cue; ignore cue offsets that do not fit the loaded source; never let a section with no
audio block navigation; and surface a reason instead of a silent return.

**Part A practice (A1→A2) had the identical defect** and is fixed by the same change — worth
testing even though the bug report did not mention it.

### Issue 3 — Atlas Sample 8 audio

The replacement itself was already done and deployed in an earlier session (paper
`8625c6f593d440c1aeaf7a87ab34d4c0`, four primary Audio assets — Full, Part A, Part B, Part C —
sourced from the owner's `9- Sample Test 8` folder; the old 11 audio rows were removed). **That
was verified by admin-API SHA/size checks only — never by authenticated learner playback.** That
gap is what section 5 closes.

This release hardens the pipeline around it:

- Chunked uploads recorded no duration, and the Listening publish gate treats a primary audio
  asset without one as an **error** (`listening_audio_duration`) — so any audio replacement left
  the paper unpublishable. The admin uploader now measures the clip and the attach call stamps it.
- Primary-asset demotion compared the part label case-sensitively, so attaching `c1` left `C1`
  primary and the old file could still win. Now case/whitespace safe.
- The service worker cached `/v1/media/{id}/content` and `/v1/listening/audio/*`, so a replaced
  recording could replay from Cache Storage. Both now bypass the SW, and `CACHE_VERSION` is bumped
  to `oet-v5` to evict what is already cached.

Reference durations for Atlas 8 (measured from the owner's source files): Part A `644.76s`,
Part B `510.30s`, Part C `1051.58s`, Full `2206.72s`. The full file is the three parts
concatenated (sum `2206.64s`), so in the full exam Part B starts at ~`644.8s` and Part C at
~`1155.1s`.

---

## 2. Credentials and access

Admin (`admin@oet-prep.dev`) and a real learner (`mindreader420123@gmail.com`) accounts were both
used in this session — contrary to what an earlier draft of this doc said, the seeded admin account
**is** usable on production; sign in through the normal `/v1/auth/sign-in` route with a JSON body
(`{"email":...,"password":...,"rememberMe":false}`), same as any account. Do **not** use
`--dev-auth` against production.

---

## 3. The Part B/C recovery sweep — ALREADY RUN, use this section to re-run or extend it

**This has already been executed for real** (section 0 has the numbers). Use this section if you
need to re-run it — e.g. after uploading a corrected question-paper PDF for one of the 3 remaining
papers — not as a first-time setup step. **Use the fleet sweep**, not the per-paper loop — looping
over ~36 papers by hand is exactly the manual pattern that leaves a gap.

```bash
# 1. Dry run first — writes nothing, shows exactly what would change
POST /v1/admin/listening/part-bc/recover-source?publishedOnly=true&dryRun=true

# 2. Apply across every published Listening paper in one pass
POST /v1/admin/listening/part-bc/recover-source?publishedOnly=true&dryRun=false

# Read-only audit at any time
GET  /v1/admin/listening/part-bc/source-audit?publishedOnly=true
```

The sweep returns totals plus a per-paper breakdown:

| Field | Meaning |
|---|---|
| `papersScanned` | Listening papers examined |
| `totalRecovered` | Items whose printed question was restored from source |
| `totalStillUnrecoverable` | Items that must be typed in by hand — **drive this to 0** |
| `papersWithoutSourceText` | Papers with no usable question-paper text even after re-extraction |
| `papersFullyClean` | Papers where every Part B/C item now shows a question |
| `failures[]` | Papers that threw; one bad paper never aborts the sweep |

**Per-paper alternative (admin UI).** Admin → Content → Listening → *paper* → **Questions** tab →
**Part B** / **Part C**. A warning panel offers **Re-check** and **Restore N from source**, and
lists the items needing manual entry. Same engine, one paper at a time.

**Per-paper API:** `GET|POST /v1/admin/papers/{paperId}/listening/part-bc/{source-audit,recover-source}`.

### What each per-item outcome means

| Result | Meaning | Action |
|---|---|---|
| `recovered` | Stem/options restored verbatim from that paper's own question paper | Spot-check against the source PDF |
| `already-usable` | The item already showed a real question | Nothing |
| `unrecoverable` + reason | The source text cannot attribute it safely | **Type it in by hand** from the printed paper |
| `sourceTextAvailable: false` | No usable question-paper text even after a forced re-extraction | Upload the question paper on the **PDFs** tab, then re-run the sweep |

`unrecoverable` is a correct, deliberate outcome — the parser reports rather than guesses. The
usual causes are a `SAMPLE` watermark interleaving two printed items, and option prose containing a
standalone `A`/`B`/`C` ("Hepatitis B", "vitamin C"). **Never** invent wording to clear one.

### Why a first attempt might have found nothing

The PDF text cache stores an empty string when extraction fails, and the normal extraction pass
skips any asset that already has an entry — so a paper ingested before the PDF engine was
configured would stay blank forever. Recovery now **forces one re-extraction** when it finds no
usable source text, and retries before giving up. It only rewrites cache entries that are unusable;
a good extraction is never discarded.

## 4. Verification — Issue 1, Part B/C headings (learner)

**Already verified via the live learner API as the real student account** (section 0) for Nova
Practice Series Listening Test 12, including Q30 — the exact item the original bug report screenshot
was of — and it now shows its own correct question. This section is the checklist for extending
that same check to every other paper visually in a browser, and for the 3 papers OCR has not
reached yet.

Sign in as the **student**. For **each** of Atlas and Nova, for **Part B** and **Part C**:

1. Go to `/listening` → open the paper (or `/listening/practice/B`, `/listening/practice/C`).
2. Walk every question: Part B = Q25–Q30, Part C = Q31–Q42.

**Pass criteria (from the owner's acceptance checklist):**

- [ ] Atlas Part B — each of Q25–Q30 shows its own heading, matching the source question paper item by item.
- [ ] Atlas Part C — each of Q31–Q42 shows its own heading, matching the source.
- [ ] Nova Part B — same.
- [ ] Nova Part C — same.
- [ ] **No two different questions share a heading**, unless the source paper genuinely repeats it.
- [ ] The string `What does the speaker identify as the main clinical priority?` appears **nowhere**, unless it is genuinely printed on that item in the source.
- [ ] The string `What is the speaker's main point in this extract?` appears nowhere, same caveat.
- [ ] No item shows `See PDF`, `CPDF`, or an empty heading box.

**Expected exceptions — Atlas Sample Test 6, 7, and 8 will still show the amber notice** *"The
printed question for Qn is not available on this paper yet…"* for all 18 Part B/C items each (54
total). This is confirmed, not a regression: their question-paper PDFs are CamScanner image scans
with zero text layer for the question content (section 0 has the `pdftotext` proof). Do not treat
this as the recovery sweep failing — it is the sweep correctly refusing to guess at a paper it
cannot read at all. Closing it needs OCR, not another sweep; see section 7.

Every other paper should show a real question everywhere. If one doesn't, that's a genuine
regression — record paper + number and stop.

Screenshot Q25 and Q28 on Atlas Part B specifically — those are the two the tester reported.

---

## 5. Verification — Issue 2, Separate Part C C1→C2 (learner)

This is the flow that was dead, and it turned out to be dead for TWO distinct reasons found across
two rounds of fixing — the second only surfaced by starting a real attempt as the real student on
Atlas Sample Test 8 and reading its session payload, which showed C1 and C2 both resolving to the
same audio file even though each had its own explicit database entry. Both root causes are fixed
and deployed; **this section is the one piece of this whole handoff that has not been watched
happen in an actual browser tab** (see section 0b) — it is unit-tested against Atlas 8's exact data
shape and confirmed correct by code inspection, but not clicked through live. Prioritize this over
sections 4 and 6 if you only have time for one.

Test on **both** Atlas and Nova, and **specifically include Atlas Sample Test 8**.

1. `/listening/practice/C` → pick the paper → **Start Part C practice**.
2. Answer/skip through the C1 questions.
3. Trigger the boundary: either let the C1 audio reach its cue, or navigate to the last question
   and press **Next Section** → **Lock & continue**.

**Pass criteria:**

- [ ] Atlas — C1 → Continue → **C2 opens**, every time.
- [ ] Nova — C1 → Continue → C2 opens, every time.
- [ ] No stuck "Lock and Continue" modal, no dead button, no silent no-op.
- [ ] C2's audio plays **its own extract** — it must not restart Part C from 0:00.
- [ ] If the button legitimately cannot be taken yet, a visible message says why (audio not
      finished) instead of nothing happening.
- [ ] The **full Listening exam** C1→C2 still works — this must not have regressed.
- [ ] **Also check Part A practice** (`/listening/practice/A`): A1 → A2 had the same defect.
- [ ] Part B practice still completes and submits normally.

Repeat once on a phone-sized viewport — the tester's screenshots are mobile.

---

## 6. Verification — Issue 3, Atlas Sample 8 audio (learner)

Already live, but never verified by authenticated playback. **Hard-refresh first** (or clear site
data) so the old service-worker cache is evicted — `CACHE_VERSION` moved to `oet-v5`.

- [ ] Atlas Sample 8 — Separate **Part A** practice plays the correct Part A audio.
- [ ] Atlas Sample 8 — Separate **Part B** practice plays the correct Part B audio.
- [ ] Atlas Sample 8 — Separate **Part C** practice plays the correct Part C audio.
- [ ] Atlas Sample 8 — **Full Listening exam** plays the corrected complete audio, and the
      A → B → C transitions land in the right places (~`644.8s`, ~`1155.1s`).
- [ ] Audio content matches the Atlas Sample 8 question paper — i.e. what you hear is what the
      questions ask about.
- [ ] The old/incorrect audio is gone, including after a refresh and a cache reload.
- [ ] No other Atlas or Nova sample's audio changed.

Cross-check the served assets against the expected durations in section 1.

---

## 7. Known limits — report these, do not "fix" them by relaxing the parser

- **Atlas Sample Test 6, 7, 8 (54 items) need real OCR, not another sweep.** Their question-paper
  PDFs have no text layer for the question content — confirmed with `pdftotext`, only a
  CamScanner-stamped cover page has readable text. `ListeningPartBCSourceParser` operates on
  extracted text; there is nothing for it to read on these three. The codebase already has an
  AI OCR pipeline built for exactly this case: `ListeningPartBCExtractionService`
  (`backend/src/OetLearner.Api/Services/Listening/ListeningPartBCExtractionService.cs`) runs
  Mistral OCR over an uploaded question-paper PDF and has Claude cross-check against the answer
  key, returning a projection an admin reviews and saves via the existing answer-sheet UI
  (`ListeningPartAiExtraction` on the admin Questions tab). Running that against these 3 papers'
  question-paper + answer-key PDFs is the correct next step — do not attempt to teach the
  deterministic text parser to guess from an empty string.
- **Recovery is precision-first by design.** Items reported `unrecoverable` for any OTHER reason
  must be typed in from the printed paper. Do not relax the parser to make them pass.
- **The one hand-restored Nova paper (`77114cbc…`) is not trustworthy ground truth.** Migrations
  `20261127000000` and `20261129000000` set materially different Q30 wording, and the second
  reused the first's Q30 stem as an *option*. Verify that paper against the source PDF like any
  other; do not copy its pattern.
- **A published Listening paper with blank stems still starts.** Unlike Reading (which throws
  `reading_paper_not_publish_ready` at attempt start), Listening does not re-validate on the
  learner path — the publish gate is publish-time only. The amber in-player notice is the current
  mitigation, and it is now visible only on the 3 scanned papers. Flag it if the owner wants a hard
  block instead; it would take those 3 papers offline, which is why it was not done unilaterally.
- **Pre-existing red tests, unrelated to this work.** The backend Listening suite has 23 failures
  that predate every commit in this release (verified by reverting all listening changes and
  re-running: baseline 23 failed / 493 passed). This release's own tests are all green — 33/33 in
  the Part B/C parser+recovery suite, 31/31 in the player's cbla-fidelity suite. `QA Smoke`,
  `SBOM and SCA`, and `Speaking Module CI` are chronically red on `main` for unrelated reasons.
  None of this blocks the release.
- **Multiple sessions push to `main` concurrently.** While shipping this, two other in-flight
  features from other sessions landed on `main` (`feat/ai-w3-completion`-related commits). The
  repo's Actions concurrency group cancelled one in-progress deploy run when a newer push
  superseded it mid-flight — the newer run still contained this release's commits and deployed
  successfully. If a deploy run shows `cancelled`, check whether a newer run for a SHA that still
  contains your commit succeeded before assuming anything is wrong.

---

## 8. Where things live

| Concern | Path |
|---|---|
| Source parser (5 real paper layouts + zero-gap fix) | `backend/src/OetLearner.Api/Services/Listening/ListeningPartBCSourceParser.cs` |
| Recovery service + fleet sweep | `backend/src/OetLearner.Api/Services/Listening/ListeningPartBCSourceRecoveryService.cs` |
| Real word-layout PDF extraction | `backend/src/OetLearner.Api/Services/Content/PdfPigPdfTextExtractor.cs` |
| Existing AI OCR pipeline (for the 3 scanned papers) | `backend/src/OetLearner.Api/Services/Listening/ListeningPartBCExtractionService.cs` |
| Per-paper admin routes | `ListeningAuthoringAdminEndpoints.cs` (`/v1/admin/papers/{id}/listening/part-bc/{source-audit,recover-source}`) |
| Fleet-wide admin routes | same file (`/v1/admin/listening/part-bc/{source-audit,recover-source}`) |
| Scoped-attempt audio | `ListeningLearnerService.ApplyQuestionScope` |
| Backfill guard | `ListeningBackfillService` |
| Player advance/audio model + URL-based sibling detection | `app/listening/player/[id]/page.tsx` |
| Candidate question renderer | `components/domain/listening/BCQuestionRenderer.tsx` |
| Admin recovery panel | `components/admin/listening/part-bc-source-recovery-panel.tsx` |
| Audio upload duration + primary demotion | `app/admin/content/listening/[paperId]/audio/page.tsx`, `ContentPaperService.AttachAssetAsync` |
| Service-worker media bypass | `public/sw.js` |
| Data-quality SQL | `scripts/listening/audit-listening-questions.sql` (queries 6 and 7) |

**Tests:** `ListeningPartBCSourceParserTests` (33), `ListeningPartBCSourceRecoveryServiceTests` (10),
`app/listening/player/[id]/__tests__/cbla-fidelity.test.tsx` (31, including the exact Atlas 8
shared-audio shape), `components/domain/listening/__tests__/BCQuestionRenderer.test.tsx` (2). All
green on every commit before it shipped.

---

## 9. Report back

For sections 4 and 6 (already largely verified — confirm the remaining browser-visual pieces) and
especially **section 5** (not yet watched live): state pass/fail per checkbox, with paper name +
question number for every failure, and a screenshot for anything candidate-visible. List separately:

1. Any item `unrecoverable` for a reason OTHER than the 3 known scanned papers (paper + number + the
   stated reason) — that would be a genuine new finding.
2. Whether the AI OCR pipeline (section 7) closed the 3 scanned papers, if you ran it.
3. The deployed SHA you tested against. It should be `f3c0ff400` or later (check with
   `gh run list --workflow "Build & Deploy (web + API)" --limit 1`); say so if it has moved, and
   check whether a newer commit's deploy superseded a cancelled one before assuming failure.
