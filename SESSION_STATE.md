# SESSION STATE

Session: writing-ai-launch
Goal: Writing AI is ready for owner testing and candidate launch: every row of the 15-row pre-launch checklist PASS with GitHub Actions evidence (stable Max 5x grading with invisible failover, zero-loss drafts and submissions, report UI cleanup, 3 live typed letters per enabled profession)
Mode: execute
Updated: 2026-10-02T14:21:43Z
Branch: feat/writing-ai-launch-2026-10-02
HEAD: 7de11071d

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

Implement the owner's "Writing AI - Final Pre-Launch Handoff" (22 pp.). The full plan, with file-level designs, tests, owner decisions
and the 36 verbatim QA scripts, is `C:\Users\Dr Faisal Maqsood PC\.claude\plans\implement-100-leave-twinkling-catmull.md`
(tickets WAI-00..WAI-12). Acceptance bar: all 15 launch-checklist rows PASS with Actions run ids; PARTIAL/NOT-PROVEN rows are stated, never rounded up.

## Acceptance criteria

- [ ] AC-1 Grading never visibly fails: Max 5x primary, retry once, API, GPT-6.1 Sol via Codex; failures auto-retry server-side; a failed row is retryable from Post Submissions on the SAME submission.
- [ ] AC-2 Credits: charged once at task open, held on failure, Retry costs 0, plan gate cannot refuse a credit-funded grade; real-ledger tests.
- [ ] AC-3 Drafts: autosave + local copy + exact resume text + same remaining timer; V2 submissions listed in Post Submissions; free sample follows the same rules.
- [ ] AC-4 Report: Appeal removed, "Preparing model answer", new section order with View all corrections, desktop containment and mobile overlays proven.
- [ ] AC-5 Live QA on production via Actions: 3 typed letters per enabled profession, P0-3 scenarios, QA-2 evidence table delivered.

## Decisions (do not revisit)

- D-1 Max 5x subscription stays primary and is made stable; no API-primary mode; one tiny (~$0.01) paid Anthropic test-model call is authorised, no other API spend — owner, 2 Oct 2026.
- D-2 Level 3 is GPT-6.1 Sol (gpt-6.1-sol, effort high), replacing gpt-6-sol everywhere — owner.
- D-3 Timer rule is pause-while-away (remaining at last save restored; clock runs only while the editor is mounted) — owner.
- D-4 QA-only per-learner fault switch approved; diagnose from the latest failed grades first — owner.
- D-5 Platform $ caps and the Max weekly-estimate auto-switch are deleted via one admin switch (off by default); per-learner credits, plan token limits, kill switch and kill list stay — owner.
- D-6 Credits: charge once at task open; failure holds the credit; Retry costs 0; no automatic refund — owner.
- D-7 Shared Claude login with the Owner Agent Console is kept (Max allows concurrent sessions); mitigations only — owner.
- D-8 Assumptions open to veto: fresh disposable QA learners per profession; "Practice this again" becomes a real new attempt (attempt-scoped submit lock); Post Submissions = the existing Past submissions card.

## Touched files

| Path | Change |
| --- | --- |
| SESSION_STATE.md, TASKS.json | edit (re-goal) |

## Verification gates

| Gate | Command / workflow | Evidence | Result |
| --- | --- | --- | --- |
| production diagnostics (WAI-00, read-only) | writing-rev8-ci.yml `production-latest-diagnostics` | run 37018897335 | PASS |
| authorised API model check (WAI-00, claude-opus-5-5) | writing-rev8-ci.yml `production-latest-diagnostics` | run 37018040352 | PASS |
| ship-gate | pnpm run ship:gate | NOT RUN | NOT RUN |
| backend compile + tests | writing-rev8-ci.yml / qa-smoke.yml | NOT RUN | NOT RUN |
| frontend tsc + vitest | writing-rev8-ci.yml / qa-smoke.yml | NOT RUN | NOT RUN |
| deploy | deploy.yml | NOT RUN | NOT RUN |
| live QA | writing-prod-qa.yml | NOT RUN | NOT RUN |

### WAI-00 root-cause table (production, 2 Oct 2026 14:15Z; evidence = the two runs above)

| Rank | Finding | Evidence |
| --- | --- | --- |
| 1 | Sticky Max marker: `WritingAiClaudeQuotaExceededUntil` = 2026-10-07 11:49Z, failoverActive=true, so Max (L1) is skipped for EVERY Writing grade | set 30 Sep 11:49:09Z by two instant `provider_error` rows on writing-claude-sub (75/386 ms, a sidecar redeploy); old code turns any L1 blip into a 7-day marker; all grades since ran on the paid API (L2: 54 calls, $13.52 this week) |
| 2 | L2 API had no credit: HTTP 400 "credit balance too low" classed `provider_quota_exhausted`, circuit `anthropic` open | 8 rows 30 Sep 19:24 -> 1 Oct 16:31; the owner has since funded it: the ONE authorised `test-model` call (claude-opus-5-5) returned ok in 2.2 s on 2 Oct (run 37018040352) |
| 3 | L3 Codex: gpt-6-sol worked (3 successes, last 1 Oct 16:31) but was unreachable in deploy windows: HTTP 404 x19 (30 Sep 17:23-20:45, "Unknown route"), 502 x2 (1 Oct 04:00); `/providers/*/test` probes of both subscription sidecars ok on 2 Oct (run 37018897335) | usage breakdown; gpt-6.1-sol is still UNPROVEN live (proved by the QA l1l2 run) |
| 4 | Plan gate refuses a credit-funded grade (`feature_not_in_plan`, policy `plan.feature_gate.writing.grade`), Retry fails identically | 6 rows 12 Sep -> 1 Oct 17:09, three within 2 min (Retries) |
| 5 | Credits charged TWICE per letter for a finite-balance learner (`writing-v2:` -2 at task open, `writing-grade:` -2 at grading) and a failed grade with no `:release` row | credit ledger of two failing learners |
| - | NOT observed: Indeterminate duplicate (0 of the latest 200 ops; 176 FailedTerminal, 24 Completed) but one submission accumulated 7 FailedTerminal ops via Retries (replay walk) | operations list |
| - | Historical only: 71 `global_budget.hard_kill` + 48 `plan.starter.daily.deny` refusals, all 12 Sep; budgets now $30/day, 2026-10 committed 0 | usage breakdown, /budgets |

Other facts: Max sidecar latency (all features) p50 63 s / p90 83 s / max 93 s. `free_samples_enabled` is ON (rollout 100). Learner professions (anonymous catalogue): nursing, medicine, pharmacy, dentistry, physiotherapy, other-allied-health, radiography, academic-english. Dietetics, OT, optometry, podiatry, speech pathology and veterinary are NOT account professions (only the six clinical ones have canonical Writing packs), so the live matrix is 6 x 3 = 18 letters and the other handoff professions are reported `NOT_ENABLED` with evidence — flag this scope gap to the owner in the final report.

## Blockers

- None blocking. Owner-visible scope gap (see above): the handoff lists 11 professions + veterinary, the product enables 6 for Writing.

## Next action

1. WAI-01..WAI-10 run in worktrees `_wai-*-wt` on branches `wai/*` (8 background agents); merge each into this branch, then WAI-06b (pipeline lock/dedupe), full CI, `/code-review`, ship Increment 1. After the Inc-1 deploy: clear the legacy sticky marker (`PUT /v1/admin/ai/writing-provider {clearQuotaMarker:true}`) and reset stale circuits before the mini live check.
