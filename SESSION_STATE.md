# SESSION STATE

Session: writing-ai-final
Goal: Implement the 6 Oct 2026 Writing AI-Final handoff: candidate-facing cleanup, severity/priority rules, secondary reviewer with soft 400+ guardrail, 15-minute release window and the five-account owner allowlist.
Mode: execute
Updated: 2026-10-06T14:40:00Z
Branch: work/2026-10-05
HEAD: 48ea43a39

<!--
The current run's working memory. This is layer 2 of three:
  1. AGENTS.md / .github/instructions/**  permanent rules
  2. SESSION_STATE.md + TASKS.json         this run        <- you are here
  3. VERIFICATION.md / git / Actions runs  objective truth

Rules
- Header keys are required, and `Mode` must be plan | execute | verify | blocked | done.
- The seven H2 sections below are required and the order is load-bearing
  (`pnpm run ax:check` enforces it).
- Never tick a gate without evidence. A `PASS` row needs a run id, a workflow
  file, or `local:<command>`. Record real runs with `pnpm run ax:record`.
- Keep it short. It is working memory, not a history file.
- Two sessions writing this at once? Take the newer `Updated:` block wholesale —
  do not hand-merge. The durable, merge-safe ledger is PROGRESS.md.
-->

## Objective

Implement the owner's 6 Oct 2026 "Writing AI-Final" handoff before remaining-profession testing: no internal identifiers, Revise & Resubmit or RAW label in candidate Writing; advisory/severity/Top-Priority rules; a secondary reviewer (GPT-6.1 Sol / Codex route) with a soft, uncapped 400+ guardrail; a server-anchored 15-minute release window with five permanent owner accounts exempt from the delay and from copy/paste limits. No schema change, no hashed prompt/regression input change.

## Acceptance criteria

- [ ] AC-1 Candidates never see BUILTIN/AI/DH/OW/G-W/OA/R-label/validator/provider tags (server projection + client defence).
- [ ] AC-2 Revise & Resubmit is gone everywhere; Practice this again is a fresh charged attempt; Retry grading never recharges.
- [ ] AC-3 "Criteria score N/38" replaces RAW; advisory never lowers a score or enters Top Priorities; He/She paragraph start is not auto-Major.
- [ ] AC-4 Reviewer runs before publication, soft 400+ enhanced verification, no hard cap, admin-only notes.
- [ ] AC-5 Normal candidates see a 15:00 server-anchored countdown with the exact notice; the five owner accounts are instant with copy/paste.
- [ ] AC-6 Post Submissions persistence, autosave/resume, failover and no double charge unchanged. Owner QA (not tested here) covers Medicine C/C+/B.

## Decisions (do not revisit)

- D-1 No EF migration/column: release time is derived from SubmittedAt+15 min at read time; reviewer state rides in ProviderResultJson, FeatureRecordJson and AuditEvent.
- D-2 Hashed regression inputs (CandidateGradingRules, DescriptorEngine, rulebooks, numbered AiGatewayService lines) and WritingRuleEngine.ValidatorVersion are untouched; every rule-engine relaxation is guarded by !IsModelAnswer so the Model Answer gate is unaffected.
- D-3 Reviewer route is writing-codex-sub gpt-6.1-sol via IAiGatewayService under new feature code writing.grade.review; Max is never touched. Mode: shadow flag => Shadow; writing_ai_reviewer row Enabled=false => Off; else Enforce when the Codex row is active; else Off. Enforce HOLDS on outage (retryable, existing 2/5/15/30 back-off) and never publishes unreviewed.
- D-4 Reviewer changes are applied by a pure deterministic applier: finding-justified criterion deltas, capped /500 moves, corridor only for reviewer-changed scores, unjustified /500 opinions rejected below 400, 400+ recalibrated never clipped.
- D-5 Learner canon pages stay (ids hidden in visible text); revise endpoint is a hard 409 stub; wire keys of neutralised DTO fields stay for one release.
- D-6 lib/catalog-website-packages.ts (another session's file) still says "instant" for Writing packages; left untouched and reported.

## Touched files

| Path | Change |
| --- | --- |
| backend/src/OetLearner.Api/Services/Writing/** (Review/*, WritingCandidateText, WritingCandidateSeverityPolicy, WritingResultRelease, pipeline, digest, builder, mapper, release-gated services) | reviewer layer, sanitiser, severity policy, release window, revise removal |
| backend/src/OetLearner.Api/Services/Rulebook/WritingRuleEngine*.cs, WritingRuleProvenance.cs, AiGatewayService.cs | candidate-lane false-positive fixes, TryGet, guardrail wording, ReviewWriting mode |
| backend/src/OetLearner.Api/{Contracts,Endpoints,Hubs,Domain,Services}/** (AI feature-code registries, auth flag, free sample, coach, lint) | contracts, 409 revise stub, hub payload, allowlist flag, governance |
| app/(learner)/writing/**, components/domain/writing/**, lib/writing/**, lib/paste-exempt.ts, next.config.ts, messages/{en,ar}/*.json | result screens, countdown, allowlist, copy, redirect, key bundles |
| docs/AI-USAGE-POLICY.md, docs/ops/WRITING-AI-PROVIDERS.md, scripts/qa/writing-prod-qa/** | reviewer docs, inert QA harness updates |

## Verification gates

| Gate | Command / workflow | Evidence | Result |
| --- | --- | --- | --- |
| ship-gate | pnpm run ship:gate | local:ship:gate | PASS |
| deploy | production-deploy.yml | NOT RUN | NOT RUN |

## Blockers

- None. Nothing compiled or tested locally by owner directive; first compile is Build images, then owner QA.

## Next action

1. Run pnpm run ship:gate, commit explicit paths, pnpm run ship, then confirm live health and ax:record evidence.
