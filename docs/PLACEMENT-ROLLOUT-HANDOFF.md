# Placement Test — Rollout & Handoff (2026-09-18, updated 2026-09-20)

Free General-English placement test on the private GEPA engine
(`D:\Projects\GEPA`, live at gepa.polytronx.com in free_beta). This page is the
handoff for the whole rollout; §0 and §0b are the historical record of the first
round, §5–§8 are the owner decisions of 20 Sep 2026.

## Owner decisions of 20 Sep 2026 (summary)

| # | Decision | Where |
|---|---|---|
| 1 | Both repositories stay **private at all times**. CI and deploy run on a private self-hosted runner. Deploy GEPA first, then oetwebapp. Production-verified only after the private CI build and the complete automated test suite pass after push. | §2, §7, §8 |
| 2 | Dr Ahmed Hesham is the named Product / Academic Reviewer for the Placement Test flow and content (internal production approval). Formal psychometric validation stays a separate later stage. | §5 |
| 3 | The Pre-A1 Language Systems bank was one item short of a 5-item confirmation block. A fifth genuine Pre-A1 item (`LS-PRE-05`) is added; the block is not reduced. | §3 |
| 4 | Extra time is admin-approved for the initial release; candidates cannot self-enable it. | §6 |
| 5 | `Placement.BetaOnly` stays enabled until the owner finishes the real-device production checks and gives final approval for broad student access. Website PR (oetwebsite #2) stays unmerged until then. | §2, §8 |

Whether each step has actually passed is tracked in the verification log in §8,
not here.

## What is built, and where

| Piece | Repo / branch | Commit(s) |
|---|---|---|
| Engine fixes + readiness gate + service role | GEPA `main` | `edfef0e`, `9d5fb9f` |
| Engine hardening: one unit-timing table, unit start / technical routes, media serving, inventory report, Grammar/Vocabulary diagnostics (DECISIONS D-028, D-030–D-032) | GEPA `feat/placement-hardening` | `cde1e75` |
| API proxy + minimal signup + result history (PR #230, merged) | oetwebapp `main` | `bc1016a7`, `5eb6aab0`, `2ec29648` and follow-ups; merge commit `fad48f707` |
| Production hardening: five-part flow, Listening / Speaking / Writing, timers, results, dashboard card + sidebar item, admin inventory; P0 fixes for Listening audio and Speaking upload; Safari deadline parsing | oetwebapp `fix/placement-p0-media` | `8950317f6`, `0ae9456e2`, `2dcb1e93f` (CI baseline repair `ad3a88886`) |
| Phase 2 (20 Sep): admin-approved extra time (engine + API + admin console), Pre-A1 item `LS-PRE-05`, private CI | GEPA + oetwebapp | commit ids are recorded in the §8 log at push |
| Website discovery (homepage / nav / landing page) | oetwebsite `feat/placement-test` | `9c0acac` (PR #2, held — §8) |

The oetwebapp hardening needs the matching GEPA engine release; older engines
degrade gracefully (build-time deadline, no inventory).

## 0. Live end-to-end verification (2026-09-18) — historical

This ran against the build as it was on 18 Sep, before the hardening commits
above and before the phase-2 changes. It is **not** verification of the current
build.

The full stack was driven live before handoff: GEPA engine + real
Postgres (readiness gate green, checksums verified), OET API wired to it
(schema cloned from prod + this migration), production Next.js build in
a real browser. Verified: minimal registration (+ control that standard
signup still requires enrollment), proxy session create with service
token, full LS/RD/LSN adaptive run to measured bands, receptive profile,
real WAV upload with server-measured metrics (4.00s pcm_analysis),
speaking/writing honest `pending_review` without a provider key, full
result honesty (SPK/WRT insufficient_evidence, headline none), OET-owned
PlacementResults row with ruleset 2.0.0-beta + history endpoints, audio
streaming, cross-account denial, retention sweep keeping results, and
the browser-verified placement-entry redirect to the minimal signup.
Four defects found and fixed in `2ec29648`/`9d5fb9f` (details in those
commits) — notably the migration is now idempotent, which unblocks the
production deploy itself.

PRs: oetwebapp#230 (merged), oetwebsite#2 (unmerged, held).

## 0b. Remaining-engineering round completed (2026-09-18, later commits) — historical

The six follow-up items from the plan's final review shipped on
`feat/placement-test` (oetwebapp PR #230, since merged):

- `a8e91612` — proxy endpoint tests against a stub engine (flag gate,
  service-token + candidate-uid forwarding, results persistence, admin
  boundary). 6/6 green.
- `f9b8ddf8` — hermetic Playwright journey (unauth redirect + mocked
  full objective run → foundation profile) + the runner resume-advance
  fix it caught + opt-in `allowMockedBackendNoise` diagnostics option.
- `92cec945` — controlled-beta allowlist: `Placement.BetaOnly` +
  `Placement.BetaEmails` (admin-editable runtime settings), 404 for
  outsiders, `access: granted | not_in_beta` on /status.
- `fc783c2f` — `/placement-test/history` + `/placement-test/results/[id]`
  standalone pages; ResultReportCard extracted + made casing-tolerant
  (engine emits retestAdvice/confidenceReasons in camelCase); admin
  review console at `/admin/placement` (queue, detail, audio playback,
  re-rate, append-only human score).

## 1. Connect the OET API to the engine (owner-checked, private)

The engine must stay private (internal docker network / localhost; never
publish a new public route). On the VPS both stacks already run under the
`platform` network.

1. Mint a service token (admin account on the engine):
   `POST /api/admin/service-token` with an admin bearer token
   (body `{ "ttl_days": 30 }`). Calendar reminder to rotate.
2. Set env on the OET API service:
   - `GEPA_ENGINE_BASE_URL` — internal engine URL
     (e.g. container name / internal address reachable from the API)
   - `GEPA_SERVICE_TOKEN` — the minted token
   (`Program.cs` also accepts `Placement:EngineBaseUrl` config).
3. Restart the API. `GET /v1/admin/placement/health` (admin token) returns
   the engine's `/readyz` (per-band bank coverage + manifest checksums).

## 2. Deploy order

Both repositories stay private throughout; nothing in this order involves
making either one public (§7).

1. **GEPA engine first.** Push, then the private CI runs the full build and
   complete automated test suite (`ci.yml`). On a green push to `main` the
   release job tags the next beta and `deploy-vps.yml` deploys it. The engine's
   readiness gate refuses new sessions if the seed manifest checksums fail —
   that is the kill-switch (`ASSESSMENT_READINESS_GATE=false` is the rollback).
   Confirm the engine is healthy before step 2.
2. **Then oetwebapp.** Only after step 1 is deployed and healthy: the hardening
   and phase-2 changes reach `main` and `Build & Deploy (web + API)` runs on the
   private runner. The placement routes 404 until the flag is flipped, so this is
   safe pre-launch.
3. **Controlled beta (BetaOnly stays ON).** Admin console → Runtime Settings →
   `placement.placementEnabled = true` (DB override over env
   `Features:PlacementEnabled`) with `Placement.BetaOnly` on and the testers in
   `Placement.BetaEmails`. Everything is learner-gated. The owner runs the
   real-device checks here: `docs/PLACEMENT-REAL-DEVICE-CHECKLIST.md`.
4. **Broad student access — only after the owner's final approval (§8).** Turn
   `Placement.BetaOnly` off; then merge PR #2 (website) → VPS
   `git reset --hard origin/main` per the website deploy runbook.

The public/private flip steps in `AGENTS.md` (Ship-It Workflow steps 3 and 6;
the "GitHub Actions visibility" section) do not apply to this rollout: the owner
decision of 20 Sep is that neither repository is ever made public, not even
temporarily.

## 3. What is already owner-approved vs still owner-gated

Shipped (approved plan defaults): minimal same-account signup, beta-first
release, human-review fallback for all productive scoring (no paid AI
calls — the engine only calls Gemini when `GEMINI_API_KEY` is set, and a
missing/failing provider leaves submissions `pending_review`, never
simulated scores).

Decided 20 Sep 2026:
- **Pre-A1 Language Systems item.** The bank was one item short of the intended
  5-item confirmation block. The owner chose to **add** a fifth item (`LS-PRE-05`,
  authored separately) rather than reduce the block to four. It must be genuine
  Pre-A1 difficulty, test a construct not already used by the other four items,
  have plausible distractors, be culturally neutral, and pass the same
  item-quality checks as the rest of the bank. The item and how it was checked
  are recorded in GEPA `docs/DECISIONS.md` D-033. It is hand-authored and is not in
  the source PDF, and `scripts/parse_pdf_to_seed.py` still asserts the PDF's 52
  Language Systems items, so regenerating `seed/` from the PDF would drop it until
  the item is re-added or the parser is extended (D-033).
- **Extra time** is admin-approved (§6), **named reviewer** (§5), **private CI**
  (§7), **BetaOnly** stays on (§8).

Still owner-gated before broad student access:
- **Real-device production checks and final approval** — the owner runs the
  checklist (Chrome, Safari, mobile, desktop/laptop) and gives the go-ahead (§8).
- **Rest of the item-bank expansion** — the confirmation phase honestly reports
  an evidence shortfall when a band runs dry (never cross-band substitution).
  The inventory matrix in Admin > Placement (module x CEFR band, cells flagged
  empty or below one confirmation block) shows where the bank is thin. Expand
  `seed/` and re-verify the manifest checksums (`seed:validate` runs in CI).
- **Formal psychometric validation** — a separate later stage. Until then no
  stronger validation claims (§5).
- **`GEMINI_API_KEY`** — enables automated Writing/Speaking rating
  (audio is sent inline; ratings are audio-grounded). Without it,
  productive results require reviewer action in the queue.
- **Reviewer capacity** — the admin review queue at
  `/v1/admin/placement/review/queue` needs named reviewers for
  `pending_review` sessions. The 20 Sep decisions do not address this.
- **Retention / retest policy** — engine recordings expire on the 90-day
  clock; OET `PlacementResults` history is permanent by design. Retest
  cadence is not limited (each session is a fresh attempt; history keeps
  all of them).
- **Legacy standalone GEPA site cutover** — gepa.polytronx.com stays live
  until an approved data-preserving cutover (pg_dump archive → retire;
  anonymous sessions are not migratable to OET accounts).
- **Website PR (oetwebsite #2)** — stays unmerged until the owner's final
  approval.

## 4. Journey / behaviour notes

- The journey is five parts — Language Systems, Reading, Listening, Speaking,
  Writing — with a persistent "Part X of 5" header and a transition screen
  between parts. Approximate full-profile time 60–85 minutes.
- A reload resumes an open attempt. Listening resumes through the Audio Check
  screen (autoplay needs a fresh tap) and Speaking through the Microphone check
  (the microphone needs permission again).
- Unauthenticated `/placement-test` → minimal placement signup
  (`/register?purpose=placement`) → straight back into the test
  (`next` preserved through sign-in / MFA / device challenges).
  Existing users follow "Sign in" from that screen.
- The exam-date gate never diverts the placement route
  (`EXAM_DATE_EXEMPT_PATHS`); learners who registered minimally set
  exam date via goals/onboarding when they enroll for OET.
- Attribution: website CTAs carry
  `utm_source=website&utm_campaign=placement_test&next=/placement-test`;
  the placement entry captures UTMs (first-touch wins) and the signup
  payload persists them.
- Missing-evidence honesty: unmeasured modules report `not_measured`
  (never a default band); partial profiles are first-class results.
- Recording expiry never deletes result history (engine retention clears
  audio + references; OET-side `PlacementResults` is permanent).
- An account with an active extra-time grant sees a line on the test overview
  saying extra time has been approved (§6).

## 5. Named reviewer

**Dr Ahmed Hesham** is the named **Product / Academic Reviewer** for the GEPA
Placement Test flow and content, for **internal production approval**. Recorded in
the GEPA repository as `docs/DECISIONS.md` D-035.

- This is not psychometric validation. Formal psychometric validation stays a
  separate later stage, and until it is done no stronger validation claims are
  made.
- The GEPA claims policy stays in force: results are an "indicative placement
  estimate" / "diagnostic profile", never "validated", "certified" or
  "CEFR-aligned", and confidence is Low or Moderate only.
- It is a governance designation. It does not by itself create an account role,
  and it is separate from staffing the Speaking/Writing review queue (§3).

## 6. Extra-time accommodations (admin runbook)

**Rules**
- Extra time is **admin-approved** for the initial release. A candidate cannot
  turn it on: the test has no accommodation control, and the server takes the
  grant from the signed-in account, not from anything the candidate sends. A
  student-facing request workflow can be added later; it is not part of this
  release.
- A grant is a whole-number percentage (the console offers +25%, +50%, +75%,
  +100%; the API accepts 1–100). The engine multiplies the timed allowances by
  1 + percentage/100: objective question timers, Writing time limits and Speaking
  planning time. Speaking response caps do not change (GEPA D-034).
- One active grant per learner account. Granting again replaces it; the earlier
  grant stays on record as revoked (reason "superseded").
- A grant applies when the learner **starts a new attempt**. An attempt already
  under way keeps the allowance it was started with.
- Permissions: opening the Placement page needs the review-ops permission; the
  card's calls need `learner:write` (grant, revoke) or `learner:read` (view).
  `system_admin` passes the API checks.

**Grant extra time**
1. Sign in as an admin → **Admin > Placement** (`/admin/placement`) → the
   **Extra-time accommodations** card.
2. In **Grant extra time**: enter the learner's email or user ID, choose the
   percentage, and optionally add an administrative reference (up to 200
   characters, e.g. a ticket number).
3. Select **Grant extra time**. You should see "Extra time granted: +N% for
   <learner>." and a new row: Status Active, Extra time +N%, Approved by your
   name and the time.
4. Ask the learner to start a new attempt. Their test overview shows a line that
   extra time has been approved for the account (+N% on timed sections).
   "No learner matches that email or id" means the address or ID is not a learner
   account.

**Revoke extra time**
1. Find the row (**Filter by learner**; **Show revoked** to include past grants).
2. Select **Revoke**, optionally enter a reason (up to 200 characters), then
   **Confirm revoke**.
3. The row shows Revoked with who revoked it, when, and the reason. The grant and
   its usage history stay on record; nothing is deleted. To restore extra time,
   grant it again (a new grant).

**What is recorded**

| What | Where you see it |
|---|---|
| Who approved (name and user id) | **Approved by** column; audit log |
| Date and time approved | **Approved by** column (local time with zone; stored in UTC) |
| Percentage granted | **Extra time** column |
| Which attempt used it | **Attempts used** — expand to list each attempt's session id, when it was applied and the percentage it got. One entry per attempt, a snapshot that later grant changes never rewrite |
| Who revoked, when, why | **Status** column |
| Every grant and revoke | Admin audit log (`PlacementAccommodationGranted` / `PlacementAccommodationRevoked`) |
| The approval on the attempt itself | The engine stores the approval (id, approver, time) and the percentage on the session, which is the attempt (GEPA D-034) |

**Privacy**
- The reference and revoke-reason fields are administrative only. Never enter
  medical or personal health details (the form warns about this).
- The list shows which learners have an accommodation. Treat it as sensitive; it
  is limited to admins with learner permissions.
- Learner screens show only that extra time was approved and how much. The
  engine's session record for the attempt also holds the approval, including the
  approver's name and user id, and the learner's own session data can include it
  (GEPA D-034); raise it if that is not acceptable.
- Accommodation status is never a negative signal and never lowers confidence by
  itself (GEPA `docs/01_PRD.md` §10 and `AGENTS.md` §5).

The real-device checklist has a row that compares the timers of an extra-time
account with a standard account.

## 7. Private CI and runner

Decision (owner, 20 Sep 2026): `jerryboganda/GEPA` and `jerryboganda/oetwebapp`
stay **private at all times**, never public even temporarily, to run Actions.
GitHub-hosted runners are refused for private repositories on this account by a
billing / spending-limit block (the job never starts: no steps, no runner), so
CI and deploy run on a **private self-hosted runner** — a dedicated WSL2 Ubuntu
distro on the owner workstation.

- Workflows select the runner with the repository variable `CI_RUNS_ON`
  (`oet-private` = the private runner); when it is unset they fall back to
  `ubuntu-latest`.
- Set-up, start/stop, hardening, disk hygiene and the rules for the runner:
  **`docs/PRIVATE-CI-SELF-HOSTED-RUNNER.md`**.
- Nothing starts automatically with Windows; while the distro is stopped, jobs
  queue and wait rather than fail. Run one repository's jobs at a time.
- Never register a self-hosted runner on a public repository.
- Fixing Billing & plans on GitHub would let `CI_RUNS_ON` be unset again.

## 8. Go-live gate

This phase counts as **production-verified only after the private CI build and
the complete automated test suite have passed on the pushed commit**. Quote the
workflow run; a local run does not count. In order:

1. Push; private CI green (full build + complete automated test suite).
2. Deploy **GEPA** and confirm it is healthy.
3. Deploy **oetwebapp** and confirm it is healthy.
4. Send the owner confirmation.
5. Owner performs the real-device production checks
   (`docs/PLACEMENT-REAL-DEVICE-CHECKLIST.md`): Chrome, Safari, mobile,
   desktop/laptop.
6. Owner gives final approval for broad student access.

`Placement.BetaOnly` stays **enabled** through step 5 and until step 6. The
website PR (oetwebsite #2) stays unmerged until step 6.

Verification log (fill in as each step passes):

| Step | Repo | Commit | Workflow run | Result |
|---|---|---|---|---|
| CI build + full test suite | GEPA | | | pending |
| Deploy | GEPA | | | pending |
| CI build + full test suite | oetwebapp | | | pending |
| Deploy | oetwebapp | | | pending |
| Real-device checks | both | | checklist | pending (owner) |
| Final approval for broad access | both | | | pending (owner) |

## 9. Known follow-ups (non-blocking)

- GitHub-hosted runners are blocked for the private repositories by an account
  billing / spending-limit block ("job was not started — payments have failed or
  spending limit"). For this phase the private runner (§7) is the resolution;
  fixing billing would allow returning to hosted runners.
- A hermetic Playwright journey exists
  (`tests/e2e/placement/placement-journey.spec.ts`). A live-engine journey could
  be added on top of `lib/api/placement.ts` fixtures (the engine has an `E2E_MODE`
  seeding route that writes real WAV fixtures for speaking).
- A student-facing extra-time request workflow, if wanted (§6).
- The engine's `ruleset_version` surfaces on session create/state and is
  stored with every `PlacementResult` — use it when the ruleset
  recalibrates after field data lands.
