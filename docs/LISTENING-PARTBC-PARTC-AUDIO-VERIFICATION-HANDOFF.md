# Handoff — verify the Listening Part B/C heading, Separate Part C, and Atlas 8 audio fixes on production

**Release commit:** `ac499562b` — *fix(listening): restore Part B/C printed questions and unblock separate Part C*
**Branch:** already on `origin/main`
**Bug report being closed:** "Critical Listening Bug Report", 28 Aug 2026 (Atlas + Nova; Part B/C headings, Separate Part C C1→C2, Atlas Sample 8 audio)

Production app: `https://app.oetwithdrhesham.co.uk` · API: `https://api.oetwithdrhesham.co.uk`

---

## 0. Deploy status — LANDED, verified

`2cdf9b86d` (which contains the release commit `ac499562b`) **is live on production.**
`Build & Deploy (web + API)` run `33271190604` completed **success** at 2026-08-29 19:57 UTC with
all seven jobs green, including `migrate-production` and `deploy`.

Confirmed against production before this doc was updated:

| Check | Result |
|---|---|
| `GET /health` | `status: ok`, `database: ok` |
| `GET /health/ready` | `database`, `migrations`, `stuck_jobs`, `storage` all `ok` |
| `app.oetwithdrhesham.co.uk/sw.js` | `CACHE_VERSION = 'oet-v5'` and `EXAM_MEDIA_API` present — the frontend half of this release is served |
| `GET /v1/admin/papers/{id}/listening/part-bc/source-audit` | **401** (route registered, auth-gated). A deliberately fake route under the same prefix returns **404**, so 401 proves the new endpoint is deployed |

If you want to re-confirm at any point:

```bash
gh run list --workflow "Build & Deploy (web + API)" --limit 1   --json databaseId,status,conclusion,headSha
curl -s https://api.oetwithdrhesham.co.uk/health/ready
curl -s https://app.oetwithdrhesham.co.uk/sw.js | grep CACHE_VERSION   # expect oet-v5
```

> **Project rule for any future deploy:** GitHub Actions runs require the repo to be **public**
> for the duration of the run. Flip it public, run the workflow, then flip it back to private
> immediately. **Never leave it public.** (`gh repo edit jerryboganda/oetwebapp --visibility
> public|private --accept-visibility-change-consequences`.) The repo is private as of this
> writing — confirm with `gh repo view --json visibility` before you finish.

**What is live is the mechanism, not the repaired data.** Section 3 is still a required manual
step before any Part B/C heading check can pass.

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

The owner will supply a **student (learner)** account and an **admin** account. Both are needed:
admin for section 3, learner for sections 4–5.

Do **not** use `--dev-auth` against production, and do not attempt the seeded
`admin@oet-prep.dev` bootstrap account — it is not usable on prod.

---

## 3. REQUIRED manual step — run the Part B/C recovery (admin)

Nothing about Issue 1 is visible to a candidate until this is run per paper.

**Via the admin UI (preferred):**

1. Sign in as admin → **Admin → Content → Listening**.
2. Open an Atlas or Nova paper → **Questions** tab → **Part B** (or **Part C**).
3. A warning panel appears: *"N Part B/C items have no printed question"*.
   - Click **Re-check** to refresh the audit.
   - Click **Restore N from source** to write the recovered wording.
4. Repeat for every Atlas and Nova Listening paper, Part B **and** Part C.

**Via the API (for scripting the sweep):**

```bash
# Dry-run audit — writes nothing
GET  /v1/admin/papers/{paperId}/listening/part-bc/source-audit

# Apply. dryRun=true previews; the response also returns the publish-gate report
POST /v1/admin/papers/{paperId}/listening/part-bc/recover-source?dryRun=false
```

List the papers to sweep with `GET /v1/admin/papers?subtest=listening&search=atlas` (and
`&search=nova`); read the total from the `X-Total-Count` header. Do **not** use
`/v1/admin/papers/export` — it ignores filters and caps at 2000 rows.

**Expected outcomes, and what each means:**

| Result | Meaning | Action |
|---|---|---|
| `recovered` | Stem/options restored verbatim from that paper's own question paper | Spot-check against the source PDF |
| `already-usable` | The item already showed a real question | Nothing |
| `unrecoverable` + a reason | The source text cannot attribute it safely | **Type it in by hand** from the printed paper |
| `sourceTextAvailable: false` | That paper has no extracted question-paper text | Upload/re-extract the question paper on the **PDFs** tab, then re-check |

`unrecoverable` is a correct, deliberate outcome — the parser reports rather than guesses. The
common causes are a `SAMPLE` watermark interleaving two printed items, and option prose containing
a standalone `A`/`B`/`C`. **Never** invent wording to clear one.

---

## 4. Verification — Issue 1, Part B/C headings (learner)

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

**If an item instead shows the amber notice** *"The printed question for Qn is not available on
this paper yet…"* — that is the new safety net working as designed, and it means section 3 has not
been completed for that item. Go back and either re-run recovery or enter it by hand. Record every
such item by paper + number.

Screenshot Q25 and Q28 on Atlas Part B specifically — those are the two the tester reported.

---

## 5. Verification — Issue 2, Separate Part C C1→C2 (learner)

This is the flow that was dead. Test on **both** Atlas and Nova.

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

## 7. Known limits — report these, do not "fix" them

- **Recovery is precision-first by design.** Items reported `unrecoverable` must be typed in from
  the printed paper. Do not relax the parser to make them pass.
- **The one hand-restored Nova paper (`77114cbc…`) is not trustworthy ground truth.** Migrations
  `20261127000000` and `20261129000000` set materially different Q30 wording, and the second
  reused the first's Q30 stem as an *option*. Verify that paper against the source PDF like any
  other; do not copy its pattern.
- **A published Listening paper with blank stems still starts.** Unlike Reading (which throws
  `reading_paper_not_publish_ready` at attempt start), Listening does not re-validate on the
  learner path — the publish gate is publish-time only. The amber in-player notice is the current
  mitigation. Flag it if the owner wants a hard block instead; it would take affected papers
  offline, which is why it was not done unilaterally.
- **Pre-existing red tests.** The backend Listening suite had 23 failures on this branch *before*
  this work (verified by reverting and re-running: baseline 23 failed / 493 passed; with the
  release 23 failed / 514 passed — i.e. only the 21 new tests were added). `QA Smoke`, `SBOM and
  SCA`, and `Speaking Module CI` are chronically red on `main`. None of these block the release.

---

## 8. Where things live

| Concern | Path |
|---|---|
| Source parser | `backend/src/OetLearner.Api/Services/Listening/ListeningPartBCSourceParser.cs` |
| Recovery service | `backend/src/OetLearner.Api/Services/Listening/ListeningPartBCSourceRecoveryService.cs` |
| Admin routes | `backend/src/OetLearner.Api/Endpoints/ListeningAuthoringAdminEndpoints.cs` (`part-bc/source-audit`, `part-bc/recover-source`) |
| Scoped-attempt audio | `ListeningLearnerService.ApplyQuestionScope` |
| Backfill guard | `ListeningBackfillService` |
| Player advance/audio model | `app/listening/player/[id]/page.tsx` |
| Candidate question renderer | `components/domain/listening/BCQuestionRenderer.tsx` |
| Admin recovery panel | `components/admin/listening/part-bc-source-recovery-panel.tsx` |
| Audio upload duration + primary demotion | `app/admin/content/listening/[paperId]/audio/page.tsx`, `ContentPaperService.AttachAssetAsync` |
| Service-worker media bypass | `public/sw.js` |
| Data-quality SQL | `scripts/listening/audit-listening-questions.sql` (queries 6 and 7) |

**Tests added:** `ListeningPartBCSourceParserTests` (11), `ListeningPartBCSourceRecoveryServiceTests` (10),
plus 3 Part C navigation cases in `app/listening/player/[id]/__tests__/cbla-fidelity.test.tsx` and
2 in `components/domain/listening/__tests__/BCQuestionRenderer.test.tsx`.

---

## 9. Report back

For each of sections 4, 5 and 6: state pass/fail per checkbox, with paper name + question number
for every failure, and a screenshot for anything candidate-visible. List separately:

1. Every item still `unrecoverable` after the recovery sweep (paper + number + the stated reason).
2. Any paper with `sourceTextAvailable: false`.
3. The deployed SHA you tested against. It should be `2cdf9b86d` or later; re-confirm with the
   commands in section 0 before you start, and say so if it has moved.
