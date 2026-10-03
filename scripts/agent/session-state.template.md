# SESSION STATE

Session: <slug>
Goal: <one sentence — what "done" means>
Mode: execute
Updated: 2026-01-01T00:00:00Z
Branch: main
HEAD: 0000000

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

<2–4 lines. What this session is changing and why. Include the defect or the
acceptance bar, not the implementation detail.>

## Acceptance criteria

- [ ] AC-1 <observable outcome>
- [ ] AC-2 <observable outcome>

## Decisions (do not revisit)

- D-1 <decision> — <why>. Reversing this costs <what>.

## Touched files

| Path | Change |
| --- | --- |
| <path> | <new / edit / delete> |

## Verification gates

| Gate | Command / workflow | Evidence | Result |
| --- | --- | --- | --- |
| ship-gate | pnpm run ship:gate | local:ship:gate | PASS |
| deploy | production-deploy.yml | NOT RUN | NOT RUN |

## Blockers

- None.

## Next action

1. <the single next concrete command or edit>
