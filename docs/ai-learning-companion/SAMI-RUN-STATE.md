# SAMI Beta-Gate Run State (this run)

Companion to `PROGRAM-STATUS.md`, scoped to the current push for the SAMI v1.1 FINAL beta gate.
`SESSION_STATE.md` is owned by whichever concurrent session is active on it — this file exists so
parallel SAMI work does not clobber another session's ledger.

Updated: 2026-10-08 · Branch `main` · HEAD `60bec4831`

## Objective

Bring the live platform to the revised SAMI spec and prove it: remove the defects that make Sami
unusable in practice, implement per-user access per §9, strip the candidate-facing wallet from Sami
chat, execute the 80 UAT scenarios on one recorded build, and deliver the §15 handover package with
a truthful F-001..F-184 register.

## Owner decisions recorded (see `DECISION_LOG.md` for the canonical entry)

**D-008 — Sami access model (owner, verbatim substance).** Sami Chat = package entitlement +
per-user admin enable/disable. No tier ladder, no message cap, no visible AI Credit balance, no
chatbot top-up packs, no credit-based paywall, no separate Sami subscription. The separate
Writing / Speaking / Reading / Listening assessment credit system and its billing logic stay
untouched. Existing Plus/Pro/Ultimate and top-up products are not deleted; they stop being offered
as Sami products for new candidates, and any entitlement a real customer already purchased is
preserved until expiry or migrated safely.

**Register reconciliation (owner).** F-135 → NOT APPLICABLE. F-136/137/138 → SUPERSEDED/NOT
APPLICABLE for Sami launch. F-139/140/141 → RETAIN only where the separate assessment products
require them, not for Sami Chat. F-142 → replaced by the package-entitlement / eligible-course
upgrade path. The revised final spec governs where it conflicts with older tier/credit text.

## Shipped and awaiting/verified live

| Commit | What | State |
| --- | --- | --- |
| `8cd4ad338` | D-SAMI-001: abandon in-flight turn on socket drop; empty completion becomes a retryable error | live, verified serving |
| `7988b7596` | Sami chat no longer metered: token-quota reserve skipped for the Sami feature codes; learner gate stops emitting cap refusals; credit chip removed from the Sami surface | pushed, deploy pending |
| `60bec4831` | Per-user Sami access with provenance: `CompanionUserAccess` + migration `20270116090000`, the single `CompanionAccessResolver` gate, orchestrator turn gate, admin GET/POST/DELETE routes with audit, `access.source` on the session payload | committed, push pending the ship lock |

## Evidence-backed findings

- **D-SAMI-002 was NOT a defect.** The live image path works end to end. A synthetic score-report PNG
  returned all four scores (320 / 295 / 350 / 370) plus profession and test date, and honoured the
  confirmation gate; the case-notes document turn returned the correct recipient and purpose. The
  earlier recorded "no file actually reached me" refusals were an artifact of the lost harness that
  never sent `StartTurn`'s attachment arguments. Do not re-investigate this.
- **Root cause of the Pack 1 failure (now fixed).** Sami answered four ordinary turns, then every
  remaining turn returned `Daily AI credits exhausted on plan pro` while the account held an active
  `pro` entitlement with credits remaining. The assistant gateway's token-quota reserve was gating
  Sami chat — precisely the candidate-facing wallet dependency §1.2 and §9 forbid.
- Credit **debiting** was already correctly scoped to assessment features only
  (`ShouldDebitAiCredit`), so chat never consumed the learner's balance. Only the quota reserve blocked.

## UAT status

| Pack | State |
| --- | --- |
| Pack 1 | `uat/results/uat-pack1-live.json` — 20 records. Tests 01-04 are real answers; 05-20 are the cap refusal, so the pack was unrunnable past Test 04. Re-run required on the fixed build. |
| Pack 2 | Definitions ready (`.tools-state/sami-ops/packs/pack2.mjs`). Not yet executed. |
| Pack 3 | Assets rendered to real files (`.tools-state/sami-ops/assets/`): score cards A and A-v2, reading question clean + blurred, handwriting, sensitive-data trap, and the case-notes PDF that never existed, so the whole-PDF scenarios could not previously run as written. |
| Pack 4 | Browser-dependent scenarios outstanding (entitlement display, deep links, OCP/device, contextual upgrade). |

Six scenarios need a human hand and are the owner's: blurred-photo capture, handwritten-note photo,
real voice recording, live pause-and-coach voice role-play, sensitive-note photo, second-device
continuity. Everything else is machine-runnable.

## Harness (local only, gitignored, never committed)

`.tools-state/sami-ops/` — `lib.mjs` (hub client), `run-pack.mjs` (pack runner), `packs/*.mjs`
(verbatim §18-§22 prompts), `make-assets.mjs` (asset renderer), `probe-signin.mjs`,
`selftest.mjs`, `probe-attachments.mjs`, `accounts.local.json`.

## Blockers

- `pnpm run ship` cannot run while a sibling session holds the lock; a background retry loop waits
  for it. A live lock is never force-released.

## Next actions

1. Confirm the queued ship pushes `60bec4831` and `Deploy production` is green; record SHA + slot.
2. Re-run Pack 1 on the fixed build and expect 20 real records.
3. Run Pack 2 and Pack 3 from the rendered assets.
4. Build the UAT-backed missing features: F-123 tutor brief, F-127/128/129 admin dashboards,
   F-089 handwriting, F-099 exact timestamp, F-106 workshop, F-112 resume-after-purchase.
5. Register reconciliation, legacy-persona sweep, §15 handover documents.
