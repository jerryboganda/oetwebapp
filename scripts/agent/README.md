# scripts/agent/ — agent working-memory ledger

Layer 2 of the three-layer state model in `AGENTS.md` § Continuity Protocol:

| Layer | File | Lifetime |
| --- | --- | --- |
| Permanent rules | `AGENTS.md`, `.github/instructions/**`, `docs/**` | months–years |
| **Current run** | `SESSION_STATE.md`, `TASKS.json` | hours–days |
| Objective truth | `VERIFICATION.md`, git, GitHub Actions runs | always |

`state.mjs` is the only writer of the ledger artifacts. It is deliberately dumb: it
parses markdown/JSON and reads run metadata from `gh`.

## Commands

```powershell
pnpm run ax:status      # goal, mode, gate and task counts, blockers, next action
pnpm run ax:check       # validate the ledger — exit 1 on any error
pnpm run ax:next        # print the first pending task whose dependencies are done
pnpm run ax:init        # create SESSION_STATE.md from the template (refuses to overwrite)
pnpm run ax:record      # GitHub Actions -> VERIFICATION.md + the local evidence journal
pnpm run ax:verify      # re-check every recorded run id against the GitHub API
pnpm run ax:self-test   # fixture assertions for the checker itself
```

Direct invocation works too: `node ./scripts/agent/state.mjs <command>`.
`check` accepts `--warn` (never exits non-zero — used by `ship:gate`) and `--json`.

## What each file is

- **`SESSION_STATE.md`** (tracked) — the current run: objective, acceptance criteria,
  decisions not to revisit, touched files, verification gates, blockers, next action.
  Section order is load-bearing. Schema: `session-state.template.md`.
  Two sessions writing it at once: take the newer `Updated:` block wholesale.
- **`TASKS.json`** (tracked) — the execution queue. A task is *ready* when it is
  `pending` and every `blockedBy` id is `done`. Keep at most one `in_progress`.
- **`VERIFICATION.md`** (tracked) — **machine-written only.** One row per GitHub Actions
  run, written by `ax:record`, re-checkable by `ax:verify`. Never hand-edit a Result.
- **`.github/agent-state.local.md`** (gitignored) — the raw evidence journal. `ax:record`
  appends full `gh run view --log-failed` output here so large logs never enter git.

## The honesty rule

A gate may not be ticked without evidence. `ax:check` rejects a `Verification gates` row
whose `Result` is `PASS` while `Evidence` is blank, `-`, `n/a`, or prose. Legal evidence is
a run id (`36824151971`), a workflow file (`deploy.yml`), or `local:<command>`.
A gate that genuinely did not run is recorded as `NOT RUN` — that is honest and passes.

## Compute policy

This directory obeys `AGENTS.md` § "GITHUB ACTIONS IS THE ONLY AUTHORIZED COMPUTE
ENVIRONMENT". `state.mjs` performs **static file reads and read-only `gh` calls only**.
It must never run `pnpm`/`npm`/`dotnet`/`next`/`docker`, never install, never build and
never test. `record` and `verify` are the only networked commands and are never reachable
from CI. If you need a build, a test or a typecheck, push the branch or dispatch
`.github/workflows/qa-smoke.yml` — see `.github/instructions/validation.instructions.md`.
