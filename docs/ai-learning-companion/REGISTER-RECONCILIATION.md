# Register Reconciliation — SAMI feature traceability F-001..F-184

**Date:** 2026-10-08 · **Branch:** `main` · **Artefacts:** `traceability/features.json`, `traceability/features.csv`, `traceability/FEATURE_TRACEABILITY_MATRIX.md`, this file.

The register was frozen at its Wave 2 (2026-10-07) values and no longer described the shipped product. It has been reconciled against the acceptance baseline and the actual repository state. **The status vocabulary is unchanged** (`EXISTS` / `PARTIAL` / `MISSING` / `DEFERRED_BY_SOURCE`) because `scripts/ai-learning-companion/validate_traceability.py` and the CSV consume it; only values, evidence and notes changed.

## 1. Authority

- Acceptance baseline: `source/SAMI_FINAL_PRODUCTION_HANDOVER_1.0_FINAL.md` (D-002). §16 is the feature inventory; §1.1/§1.2 are the delivery gate and scope boundary.
- Owner decisions: `DECISION_LOG.md` D-001..D-007 plus **D-008** (Sami access model) recorded in `SAMI-RUN-STATE.md`. D-006 puts all 184 features in the program (Wave 6 for expansion/B2B); D-008 removes the tier ladder, message cap and candidate-facing wallet from Sami chat.
- Shipped state: `PROGRAM-STATUS.md` (waves) and `SAMI-RUN-STATE.md` (this push, gate status, open defects).
- UAT records: `uat/results/*.json` and the consolidated `uat/results/UAT-REGISTER.md`.

## 2. Method and evidence rules

- Every status change cites a file, migration id, commit or UAT record. Nothing was upgraded on inference; where evidence was missing the old value was kept. Two rows carry a "verified but deliberately not upgraded" finding rather than a new status (F-140, F-147).
- `TO VERIFY` values were left as `TO VERIFY` — none was invented (see §11).
- `estimate_days` remains `TO VERIFY - developer after repository audit` on every row: no effort estimate was invented.
- `repo_evidence` / `repo_gap` were rewritten where they named files that do not exist, described code that has changed, or cited test suites that were deleted on 2026-10-08 (owner directive).
- Only these four files were modified. No build, test, lint or typecheck command was run: compilation is not part of this task and there is no automated QA in this repo. The one command executed was the repository's own stdlib-only validator, `python scripts/ai-learning-companion/validate_traceability.py` (see §12).

## 3. Status counts before → after

| Status | Before | After | Delta |
|---|---|---|---|
| EXISTS | 78 | 80 | +2 |
| PARTIAL | 78 | 66 | -12 |
| MISSING | 25 | 35 | +10 |
| DEFERRED_BY_SOURCE | 3 | 3 | +0 |
| **Total** | **184** | **184** | 0 |

13 features changed status. 33 more rows kept their status but had notes, evidence or gaps corrected (including the six deleted-test corrections and the owner dispositions).

## 4. Status changes with evidence

| Feature | Requirement | Before → After | Evidence |
|---|---|---|---|
| F-048 | Quick answer | PARTIAL → EXISTS | Brief/Standard/Deep depth preference persisted, exposed in the preferences panel and applied by CompanionPromptComposer:414-419. |
| F-049 | Detailed tutor | PARTIAL → EXISTS | Deep depth preference persisted and applied by CompanionPromptComposer:414-419; grounded composer + citations verified. |
| F-054 | Arabic explanation | PARTIAL → EXISTS | CompanionPromptComposer Arabic/code-switch branches (:426-438) + ar message bundle + UAT Pack 1 Test 16 Arabic answer on 0482ca9bf. |
| F-135 | Free message cap | EXISTS → MISSING | No message cap exists; quota reserve skipped for Sami feature codes (AiAssistantGateway.cs:132,685-687) and cap reasons never emitted (CompanionAccessResolver.cs:198-214). |
| F-168 | IELTS pack | PARTIAL → MISSING | Only an IELTS writing/scoring engine + guide page exist; no pack (Services/IeltsMockEngine.cs, LearnerEndpoints.cs:737-749, app/(learner)/ielts-guide). |
| F-169 | PTE pack | PARTIAL → MISSING | Only Services/PteScoring.cs; no PTE pack. |
| F-171 | Future exam packs | PARTIAL → MISSING | Exam axis exists (ExamFamilyCode/ExamTypeCode); no pack mechanism. |
| F-177 | Institution roles | PARTIAL → MISSING | Sponsor role only; no institution role hierarchy (app/sponsor, SponsorService). |
| F-178 | Seat/usage budgets | PARTIAL → MISSING | SponsorSeatPackService seat packs only; no institution budget/cap. |
| F-179 | Institution analytics | PARTIAL → MISSING | Sponsor-portal reporting only; no institution analytics. |
| F-181 | API/webhooks | PARTIAL → MISSING | Inbound provider webhooks + admin retry only; no partner API or outbound webhooks. |
| F-183 | Custom retention | PARTIAL → MISSING | Global retention workers only; no per-contract retention. |
| F-184 | Enterprise audit/support | PARTIAL → MISSING | Platform audit/support primitives only; no enterprise export or contracted support tier. |

## 5. Rows whose status did **not** change but whose evidence/notes were corrected

| Feature | Requirement | What was corrected |
|---|---|---|
| F-002 | Registered Free mode | Free tier unchanged; companion codes now sit in the free plan's meter list (SeedData.AiQuota.cs:38-47) and no longer gate Sami chat (CompanionAccessResolver.cs:198-214). |
| F-008 | Screenshot score import | Confirm-before-save path verified in CompanionMemoryTools (record → confirm); gap corrected from the stale "no confirm-before-save step". |
| F-009 | Study availability | CompanionAvailability model (Domain/CompanionMemoryEntities.cs:171-198) + companion_set_availability tool; stale "no shift/travel model" gap replaced. |
| F-012 | Multiple historical exam journeys | CompanionJourney entity + start_journey tool; UAT-REGISTER.md Pack 2 Test 20 created round 3 with round 2 preserved. |
| F-023 | Platform navigation map | Deleted-test correction: CompanionDestinationSecurityTests no longer exists (no backend/tests or tests/ directory). |
| F-033 | 30/60/90-day plans | Template week ranges re-read from StudyPlanTemplateSeeder/SamiPlanTemplateSeeder + StudyPlanTemplateSelector. |
| F-034 | 14/7/3-day plans | tmpl-sami-final-3d / -emergency-7d / -intensive-14d / -exam-eve verified in SamiPlanTemplateSeeder.cs:59-375; stale MinWeeks=2 gap removed. |
| F-035 | Exam-eve plan | tmpl-sami-exam-eve template verified (SamiPlanTemplateSeeder.cs:115-162); stale "no exam-eve template" evidence replaced. |
| F-037 | Shift-worker planning | Night/long-shift fields + StudyPlanAvailabilityShaper behaviour verified (:17-111); stale "no shift model" gap replaced. |
| F-038 | Travel-aware replanning | Travel fields + shaper travel branch (:66-69) verified; stale "no travel model" gap replaced. |
| F-041 | Conversation memory | Empty-completion guard + iteration-exhaustion persistence in AiAssistantOrchestrator (commit 8cd4ad338). |
| F-043 | Journey memory | CompanionJourneyService + CompanionJourney entity (migration 20261007090000). |
| F-047 | Memory controls/export/delete | Deleted-test correction: CompanionMemoryIsolationTests removed; real evidence is CompanionLearnerEndpoints + CompanionMemoryPanel. |
| F-086 | Image/screenshot understanding | Image path proven end to end (D-SAMI-002 in SAMI-RUN-STATE.md; UAT Pack 3 unreadable-input refusals). |
| F-087 | PDF understanding | Document-in-prompt path verified (case-notes extraction, UAT Pack 3 Test 03); no per-PDF charge surfaced. |
| F-090 | Score report extraction | Score-report PNG extraction with confirmation gate recorded in SAMI-RUN-STATE.md (D-SAMI-002); confirm-before-save verified in CompanionMemoryTools. |
| F-097 | Cross-device conversation continuity | In-flight-turn abandonment on socket drop in hooks/use-ai-assistant.ts (commit 8cd4ad338). |
| F-110 | Show allowances/credits | No credit chip on the Sami chat surface; allowance surfaces remain for assessments (app/(learner)/page.tsx:143-144, lib/api/companion.ts:65). |
| F-133 | Entitlement management | Per-user Sami access: CompanionAccessAdminEndpoints (GET/POST/DELETE users/{userId}, audited) + CompanionUserAccess + migration 20270116090000 + admin UI page (commit 60bec4831). |
| F-136 | Plus tier | companion-plus quota policy still seeded (SeedData.AiQuota.cs:103-160); disposition SUPERSEDED/NOT APPLICABLE (D-008). |
| F-137 | Pro tier | companion-pro quota policy still seeded (SeedData.AiQuota.cs:103-160); disposition SUPERSEDED/NOT APPLICABLE (D-008). |
| F-138 | Ultimate tier | companion-ultimate quota policy still seeded (SeedData.AiQuota.cs:103-160); disposition SUPERSEDED/NOT APPLICABLE (D-008). |
| F-139 | AI Credits | Credit ledger exists and is assessment-scoped: ShouldDebitAiCredit (AiAssistantGateway.cs:659) + reserve skipped for Sami codes; disposition RETAINED-for-assessments (D-008). |
| F-140 | Top-up packs | pkg_* credit add-ons verified in the seed catalog; PDF §10.3 top-ups are live-config only (not verifiable here) — TO VERIFY. |
| F-141 | Usage counter | Usage counters exist for assessments; no Sami-chat counter (by disposition). |
| F-142 | Contextual paywall | Package-entitlement gate + upgrade card: CompanionAccessResolver, session payload access.* (CompanionLearnerEndpoints.cs:103-154), orchestrator turn gate, app/(learner)/companion/page.tsx. |
| F-147 | Optional annual billing | Inspected the seed catalog (v1.1.1): no recurring/annual SKU exists — annual support is live-config only, left TO VERIFY in the row. |
| F-149 | Fair-use/rate limits | AiInteractive on /v1/ai-assistant (the chat turn path); PerUser on /v1/companion. |
| F-153 | Anti-hallucination behavior | Thread-title leak fix (commit 8614f5298) + output-side leak screen (AiAssistantOrchestrator.cs:433-478). |
| F-154 | Entitlement-safe retrieval | Deleted-test correction: CompanionRetrievalSecurityTests removed; evidence is CompanionRetriever prefilter + orchestrator turn gate + leak screen. |
| F-155 | Academic integrity controls | Deleted-test correction: CompanionExamModeTests removed; evidence is CompanionContextResolver.ResolveExamModeAsync (client value discarded, :113-115). |
| F-160 | Accessibility - WCAG 2.2 AA across learner-facing web, web-app and native-app surfaces | Deleted-test correction: tests/a11y and tests/e2e removed; evidence is contexts/accessibility-context.tsx + app/providers.tsx + settings/reading surfaces. |
| F-174 | B2B multi-tenancy | Deleted-test correction: CompanionMultiLearnerIsolationTests removed; no tenancy boundary exists (out of beta scope). |

### 5.1 Owner dispositions (recorded verbatim in substance, not softened)

| Feature | Disposition |
|---|---|
| F-135 | **NOT APPLICABLE** to the final Sami model. Status corrected `EXISTS` → `MISSING`: there is no message cap and, since commit `7988b7596`, no token cap on the Sami chat path either (`AiAssistantGateway.cs:132,685-687`; `CompanionAccessResolver.cs:198-214`). |
| F-136 / F-137 / F-138 | **SUPERSEDED / NOT APPLICABLE** for the Sami launch. The `companion-plus` / `companion-pro` / `companion-ultimate` quota policies remain seeded but are dormant; status stays `PARTIAL` because those artefacts genuinely exist — the rows are not Sami launch dependencies and not acceptance blockers. |
| F-139 / F-140 / F-141 | **RETAINED only where the separate assessment products (Writing, Speaking, Reading, Listening grading) require them; NOT Sami chat features.** Statuses stay `EXISTS`; the notes say so explicitly, and F-141 records that no counter is shown on the Sami surface. |
| F-142 | **Replaced by the package-entitlement / eligible-course upgrade path.** Status stays `EXISTS`; evidence now points at `CompanionAccessResolver`, the session payload's `access.*` and the upgrade card in `app/(learner)/companion/page.tsx`. |

## 6. This session's shipped work, as recorded

| Work | Where it is recorded | Feature rows touched |
|---|---|---|
| D-SAMI-001 — turn abandonment on socket drop + empty-completion guard (`8cd4ad338`) | `hooks/use-ai-assistant.ts:161,187-216,350-378`; `AiAssistantOrchestrator.cs:304-315,394-421` | F-041, F-048, F-049, F-097 |
| Sami chat no longer metered (`7988b7596`) | `AiAssistantGateway.cs:132,685-687`; `CompanionAccessResolver.cs:198-214`; no cap reasons in `CompanionLearnerEndpoints` | F-002, F-110, F-135, F-139…F-142, F-149 |
| Per-user Sami access with provenance (`60bec4831`) | `Services/Companion/CompanionAccessResolver.cs`, `Domain/CompanionUserAccess.cs`, migration `20270116090000_CompanionUserAccess`, `Endpoints/CompanionAccessAdminEndpoints.cs`, `app/admin/companion/access/page.tsx`, learner gate in `Endpoints/CompanionLearnerEndpoints.cs:88-155`, orchestrator turn gate `AiAssistantOrchestrator.cs:133-156,646-690` | F-110, F-133, F-142, F-154 |
| Length-retry ladder 16,384 → 32,768 → 65,536 (`a9a9fcc07`, completed by `8614f5298`) + thread-title leak fix | `Services/Rulebook/AiProviderRegistry.cs:149-162,391-397`; `AiAssistantOrchestrator.cs:423-428,480-486` | F-048, F-049, F-153 |
| Legacy-persona sweep (§15.1, `b8a2d966f`) | Repo-wide text grep for the legacy name returns only four documentation references (the superseded source PDF's filename and the decision/start-here records that mark it superseded). No user-facing copy, prompt, seed or fixture. | program-level gate, not a feature row |

## 7. F-168..F-184 — expansion / B2B: not delivered for this beta

SAMI PDF §16 marks all seventeen FUTURE-architecture-ready and §1.2 makes them not controlled-OET-beta blockers; D-006 nevertheless moved them into the delivery program as Wave 6. They are recorded as **not delivered**. Nine rows that previously read `PARTIAL` were corrected to `MISSING` because the cited artefacts are groundwork, not the feature: F-168, F-169, F-171, F-177, F-178, F-179, F-181, F-183, F-184. Their `repo_evidence` now names that groundwork explicitly, and their `repo_stage` reads `Wave 6 — not delivered`.

## 8. Still MISSING and in scope for the OET beta (acceptance blockers)

In scope = SAMI PDF §16 disposition REQUIRED (not FUTURE, not POST-BETA, not EQUIVALENT ACCEPTABLE). `F-135` is in scope by §16 but dispositioned NOT APPLICABLE by the owner, so it is listed separately.

| Feature | Requirement | Why it is still missing |
|---|---|---|
| F-015 | Writing workshops | Requires the Stage 2 content-ops pipeline: register, extract, tag, approve, release. |
| F-016 | Speaking workshops | Requires the Stage 2 content-ops pipeline: register, extract, tag, approve, release. |
| F-017 | Correction sessions | Requires the Stage 2 content-ops pipeline: register, extract, tag, approve, release. |
| F-045 | Learning Fingerprint | Stage 3 per source; requires F-042 and F-044 first. |
| F-076 | Final 24-hours mode | Depends on short-horizon plans (F-034/F-035). |
| F-077 | Result-day assistant | No result-day assistant creating a success or resit path. |
| F-083 | Personal bests | No personal-best or milestone record. |
| F-085 | Weekly progress report | No weekly wins/risks/priorities report. |
| F-089 | Handwritten note understanding | Provider-dependent; Stage 2 at the earliest. |
| F-099 | Open exact timestamp | No video destination accepts a timestamp yet — depends on Stage 2 video corpus ingestion. Deliberately not faked: a timestamp link that lands at 0:00 is worse than no link. |
| F-106 | Open workshop | Blocked on the workshop model. |
| F-108 | Tutor handoff summary | Needs score trend, errors, adherence and recommended focus. |
| F-112 | Resume chat after purchase | No pending-action resume after checkout completes. |
| F-123 | Tutor pre-session brief | No pre-session brief with trend, errors and adherence. |
| F-127 | Hallucination/low-confidence queue | A hallucination queue and content/teaching-gap dashboards need companion telemetry first. |
| F-128 | Content-gap dashboard | A hallucination queue and content/teaching-gap dashboards need companion telemetry first. |
| F-129 | Teaching-gap dashboard | A hallucination queue and content/teaching-gap dashboards need companion telemetry first. |

**Dispositioned NOT APPLICABLE (in scope by §16, removed from the Sami model by the owner):** F-135.

## 9. Out-of-scope MISSING / deferred rows

| Feature | Requirement | Disposition |
|---|---|---|
| F-168 | IELTS pack | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-169 | PTE pack | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-170 | TOEFL pack | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-171 | Future exam packs | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-172 | Exam-version engine | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-173 | Cross-exam skill transfer | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-174 | B2B multi-tenancy | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-175 | White-label persona | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-176 | Institution knowledge upload | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-177 | Institution roles | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-178 | Seat/usage budgets | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-179 | Institution analytics | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-180 | SSO | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-181 | API/webhooks | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-182 | LMS integration | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-183 | Custom retention | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-184 | Enterprise audit/support | FUTURE (SAMI PDF §16) → Wave 6 by D-006; not a beta blocker |
| F-057 | Micro-lessons | DEFERRED_BY_SOURCE (kept) — Appendix places micro-lessons at Stage 4-5 (DEC-004). Kept in backlog, not dropped. |
| F-084 | Anonymous cohort benchmarking | DEFERRED_BY_SOURCE (kept) — SAMI PDF §16: POST-BETA for controlled beta; still built per D-006, sequenced after beta scope (Wave 6). |
| F-121 | Adaptive gamification | DEFERRED_BY_SOURCE (kept) — Appendix A visibly contains F-113 but the extracted matrix omits rows F-114 through F-122; preserve these features and require owner/phase sign-off. Section 46 also makes non-outcome gamification and fully proactive autonomy non-goals for the first 12 months. / SAMI PDF §16: POST-BETA for controlled beta; still built per D-006, sequenced after beta scope (Wave 6). |

## 10. Delivery-gate and UAT reality (not a feature row)

- **The §1.1 gate is not yet met.** All 80 UAT scenarios are not executed and evidenced. `SAMI-RUN-STATE.md`'s UAT table records Pack 1 and Pack 2 as run on the frozen candidate build (`0482ca9bf`) and Pack 3 and Pack 4 as "Not yet run on the candidate"; the consolidated `uat/results/UAT-REGISTER.md` is drawn from the earlier runs and ends with "Mechanical triage: 54 records, 17 flagged for attention". No §17.1 status has been assigned by a reviewer.
- Measured, from the records in `uat/results/`:
  - `uat-pack1-candidate-0482ca9bf.json` — 20/20 records on build `0482ca9bfe86ee57dbe5c97a2e1bb0bee5ef73d8`, 10 turns performed tool actions, **0 cap refusals, 0 empty responses**. The pre-fix baseline `uat-pack1-live.json` (build `c47c016a9945061af309ab6875068a7b490ced90`) contains 16 cap refusals.
  - `uat-pack2-candidate-0482ca9bf.json` — 20 records, 11 returned the provider-busy message (Tests 01, 02, 03, 04, 05, 06, 07, 13, 17, 19, 20 by exact response text), 8 turns performed tool actions. `SAMI-RUN-STATE.md` records 9 for this file; the count from the file itself is 11 — recorded here rather than smoothed over.
  - `uat-pack2-a9a9fcc07.json` (build `a9a9fcc07e63d746d367ba8a24e190920703ffbb`) — 20 records, 6 provider-busy, 13 tool-performing turns.
  - `uat-execution-2026-10-07T20-33-14.json` — Pack 3, 8 records, 5 empty responses (Tests 05, 07, 08, 15, 16): the D-SAMI-001 evidence. `uat-execution-2026-10-07T22-01-33.json` — the retest, 2 empty (Tests 15, 16) + 1 provider-busy (Test 05).
- **Open defects still on the candidate build, carried into the register as gaps, not as passes:**
  - **D-SAMI-003 (partially fixed).** The length-retry ladder is implemented (`AiProviderRegistry.cs:149-162,391-397`: 16,384 → 32,768 → 65,536); the widened log window still shows `OpenCode reached its output limit` five times after the retry, i.e. 32,768 is still not enough for the heaviest turns. The remaining ceiling is a genuine budget limit awaiting an owner decision.
  - **D-SAMI-004 (open, intermittent, not root-caused).** A completed answer can be persisted without the completion reaching the client (Pack 2 Test 10 on `0482ca9bf`; did not reproduce on `a9a9fcc07`).
  - **D-SAMI-001** is fixed at the client/orchestrator level; the Pack 3 Test 15/16 failures in `UAT-REGISTER.md` (WebSocket 1006 / "Cannot send data if the connection is not in the 'Connected' state") are the socket drops that fix addresses, and have not been re-run on the fixed build.
- Features SAMI-RUN-STATE still lists as to-build (therefore left `MISSING`): F-089 handwriting, F-099 exact timestamp, F-106 workshop, F-112 resume-after-purchase, F-123 tutor brief, F-127/F-128/F-129 admin dashboards.

## 11. What could not be verified (stated plainly)

1. **Live production state is out of reach from this checkout.** Anything that lives only in the database — seeded sellable catalog rows, live prices, `DocumentationVersions` content, the production feature-flag rows — is cited as *configuration*, not as proof. F-140's PDF-priced top-up packs and F-147's annual SKUs stay `TO VERIFY` against production: the static seed catalog (`backend/src/OetLearner.Api/Data/Seeds/oet-2026-catalog.json`, v1.1.1) contains **no** recurring or annual SKU and no §10.3 top-up pack — it is 25 one-time course packages plus course/credit add-ons.
2. **No deployment or run-id evidence was checked.** Deploy/run ids live in `VERIFICATION.md` and GitHub; this reconciliation verifies repository state and UAT record files only, so "shipped" here means "present in the working tree on `main` at `3cd9efff2`", not "observed live".
3. **Test-suite evidence is gone by design.** Six rows used to claim a test suite as their lock (`CompanionDestinationSecurityTests`, `CompanionMemoryIsolationTests`, `CompanionRetrievalSecurityTests`, `CompanionExamModeTests`, `CompanionMultiLearnerIsolationTests`, `tests/a11y/**`) — deleted 2026-10-08. They now state the real implementation and say owner-QA only. Note that the API's **seeded documentation content** (`Services/Seeding/DocumentationContent/Doc06LearningCompanion.cs`, `Doc12QaValidation.cs`) still tells learners those test classes exist; that is outside this task's file ownership and is flagged here.
4. **`source_section` was not remapped.** Those values still refer to the superseded master spec's section numbering. `source_decision` was rewritten from SAMI PDF §16 for all 184 rows (the previous text, "UNDECIDED (Accept / Defer / Reject must be signed off)", was true of neither the owner decision D-006 nor §16). Remapping `source_section` would need a section-by-section mapping of the old spec that this task did not have.
5. **Rows left untouched.** Features whose evidence I could not re-verify were left exactly as they were; `PARTIAL` in those rows still means "partially implemented per the last audit", not "verified today".
6. **UAT status is not assigned here.** §17.1 judgement belongs to the reviewer; the UAT records carry no per-test status field, so §10 reports flags and measured counts only.

## 12. Verification of the register itself

`python scripts/ai-learning-companion/validate_traceability.py` (stdlib only, no build step) — result recorded after the rewrite: **PASS** — F-001 through F-184 present exactly once in the JSON inventory, the CSV inventory and the Markdown matrix, in order, with `feature_count = 184`.

## 13. Files changed by this reconciliation

| File | Change |
|---|---|
| `traceability/features.json` | statuses, notes, `repo_evidence`, `repo_gap`, `repo_stage`, `source_decision`, top-level `source` |
| `traceability/features.csv` | regenerated from the JSON, same 16 columns and row order |
| `traceability/FEATURE_TRACEABILITY_MATRIX.md` | regenerated with Status/Evidence columns so it can no longer contradict the JSON |
| `REGISTER-RECONCILIATION.md` | this document (new) |

---

## 14. Owner decisions of 9 October 2026 (post-reconciliation)

Recorded here because they change dispositions. These supersede the corresponding rows above.

### 14.1 F-099 / F-094 — video library stays OUT OF SCOPE (owner decision)

**Decision:** the video library is confirmed out of scope for the OET beta, so there is no video
corpus for a timestamp to point at.

**Effect.** F-099 (open exact timestamp) remains **NOT DELIVERED** and is now formally **out of
scope rather than pending** — it is not an acceptance blocker and must not be reported as an
incomplete deliverable. The same applies to the video half of F-094 (timestamp-aware help).

**What was nevertheless fixed, and stays fixed.** The prompt rule that previously told the model
flatly that video lessons "are not indexed by timestamp" and to "never quote or guess a time
position" was internally inconsistent: the composer already prints `timestamp: {n}s` for any
retrieved chunk that carries one, and citations already expose `timestampSeconds`. It now forbids
only what is actually unsafe — inventing a position, or implying one exists when the evidence
carries none. That is correct behaviour for **any** timestamped source, video or not, and needs no
further work while the video library stays out. It changes no disposition.

### 14.2 F-045 / F-070 — instrumentation and feature APPROVED (owner decision)

The owner approved building both the missing instrumentation and the feature.

### 14.3 Correction: answer-change and timing data ALREADY EXIST

The earlier reconciliation assessed F-045/F-070 as unbuildable because "no answer-change or
per-question confidence instrumentation exists". That was **wrong on three of the four signals**,
and the error mattered because it argued against work that was in fact possible:

| Signal | Earlier assessment | Actual |
| --- | --- | --- |
| Answer changing | absent | **already recorded** — `ReadingAnswerRevision` stores one row per change (`ReadingEntities.cs:1061`) |
| Per-question timing | absent | **already recorded** — `ReadingAnswer.ElapsedMs` |
| Review flagging | absent | **already recorded** — `ReadingAnswer.FlaggedForReview` |
| Distractor pattern | absent | **already recorded** — `ReadingAnswer.SelectedDistractorCategory`, `MissReason` |
| Per-question **confidence** | absent | absent — genuinely the only gap, and it needed owner approval to instrument |

So F-045/F-070 rest on data that mostly already existed. The lesson matches the defect class
recorded in `SAMI-RUN-STATE.md`: an absence claim is itself a finding and deserves the same
evidence as a presence claim before it is used to justify not building something.

### 14.4 Six physical-capture UAT scenarios — owner will run them

The owner will execute the six scenarios that need a human input (degraded photograph, real
handwriting, a genuine voice recording, live pause-and-coach role play, sensitive-data photograph,
second device). Instructions are in `uat/OWNER-CAPTURE-SHEETS.md`. Until those results arrive they
are recorded as **owner-run, not yet executed** — never as passed.

### 14.5 F-015 / F-016 / F-017 and F-076 — owner has taken them on

The owner selected the workshop / correction-session corpus (F-015/016/017) and final-24-hours
mode (F-076) as work to take on. F-015/016/017 additionally need the actual source material —
workshop recordings, transcripts or materials — before anything can be indexed; that request is
open. F-076 overlaps the existing `sami-exam-eve` template and needs a steer on what should
differ from it.

