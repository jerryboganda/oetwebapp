# Placement Test — Rollout & Handoff (2026-09-18)

Free General-English placement test on the private GEPA engine
(`D:\Projects\GEPA`, live at gepa.polytronx.com in free_beta). Everything
below refers to the three shipped pieces:

| Piece | Repo / branch | Commit |
|---|---|---|
| Engine fixes + readiness gate + service role | GEPA `main` | `edfef0e`, `9d5fb9f` |
| API proxy + minimal signup + result history | oetwebapp `feat/placement-test` | `bc1016a7` |
| Web app placement journey | oetwebapp `feat/placement-test` | `5eb6aab0` |
| Live end-to-end fixes | oetwebapp `feat/placement-test` | `2ec29648` |
| Website discovery (homepage/nav/landing page) | oetwebsite `feat/placement-test` | `9c0acac` |

## 0. Live end-to-end verification (2026-09-18)

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

PRs: oetwebapp#230, oetwebsite#2.

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

1. Engine (GEPA): tag → existing tag workflow → smoke. Its readiness gate
   refuses new sessions if the seed manifest checksums fail — that is the
   kill-switch (`ASSESSMENT_READINESS_GATE=false` is the rollback).
2. OET API + web (merge PR #230 → deploy workflow). The placement routes
   404 until the flag is flipped, so merging is safe pre-launch.
3. Controlled beta: admin console → Runtime Settings →
   `placement.placementEnabled = true` (DB override over env
   `Features:PlacementEnabled`). Everything is learner-gated; test the
   journey end to end.
4. Public availability: flag stays on; merge PR #2 (website) → VPS
   `git reset --hard origin/main` per website deploy runbook.

## 3. What is already owner-approved vs still owner-gated

Shipped (approved plan defaults): minimal same-account signup, beta-first
release, human-review fallback for all productive scoring (no paid AI
calls — the engine only calls Gemini when `GEMINI_API_KEY` is set, and a
missing/failing provider leaves submissions `pending_review`, never
simulated scores).

Still owner-gated before public launch:
- **Item-bank expansion** — the bank has ~6-7 items per band per module;
  the confirmation phase now honestly reports an evidence shortfall when
  a band runs dry (never cross-band substitution). Expand `seed/` via
  `scripts/parse_pdf_to_seed.py`, re-verify manifest checksums.
- **`GEMINI_API_KEY`** — enables automated Writing/Speaking rating
  (audio is sent inline; ratings are audio-grounded). Without it,
  productive results require reviewer action in the queue.
- **Reviewer capacity** — the admin review queue at
  `/v1/admin/placement/review/queue` needs named reviewers for
  `pending_review` sessions.
- **Retention / retest policy** — engine recordings expire on the 90-day
  clock; OET `PlacementResults` history is permanent by design. Retest
  cadence is not limited (each session is a fresh attempt; history keeps
  all of them).
- **Legacy standalone GEPA site cutover** — gepa.polytronx.com stays live
  until an approved data-preserving cutover (pg_dump archive → retire;
  anonymous sessions are not migratable to OET accounts).

## 4. Journey / behaviour notes

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

## 5. Known follow-ups (non-blocking)

- GEPA repo CI is currently red for an **account billing** reason ("job
  was not started — payments have failed or spending limit"); local
  verification passed (34/34 incl. DB-backed). Re-run the workflow after
  fixing billing in GitHub settings.
- Playwright journey for the placement flow can be added on top of
  `lib/api/placement.ts` fixtures (engine has an `E2E_MODE` seeding
  route that now writes real WAV fixtures for speaking).
- The engine's `ruleset_version` surfaces on session create/state and is
  stored with every `PlacementResult` — use it when the ruleset
  recalibrates after field data lands.
