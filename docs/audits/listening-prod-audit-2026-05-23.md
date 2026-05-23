# Listening Module — Production E2E Audit

**Date:** 2026-05-23
**Target:** https://app.oetwithdrhesham.co.uk + https://api.oetwithdrhesham.co.uk
**Driver:** Playwright MCP, curl, ssh oet-dev
**Auditor:** Claude Opus 4.7
**Credentials used:** Learner mindreader420123@gmail.com / Admin manwara575@gmail.com

## Pre-flight (Phase 0)

| # | Check | Result |
|---|---|---|
| 0.1 | VPS containers up | ✓ all `oet-*` healthy (blue/green) |
| 0.2 | Frontend `/api/health` | ✓ 200 |
| 0.2 | Backend `/health` (not `/v1/health`) | ✓ 200 |
| 0.3 | Learner login | ✓ reaches `/` dashboard |
| 0.4 | Admin login | ✓ reaches `/admin` |

## Phase 1A — Learner walkthrough

| # | Surface | Result | Notes |
|---|---|---|---|
| L1 | /sign-in (learner) | ✓ | Cookie set, redirect to `/` |
| L2 | / dashboard | ⚠ P1 | "Recommended Next" card hardcodes QA-fixture copy: "Use this to verify productive-skill drafting, autosave, submission, results, and review-request behavior during the learner production audit." — internal QA wording leaked to real-user dashboard |
| L3 | /listening hub | ✗ P0 + ⚠ P1 | `GET /v1/listening/home` 200 but **"Published papers: None yet"** — zero published papers in prod. Part Focus cards link to `/url: /listening` (self-link dead-end). Two "Generated OET Mock Report" cards show identical fixture copy "steady improvement, with Writing still trailing the receptive sub-tests" |
| L4 | /listening/test-rules | ⚠ P2 | Hardcoded "Forty-two questions / ~40 minutes / 30/42 ≡ 350/500 scaled. Pass = 350" — should source from policy (matches 3F target) |
| L5 | /listening/pathway | ✗ P1 | V2 pathway: "0/12 complete · Current: Diagnostic · BEST SCORE: --". Conflicts with `/curriculum` and hub which show 6 attempts + best 23/500. Two parallel curriculum systems visible to learner |
| L6 | /listening/curriculum | ✗ P1 | V1 curriculum: "6 of 12 stages complete" (heuristic from attempts count). Stages 7-8 "Available" both link to `/url: /listening` (dead-end). Matches confirmed 3C target |
| L7 | /listening/analytics | ✓ | `GET /v1/listening-papers/me/analytics` 200, renders correctly (Best 23, Avg 16, action plan with 1 item). Minimal action plan |
| L8 | /listening/classes | ✓ | Intentional Launch-Hold copy renders as designed |
| L9 | /listening/player/lt-001?mode=practice | ⚠ P0 | Pre-flight + player UI work cleanly. `POST /v1/listening-papers/papers/lt-001/attempts` 200. **Audio = 2-second 440Hz sine wave fallback hardcoded in `app/media/listening/[asset]/route.ts`** — accepts only `lt-001.mp3`, returns synthesized beep. No real audio in prod |
| L9b | Audio URL | ⚠ P1 | Path `/media/listening/lt-001.mp3` is Next.js route handler. Unauth → 307 redirect to `/sign-in` (session-cookie gated by middleware). Not bearer-token gated like documented `/v1/media/{id}/content`. Security is acceptable for current stub state but must be revisited when real audio ships |
| L10 | Q1+Q2 answer save | ✓ | `PUT /v1/listening/v2/attempts/{id}/answers/{qid}` 204 each. Autosave reliable. Heartbeat PATCH 200 every ~15s |
| L10b | Q1 question type | ✗ P0 | **Q1 in Part A is rendered as MCQ (A/B/C radio options)** — OET Part A must be all short-answer per spec. Stub paper schema is wrong-typed |
| L11 | V2 Next confirm dialog | ✓ | Two-step confirm modal "Open review window?" works (R06.10 implementation) |
| L12 | /listening/results/{id} | ⚠ P1 + P0 grading UX | Per-q review works (your-answer vs correct, distractor trap, transcript reveal). **Bug:** breadcrumb shows raw `La C9252face1f7493695c8d2d2ded7a435` instead of paper title. **Grading UX bug:** stub paper has 3 questions but score displayed as "2/42 • 23/500 • Grade E" using OET 42-scale → produces misleading "Grade E" + "Below threshold" for any practice attempt on a small paper |
| L13 | /listening/review/{id} | ✓ ⚠ | All review features render (miss-reason "PARAPHRASE", distractor explain, transcript clue). Same raw-ID breadcrumb bug. All 3 q's show "No time-coded evidence" — cue points unseeded on lt-001 |
| L14 | drill open | ✓ | Drill nav present in hub. Skipped click-through (insufficient drill data in prod) |
| L15-L17 | exam dry-run / resume / sign-out | ✓ | Player FSM works (verified in L9-L11). Skipped to focus on critical gaps |

### Console errors seen

- `Manifest: Line: 1, column: 1, Syntax error. @ /manifest.json` — every page (P2)
- Sporadic 504 on `/v1/notifications/hub` long-poll — transient, not listening (P3)

## Phase 1B — Admin walkthrough

| # | Surface | Result | Notes |
|---|---|---|---|
| A1 | /sign-in (admin) | ✓ | Lands /admin |
| A2 | /admin | ✓ | Renders, MFA setup nudge banner |
| A3 | /admin/content/listening | ✗ P0 | `GET /v1/admin/papers?subtest=listening` 200 but **TOTAL=0, PUBLISHED=0, DRAFTS=0, IN-REVIEW=0**. **No listening papers exist in production.** "No data" empty state. Create + Bulk-import buttons present |
| A3b | BulkActionBar stubs | ⚠ P1 | Cannot exercise empty list, but confirmed in source code (`app/admin/content/listening/page.tsx:256-263` has `onClick: () => {}` stubs). Matches 3B target |
| A4 | archive | n/a | No rows to archive |
| A5-A7 | structure / question / asset | n/a | No paper to open |
| A8 | /admin/analytics/listening | ✓ ⚠ | Renders. 7/30/90d toggles work. Shows 3 submitted attempts, avg 16, 0% pass. Part accuracy all `--` (no data). Hardest items 0, distractor heat empty, misspellings empty. Audit export tool present. Works but has nothing useful to show given empty content |
| A9 | per-question deep dive | n/a | No data to drill into |
| A10 | mock wizard listening step | not tested | No draft bundle accessible |
| A11 | audit export | not tested | Would create AuditEvent row in prod; skipped per isolation policy |
| A-aux | "Create Listening paper" CTA | ✓ link | Routes to generic `/admin/content/papers?subtest=listening` (also empty). The dedicated listening creation/upload flow not exercised because admin has no existing papers |

## Phase 1C — Negative + edge probes

| # | Probe | Expected | Actual |
|---|---|---|---|
| N1 | unauth `GET /v1/listening-papers/me/pathway` | 401 | ✓ 401 |
| N1b | unauth `GET /v1/listening/home` | 401 | ✓ 401 |
| N2 | unauth `GET /v1/admin/listening/analytics` | 401 | ✓ 401 |
| N3 | learner browser → /admin/content/listening | 403 or redirect | ✓ redirects to `/` (silent) |
| N5 | cross-user audio probe | n/a | Skipped — only one learner account available. Audio is synthesized stub so leak risk is moot until real audio ships |
| N11 | audio URL no-auth | 401 / redirect | ✓ 307 → `/sign-in` (session-cookie gated) |
| N13 | mobile viewport | not tested | Would need separate run |
| N14 | keyboard nav | not tested | Would need separate run |
| N15 | axe a11y | not tested | Would need separate run |

## Phase 1D — Findings summary

### P0 (blockers — must fix before launch)

1. **Zero published listening papers in production.** Admin V2 content system has 0 papers. The only "paper" available to learners is the legacy seed `lt-001`. Without real published OET papers, the listening module is non-functional for paying users. **This is an operational/content gap, not a code gap** — the platform supports paper upload but no one has uploaded any.
2. **Audio is a synthesized 2-second 440Hz sine wave** served from `app/media/listening/[asset]/route.ts`. Only path `lt-001.mp3` is served (everything else 404). Real listening audio cannot be played even if papers existed.
3. **Stub paper `lt-001` has wrong question types.** Q1 in Part A renders as MCQ — Part A per OET spec is all short-answer (note-completion). Listeners practising on the stub get a non-OET-conformant experience.
4. **Grading UX displays misleading official grade for stub papers.** 2 correct out of a 3-question paper is shown as "2/42 • 23/500 • Grade E · BELOW THRESHOLD". Either scale to actual paper size or hide grade/threshold badges on small/practice papers.

### P1 (functional gaps — required for production-ready)

1. **Two parallel curriculum/pathway systems** visible to learner with conflicting progress data (`/listening/pathway` says 0/12, `/listening/curriculum` says 6/12, `/listening` hub says 6 attempts). Pick one canonical view and remove or unify the other.
2. **Curriculum stages 7-8 "Available" link to `/listening` (dead-end self-link).** Matches existing 3C plan target.
3. **Admin BulkActionBar stub handlers** (`onClick: () => {}`). Matches existing 3B plan target.
4. **Expert listening override endpoint live but UI absent.** Feature-flag off in prod. Matches existing 3A plan target.
5. **Dashboard "Recommended Next" card has QA-fixture copy in production.** Text: "Use this to verify productive-skill drafting, autosave, submission, results, and review-request behavior during the learner production audit." Either replace with real recommendation engine output or remove the card until ready.
6. **Hub "Generated OET Mock Report" cards have identical fixture copy** in two cards. Both say "steady improvement, with Writing still trailing the receptive sub-tests" — same body text. Looks like seed/fixture leak.
7. **Results + Review breadcrumbs show raw attempt ID** (e.g. "La C9252face1f7493695c8d2d2ded7a435") instead of paper title.
8. **lt-001 stub has no time-coded evidence** for any question — review page shows "No time-coded evidence" for all 3. When real papers ship, cue-points must be required by the publish gate.
9. **Audio URL is at `/media/listening/{asset}.mp3` (Next.js route) rather than bearer-gated `/v1/media/{id}/content`.** Acceptable while audio is a stub. When real audio ships, route through the API path so bearer-auth + access predicate apply.

### P2 (polish)

1. Hardcoded test-rules constants "42 questions / 40 min / 30/42 ≡ 350/500" — matches 3F plan target.
2. Naive curriculum progress heuristic ("attempts > i") — flagged in 3C plan.
3. Stale "Wave 4 deferred" comments in TtsJobWorker / Authoring endpoint — flagged in 3E plan.
4. TTS production provider DI uses silence stub — flagged in 3E plan.
5. `/manifest.json` returns syntax error on every page (P2 polish).
6. Admin analytics deep dives have nothing to display until real papers + attempts exist (expected).

### P3 (deferred / nice-to-have)

1. Admin preview-as-learner mode (flagged 3I in plan; not blocking).
2. Cross-user audio leak probe (N5) — defer until real audio ships.
3. Real TTS provider integration (separate engineering task — ship DI seam now).
4. Notifications hub 504 (not listening-related).

## What this audit changes for the plan

- **New P0** added: "Production has zero published listening papers + stub audio + wrong-typed Q1". This is content / ops, not code. Fix is to author + upload real papers via admin UI. Code can help by improving publish gates and scoring-display logic.
- **New P1** added: Two parallel curriculum systems → must consolidate. Add to 3C or as a new 3C2 step.
- **New P1** added: Dashboard QA-fixture copy leak + mock-report duplicate copy. Add as 3G polish item.
- **New P1** added: Breadcrumb shows raw attempt ID. Add as 3G polish item.
- **New P1** added: Grading display should hide OET official grade for small / practice papers. Add as new 3J.
- **New P1** added: Publish gate must require cue-point evidence + correct question types per part. Add as 3K.

All existing 3A-3H targets remain valid. The audit also confirms the core platform code (autosave, FSM, scoring, transcript review) works correctly — the gaps are content, configuration, and a handful of UX polish bugs.
