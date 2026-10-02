# SESSION STATE

Session: writing-ai-launch
Goal: Writing AI is ready for owner testing and candidate launch: every row of the 15-row pre-launch checklist PASS with GitHub Actions evidence (stable Max 5x grading with invisible failover, zero-loss drafts and submissions, report UI cleanup, 3 live typed letters per enabled profession)
Mode: execute
Updated: 2026-10-02T13:46:13Z
Branch: feat/writing-ai-launch-2026-10-02
HEAD: b1201244c

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
| ship-gate | pnpm run ship:gate | NOT RUN | NOT RUN |
| backend compile + tests | writing-rev8-ci.yml / qa-smoke.yml | NOT RUN | NOT RUN |
| frontend tsc + vitest | writing-rev8-ci.yml / qa-smoke.yml | NOT RUN | NOT RUN |
| deploy | deploy.yml | NOT RUN | NOT RUN |
| live QA | writing-prod-qa.yml | NOT RUN | NOT RUN |

## Blockers

- None.

## Next action

1. WAI-00: extend the production-diagnostics job (latest failed grades + the one authorised API model check), dispatch it, and record the root-cause table here; in parallel WAI-01..WAI-09 run in worktrees `_wai-*-wt` on branches `wai/*` and are merged into this branch.
