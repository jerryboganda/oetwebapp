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

## Correction to an earlier figure in this document

Pack 2's provider-busy count on `0482ca9bf` was reported here as **9**. The measured value by exact
response text is **11** (`uat-pack2-candidate-0482ca9bf.json`). The "9" came from a partial count
taken while that run was still in flight, and the error was carried forward into later reporting.
Corrected pair, both by exact text: **11 provider-busy → 6** after the length-retry ladder, with real
answers **9 → 14**. Anything quoting "9 → 6" should read "11 → 6".

## UAT status

### Candidate build: `0482ca9bfe86ee57dbe5c97a2e1bb0bee5ef73d8`

Deployed and confirmed serving (`X-Oet-Release` 0482ca9bf, slot blue), `Deploy production` success.
It contains all four of this run's slices — verified by ancestry check, not assumed. This is the
frozen candidate for the UAT packs; every record below carries this SHA in its `build` field.

| Pack | State |
| --- | --- |
| Pack 1 | **20/20 real responses** on the candidate — `uat-pack1-candidate-0482ca9bf.json`. 0 cap refusals (was 16 of 20 on `8cd4ad338`), 0 empty, 0 provider-busy, 10 turns performed real tool actions. Baseline comparison: `uat-pack1-live.json`. |
| Pack 2 | `uat-pack2-candidate-0482ca9bf.json` — 20/20 records captured, but **9 of 20 returned the provider-busy message** (Tests 01, 02, 03, 05, 06, 07, 13, 17, 19) and **one turn stalled** (Test 10). See D-SAMI-003 below. Tool-driven turns (08, 14, 15, 16, 18) succeeded and are substantive. |
| Pack 3 | Not yet run on the candidate. |
| Pack 4 | Not yet run on the candidate (browser-dependent scenarios outstanding). |

## Open defects on the candidate build

**D-SAMI-005 — seeded learner-facing documentation still describes deleted CI.**
Two stale claims were found in the in-app product documentation and **fixed** on 2026-10-08:
`Doc12QaValidation.cs` asserted the deploy pipeline still runs `writing-model-answer-gate`
(`dotnet test --filter WritingRev8ModelAnswerGateTests`) as a hard release gate — it does not, that
job was removed with the tests; and `Doc06LearningCompanion.cs` cited the deleted isolation suites
in the present tense as live proof. Both now state the historical position.

**Still open (deliberate):** `Doc10PlatformArchitecture.cs` references `qa-smoke.yml` and
`performance.yml` in five places as current CI, and `Doc12QaValidation.cs` still lists historical
test-file paths in its evidence blocks. Correcting these is a content edit across several seeded
documents plus a re-seed of the runtime `DocumentationVersions` rows — it belongs in one deliberate
documentation-refresh pass, not piecemeal.

**D-SAMI-003 — long single-shot reasoning turns are refused as "provider busy". PARTIALLY FIXED.**
Root cause proven from live logs, not inferred: `OpenCode reached its output limit. The incomplete
response was not replayed` (`OpenCodeStreamingCall.cs:164`). At `reasoning_effort=max` the model's
reasoning can consume the entire output budget and return `finish_reason=length` with no answer.
`AiProviderRequest.MaxTokens`' doc comment claimed a "length-retry ladder" existed in
`RegistryBackedProvider`; **it did not** — the budget was set once and both output paths threw. The
ladder was implemented in `a9a9fcc07` (one raise, OpenCode only, truncation only).

Measured effect on Pack 2 (`0482ca9bf` → `a9a9fcc07`): real answers 9/20 → **14/20**, provider-busy
11 → **6**, tool-performing turns 8 → **13**. Busy-turn latency rose 105 s → ~230 s, which proves the
retry now actually runs.

**Still open:** the widened log window shows `OpenCode reached its output limit` **5 times after** the
retry, i.e. 32_768 tokens is still not enough for the heaviest turns (notably Test 01, the diagnostic
study plan). The remaining ceiling is a genuine budget limit, not a bug. Nothing further should be
changed here without an owner decision, because the options trade differently:
raise the ceiling again (more latency, unknown end), or lower `reasoning_effort` for this feature.
Relevant context for that decision: D-005's benchmark gate measured schema validity, grounding and
cost reduction — **it never measured task-completion rate**, which is how effort=max shipped into a
configuration that truncates its own hardest turns.

**D-SAMI-004 — a completed answer can be persisted without the completion reaching the client.**
Pack 2 Test 10 on `0482ca9bf`: the server stored a full 997-character answer at 05:42:07 while the
client never received `MessageComplete` and the harness sat on the turn. Diagnosed, **not
root-caused**, and no speculative fix shipped. Note the run on `a9a9fcc07` did **not** reproduce it
(Test 10 returned in 41 s with 6 tool calls), so it is intermittent. Diagnostic direction: compare the
persisted assistant row against delivered events for one affected `threadId`, and check whether the
output-side leak screen replaced the answer or ended the turn without a `TurnError` reaching the
client.
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
