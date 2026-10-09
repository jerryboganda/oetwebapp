# SAMI — Agent Handoff for Remaining Work

**Written:** 2026-10-09 · **Release:** `a4929b8a4` (live, verified) · **Repo:** `jerryboganda/oetwebapp`

This is the complete remaining-work handoff for the AI Learning Companion (persona **Sami**).
It replaces any earlier picture you may have; the register was corrected against the code today
and this document reflects that corrected state.

---

## 1. Where the project actually stands

**Beta scope: 156 features** (184 total, minus 17 Stage 4-5 expansion items and 11 owner-deferred).

| Status | Count | Share |
| --- | --- | --- |
| **EXISTS** | 97 | **62.2%** |
| **PARTIAL** | 54 | 34.6% |
| **MISSING** | 5 | 3.2% |

**Read this correctly:** `PARTIAL` means the feature *works* and carries a documented gap — not
that half of it is unbuilt. By effort the project is further along than 62%, but do not quote a
single "percent complete" number to the owner: feature-count and effort-weighted numbers disagree,
and the honest answer is the table above.

Verified by: `python scripts/ai-learning-companion/validate_traceability.py` → **PASS**
(F-001..F-184 once each across JSON, CSV and Markdown matrix).

---

## 2. Read these first (in this order)

1. `AGENTS.md` — the always-on repo contract. Compute rules, ship rules, and the rules that
   override your instincts.
2. `docs/ai-learning-companion/SAMI-RUN-STATE.md` — this run's ledger: decisions, measured UAT
   results, and the defect classes found.
3. `docs/ai-learning-companion/traceability/features.json` — **the source of truth for status.**
   The CSV and Markdown matrix are generated; never hand-edit them.
4. `docs/ai-learning-companion/DECISION_LOG.md` — D-001..D-008, including D-008 (the Sami access
   model: package entitlement + admin enable/disable, no tier ladder, no message cap).
5. `docs/ai-learning-companion/source/SAMI_FINAL_PRODUCTION_HANDOVER_1.0_FINAL.md` — the
   acceptance baseline. §1.2/§6/§9/§10/§11 override older text.

**Non-negotiable context:** Dr Ahmed Hesham's workshops, correction sessions and their recordings
are **never** Sami knowledge sources (owner directive 2026-10-09). This is enforced at the
retrieval layer in `Services/Companion/CompanionContentBoundary.cs`, applied by
`CompanionRetriever` **before** entitlement scoping — not merely in the prompt. Do not weaken it.
Ownership lets a learner *watch* a protected resource; it never lets Sami reproduce it.

---

## 3. The five genuinely MISSING features

| id | Feature | What is actually needed |
| --- | --- | --- |
| **F-076** | Final 24-hours mode | **Blocked on an owner decision.** It overlaps the existing `sami-exam-eve` template, which already covers warm-up/confidence/logistics and excludes new content. Ask the owner **what must differ** before building; do not invent a distinction. |
| **F-089** | Handwritten note understanding | The vision path accepts images and reaches the provider. What is absent is the *instruction and evaluation* for handwriting specifically: transcription-before-correction, and honest uncertainty on illegible words. Note the owner must supply the physical capture (see §5). |
| **F-108** | Tutor handoff summary | Needs a summary artefact handed to a human tutor: what the learner worked on, current weaknesses, open questions. Check `companion_request_handoff` (exists) as the starting point rather than building a parallel path. |
| **F-112** | Resume chat after purchase | A purchase interrupts a conversation and the thread must resume with the new entitlement. Needs the purchase-return flow wired to companion thread state. |
| **F-123** | Tutor pre-session brief | A pre-session brief for a tutor booking. Likely shares most of its machinery with F-108; build them together, not separately. |

---

## 4. The 54 PARTIAL features, grouped by what the work actually is

Full per-feature gap text: `docs/ai-learning-companion/traceability/features.json` (`repo_gap`).
Grouped so you can pick a coherent slice rather than 54 unrelated edits.

### 4A. Instrumentation / plumbing (do these first — they unlock others)

- **F-120 — the biggest genuine gap.** Ten seeded achievements can never be awarded.
  `GamificationService.MeetsCriteria` only understands `attempt_count`, `streak_days`, `total_xp`,
  `vocab_added`, `vocab_mastered`; everything else hits `_ => false`. The API now reports
  `evaluable: false` and the UI labels them "Not tracked" — that is honesty, not a fix.
  To close it: emit `CheckAndAwardAchievementsAsync` from each event source (grading, mock
  completion, drill/review sessions, forum posts, referral conversion, leaderboard refresh) and
  add per-criterion queries for grades, session counts and consecutive improvement.
- **F-070 — confidence picker.** The column (`ReadingAnswer.Confidence`), the optional API field
  and `companion_learning_fingerprint` are all live, but **nothing sends a value**, so the column
  stays NULL and the confidence-vs-accuracy signal produces no data. Needs a learner-facing picker.
  **Owner decision required:** it changes timed-exam UX.
- **F-042** learning memory has no confidence dimension; **F-044** Error DNA has the taxonomy but
  no mark-impact link and **only Writing feeds it** (`WritingErrorDnaFeeder` is the sole feeder —
  `CompanionAdminDashboardEndpoints.cs:874-883` says so).

### 4B. Retrieval / grounding

- **F-014** live-class transcripts are chunked into `ClassRecordingEmbedding` but are not
  `CompanionSource` rows — and `transcript`/`recording` source types are now deliberately refused.
  Resolve which of those two facts wins before "fixing" it.
- **F-018** `ContentPaper`/`ContentPaperAsset` (Reading/Listening papers) are not indexed.
  Published PDF `MaterialFiles` **are** (`material_pdf` via `CompanionDocumentIndexer`).
- **F-019** Tutor Book is deliberately un-ingested as an external product.
- **F-027** no evaluation report bound to a `CompanionKnowledgeRelease`.
- **F-028** no per-source change reason on `RetireAsync` (the changelog is release-level).

### 4C. Tutoring behaviours (9 features)

F-051 selectable examiner mode · F-053 no selectable "Dr Hesham mode" (rulebooks *do* ground chat
via `CompanionRulebookIndexer`) · F-060/061/063 guided/hint/compare writing modes are not distinct
behaviours and are not companion-addressable · **F-063 has no implementation at all** ·
F-066 no learner-facing difficulty matrix · F-068 no per-part A/B/C coach · F-069 Listening has a
spelling/accent taxonomy but nothing feeds Error DNA from it.

**Note F-060/F-061/F-063 share one root cause:** `WritingCoachV2Endpoints` exposes exactly one
operation (`POST /v1/writing/coach/hints`). Fix the endpoint shape once and all three move.

### 4D. Admin / tutor surfaces (6)

F-108, F-123 (see §3) plus the tutor-side brief/queue surfaces. Check what
`CompanionAdminDashboardEndpoints` already exposes (six read-only GETs are live) before adding.

### 4E. Commercial (6) and Identity (5)

F-001 anonymous demo + consented lead capture · F-004 effective-dated exam **version** (this blocks
correct exam-date→version resolution) · F-007 learner-entered prior scores · F-008 one-step
screenshot pre-fill (the confirm gate already exists) · F-010 code-switch preference.

**Check the owner's D-008 ruling before touching anything commercial:** no tier ladder, no message
cap, no visible credit balance, no chatbot top-up packs for Sami.

### 4F. Proactive / engagement (5) and Trust (3)

F-116 content alerts are not relevance-matched · F-117 calendar sync is **tutor-side only** ·
F-118 no push/email/app progress summary (note: reminder **emails are disabled by admin override**
migration `20261212090000` — in-app and push still work) · F-122 no positive-moment gating ·
F-153 no unsupported-claim detection (`CompanionLeakDetector` only checks canaries and verbatim
reuse) · F-158 needs re-auditing after the reset fix shipped today.

### 4G. Exam & analytics, Multimodal, Actions (remaining)

F-080 mastery is a bare 0–100 with no named states · F-030 no companion-led diagnostic ·
F-082 no consolidated cross-subtest timing view · F-088 no audio analysis (voice notes transcribe;
the prompt forbids judging pronunciation from a transcript) · F-095 no selection-range context ·
F-096 Speaking-session state not exposed to the companion.

---

## 5. Blocked on the OWNER — do not attempt without them

1. **Six physical UAT captures.** Instructions, exact prompts and pass criteria are already
   written in `docs/ai-learning-companion/uat/OWNER-CAPTURE-SHEETS.md`. An agent cannot produce a
   blurred photo, handwriting, a real human voice, a live role-play, a sensitive-data photo, or a
   second device. Until they arrive, record these as **owner-run, not executed** — never as passed.
2. **F-076** — what must differ from `sami-exam-eve`.
3. **F-070 confidence picker** — the timed-exam UX call.
4. **F-089** — the handwriting capture.

---

## 6. Working agreement — the rules that actually bite

From `AGENTS.md`, restated because they are where agents lose time:

- **GitHub Actions is the only compute.** No local build/test/lint/tsc/dotnet/pnpm. The loop is
  inspect → edit → commit → push → read CI logs → fix.
- **There is no automated QA anywhere.** The owner QAs manually. Say "not tested - owner QA" and
  ship. Never claim a test, lint or typecheck passed.
- **Ship every slice.** `git add` explicit paths (never `-A`), commit, then `node scripts/ship/ship.mjs`.
  Do **not** batch features behind one end-of-run build: this session's last eight build failures
  were all real errors invisible to reading, including two where the field name was wrong in a file
  the agent had *already read*. The build is how you find them.
- **Verify field names against the entity class body** before writing a query. `StudyPlan` has
  `GeneratedAt` not `CreatedAt`; `StudyPlanTemplateCheckpoint` has `AfterWeek`/`Kind`/`Subtests`
  not `Label`/`AfterWeekIndex`. Both cost a red build.
- **Companion tools need registration in THREE places** or they silently never reach the model:
  `AiToolRegistry.LearnerSafeToolCodes`, `AiToolCatalogSeederHostedService.CompanionToolCodes`,
  and DI in `Program.cs`. `search_recall_set` was invisible for exactly this reason.
- **Diagnose a defect class, not just the defect.** Three times this session a change silently broke
  a neighbour and no check caught it. After touching a shared concept (a memory `kind`, an enum, a
  payload shape), grep every other reader of it.
- **Never force-release a live ship lock.** Verify the owner PID is gone, confirm no process is
  running `ship.mjs`, then recover. A hung ship (0.2 s CPU, frozen lock file) blocks every session.
- **The repository contains documented remediations that were never implemented.** Treat the docs as
  a map and the named source file as the authority.

---

## 7. Environment

- **Live:** `https://api.oetwithdrhesham.co.uk` — `X-Oet-Release` on `/health/live` is the
  authoritative deployed SHA. `/health/ready` should be 200.
- **Credentials:** the owner holds admin and student accounts; **never commit or echo them.**
- **Repo visibility** oscillates PUBLIC/PRIVATE under cross-session ship leases. Only
  `ship.mjs` (or `--may-flip-private` exiting 0) may flip it. If another workstation holds a lease,
  the wrapper correctly keeps it public — do not override.
- **Generated files:** `features.csv` and `FEATURE_TRACEABILITY_MATRIX.md` are produced by
  `scripts/ai-learning-companion/render_traceability.py` from `features.json`. Edit the JSON, re-render,
  then run `validate_traceability.py`.

---

## 8. Suggested order of work

1. **F-120 plumbing** — largest genuine gap, needs no owner decision, and unblocks 10 badges.
2. **F-060/061/063** — one endpoint-shape fix unlocks three features.
3. **F-108 + F-123 together** — they share machinery.
4. **F-004** — exam-version effective dating; it blocks correct version resolution elsewhere.
5. **F-018** — index `ContentPaper`/`ContentPaperAsset` so Reading/Listening papers are citable.
6. **F-158** — re-audit the memory-reset fix now that it is deployed.
7. Everything in §4E–§4G as capacity allows.

**Do not start §3 items without the owner.** Do not "fix" §4B F-014 or F-019 without first
confirming whether their absence is deliberate — both are documented as decisions in
`CompanionKnowledgeGovernance.NotIngested`.
