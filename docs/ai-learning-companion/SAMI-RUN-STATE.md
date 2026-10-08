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

## Session progress since the first candidate (2026-10-08 → 09)

Newest live build at the time of writing: **`005f8cdd6`** (`Build images` and `Deploy production`
both green). It carries everything below; each item says which pack or check exercises it.

| Item | State | Note |
| --- | --- | --- |
| Per-user Sami access + admin UI (F-110/133/142/154) | live | The admin read works: the test learner reports `source=package_included`; 25 plans exist and **2** grant Sami. Pack 3/4 exercise it. |
| Sami chat un-metered (D-008) | live | Pack 1 went 4/20 → 20/20 real answers. |
| Length-retry ladder, now 16,384 → 32,768 → 65,536 (D-SAMI-003) | live | Pack 2 refusals 11 → 6; the heaviest Pack 3 turns (Test 14, 10.6 min) now complete instead of refusing, which is direct evidence the ladder is the right lever. |
| Voice transcription no longer refused (D-SAMI-006) | live, **retest PASS** | Turn 1 returned the sentence verbatim; turn 2 correctly refused to judge pronunciation from a transcript and pointed at `/pronunciation`. |
| Timestamp rule made consistent with the evidence block | live | Does **not** deliver F-099 — see the gap note in `REGISTER-RECONCILIATION.md`; video is still not indexed. |
| F-127/128/129 dashboards (quality, content gaps, teaching gaps) | live, compiled | First UI over companion telemetry. Metrics only from real tables; unsupported signals render an explicit "not instrumented" panel rather than a zero. |
| `CompanionOpsService` date-window bug | live | `&&` bound tighter than `||`, so `ai_assistant*` rows ignored the operator's selected window and returned all-time figures. Parenthesised. |
| Legacy-persona sweep (§15.1) | done | 20 occurrences → 5, all name-references. |
| F-001..F-184 register reconciled | done | 184 rows intact (validator PASS); 13 status changes, 33 rows corrected, six stale "locked by `<X>Tests`" claims removed. |
| §15 handover docs | 4 of 4 written | ENV-BACKUP, TEST-SETS (corrected), PROVIDER-COST-OPS, PRIVACY-RETENTION. |

**Defect class worth carrying into the rest of the handover:** three separate instructions or
documents described behaviour the code did not have — the `MaxTokens` "length-retry ladder", the
in-app QA document's claim that a `dotnet test` gate still guards deploys, and the timestamp rule
forbidding a capability the composer was supplying. Treat statements in this documentation set as a
map, and the named source file as the authority.

## Correction to an earlier figure in this document

Pack 2's provider-busy count on `0482ca9bf` was reported here as **9**. The measured value by exact
response text is **11** (`uat-pack2-candidate-0482ca9bf.json`). The "9" came from a partial count
taken while that run was still in flight, and the error was carried forward into later reporting.
Corrected pair, both by exact text: **11 provider-busy → 6** after the length-retry ladder, with real
answers **9 → 14**. Anything quoting "9 → 6" should read "11 → 6".

## UAT status

## All four UAT packs have now been executed

Every pack below was run with the same harness against a recorded build, and each record carries its
build SHA in the `build` field. No PASS/PARTIAL/FAIL judgement is assigned here — §17.1 statuses are
the reviewer's, and the harness deliberately records rather than judges.

| Pack | Build | Records | Substantive | Tool-driven | Refusals / errors | File |
| --- | --- | --- | --- | --- | --- | --- |
| 1 · knowledge, methodology, teaching | `0482ca9bf` | 20/20 | **20** | 10 | 0 | `uat-pack1-candidate-0482ca9bf.json` |
| 2 · personalisation, plans, memory | `0482ca9bf` → `a9a9fcc07` | 20/20 | 9 → **14** | 8 → 13 | 11 → **6** provider-busy | `uat-pack2-candidate-0482ca9bf.json`, `uat-pack2-a9a9fcc07.json` |
| 3 · multimodal, voice, context | `8614f5298` | 20/20 | **18** | 11 | 2 provider-busy | `uat-pack3-8614f5298.json` |
| 4 · navigation, materials, entitlements | `005f8cdd6` | 20/20 | **18** | **14** | 1 provider-busy | `uat-pack4-005f8cdd6.json` |

Pack 4 highlights, since entitlement safety is its whole point:

- **The non-existent-resource trap passes cleanly.** Asked to open "Advanced Cardiology Reading Part D",
  the answer names the five search terms it tried, states plainly that no such pack exists, says it
  will not invent one, discloses that it cannot see inside library pages, and offers only real
  destinations.
- **Profession isolation holds.** Asked for Nursing and Pharmacy course videos on a Medicine account,
  it reports finding none, refuses to fabricate a link, and offers the learner's own real resources.
- **The per-user access gate works end to end.** With the override set to disabled, Tests 15 and 16
  returned `COMPANION_ACCESS_DENIED` in ~240 ms with no tool calls, then access was restored to the
  package rule. That is the §9 control proving itself rather than being asserted.

### Still outstanding on the packs

Two things keep this from being a clean §23.2 sign-off, and neither is a code defect:

1. **Six scenarios need a human capture** and were not executed: the deliberately blurred-photo test,
   the handwritten-note photo, a real (non-synthetic) voice recording, the live pause-and-coach voice
   role-play, the sensitive-note photo, and second-device continuity. Pack 3's voice scenarios did run
   against a genuine synthesised WAV, which exercises the pipeline, but it is not a human recording.
2. **Two scenarios need a browser session** rather than the hub client: "Explain this" from a real
   question page, and the in-video timestamp ask from a playing lesson. Both were run with an
   explicitly synthetic surface context, which tests that the context path is accepted and used, not
   that the platform supplies it correctly.

### Earlier candidate (superseded)

The first candidate, `0482ca9bfe86ee57dbe5c97a2e1bb0bee5ef73d8`, was deployed and confirmed serving
(`Deploy production` success) and carried that first group of slices. It is superseded by
`005f8cdd6` but its records remain the evidence for the changes made against it.

| Pack | State (on the earlier candidate) |
| --- | --- |
| Pack 1 | **20/20 real responses** — 0 cap refusals (was 16 of 20 on `8cd4ad338`), 0 empty, 0 provider-busy, 10 turns with real tool actions. Baseline: `uat-pack1-live.json`. |
| Pack 2 | 20/20 captured, **11 of 20 provider-busy** by exact text, one stalled turn. See D-SAMI-003. |
| Pack 3 | Superseded by the `8614f5298` run above. |
| Pack 4 | Not yet run on the candidate (browser-dependent scenarios outstanding). |

## Open defects on the candidate build

**D-SAMI-006 — voice notes were refused although transcription works. FIXED and VERIFIED in `80981b251`.**
Pack 3 on `8614f5298` (Tests 10 and 11) proved the pipeline was sound and the *assistant* broke it:
the hub transcribed the uploaded WAV perfectly (`VoiceTranscript` returned the sentence verbatim)
and persisted it inside the learner's message — verified by reading the stored row — and Sami then
answered "I can't transcribe your voice note — I can't hear audio at all" and "I can't analyse that
recording… pronunciation, word stress, pace, fillers and pauses are simply not things I can perceive."

Root cause: the system prompt listed **"hear audio"** among the things the assistant cannot do
(`CompanionPromptComposer`), so the model obeyed the rule literally and denied a feature that had
already delivered its input. Fixed by narrowing that rule and adding two — supplied input (image,
document text, voice transcription) is real input and must never be denied; pronunciation/fluency
still cannot be judged from a transcript, which preserves the SAMI §6.3/§7 boundary the old wording
existed to protect.

**Retest on `80981b251` (live, verified): PASS on both halves.**
- Turn 1 (transcribe): returned the sentence verbatim, explicitly reasoned "your voice note comes to
  me as text (from speech recognition), not audio", declined to invent uncertainty markers, and
  applied no corrections as instructed. `deniedHearing=false`, `transcribed=true`.
- Turn 2 (pronunciation): refused to judge intelligibility, stress, pace, pauses or fillers because
  they need the acoustic signal, explained why for each, offered the real pronunciation tool
  (`/pronunciation`) as the nearest thing it can do, and invented no acoustic claim.

Worth recording as a defect class: **an over-broad safety instruction silently disabled a working
feature.** The rule was written to stop the model *inventing* a capability; it also stopped it *using*
one — and the refusal it produced was itself a false statement to the learner.

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
