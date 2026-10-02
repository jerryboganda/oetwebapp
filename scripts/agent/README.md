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
pnpm run ax:verify      # re-check every recorded run id AND every run id cited by a gate row against the GitHub API
pnpm run ax:self-test   # fixture assertions for the checker itself
```

Direct invocation works too: `node ./scripts/agent/state.mjs <command>`.
The hook adapter has its own self-test: `node ./scripts/agent/hook.mjs --self-test`.
Both self-tests run in GitHub Actions (`.github/workflows/ax-check.yml`, Linux and Windows) — that run is
the evidence that the tooling works, not a local run.
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
A run id typed into a gate row is a claim too: `ax:verify` asks GitHub whether that run exists, is
completed and — for a `PASS` row — concluded `success`, so an invented or failed run id is caught.

## Claude Code hooks (auto-load + Stop gate)

Two hooks make the ledger load itself and keep a completion claim honest. Both are **fail-open**: every
path exits 0, errors are swallowed, and a checkout without `scripts/agent/hook.mjs` is silent.

| Event | What it does |
| --- | --- |
| `SessionStart` (start, resume, clear, compact) | Prints the status of **this branch's** ledger: goal, mode, open gates, next action, `ax:check` result. At the workspace root (not a repo) it prints one line per unfinished run across every registered worktree. |
| `Stop` | Blocks the stop when the ledger has structural errors, or when the last message makes a strong completion claim ("production-ready", "fully verified", "all gates pass") while a gate is not `PASS`. |

- **Scoped.** The ledger files are tracked and shared, so a ledger counts as this session's only when it differs
  from the branch's fork point on `origin/main` (committed on this branch, or uncommitted). Someone else's ledger
  stays silent.
- **Bounded.** The block is written to `<git-dir>/ax/stop-<session>.json` first; the same issue never blocks twice,
  `stop_hook_active` never blocks, and a session is blocked at most twice. Afterwards only a note is shown.
- **Compute policy.** Reads files and runs read-only git. No build, no test, no install, no network.

### Install (once per machine)

On Windows, Claude Code reads project settings only from the folder it was launched in, so a project-level hook would
miss the workspace root and every worktree. A **user-level** hook is the single wiring that covers them all, and the
launcher is silent anywhere there is no ledger.

1. Copy `scripts/agent/hook-shim.mjs` to `~/.claude/hooks/ax/ax-hook.mjs` (the shim has no ledger logic; the logic
   lives in each checkout's own `hook.mjs`, so the shim rarely needs updating).
2. Merge this into `~/.claude/settings.json` (keep every existing key; forward slashes and the quoted path are
   deliberate):

```json
{
  "hooks": {
    "SessionStart": [
      { "hooks": [ { "type": "command", "command": "node \"C:/Users/<you>/.claude/hooks/ax/ax-hook.mjs\" session-start", "timeout": 10 } ] }
    ],
    "Stop": [
      { "hooks": [ { "type": "command", "command": "node \"C:/Users/<you>/.claude/hooks/ax/ax-hook.mjs\" stop", "timeout": 10 } ] }
    ]
  }
}
```

3. Open `/hooks` once (or start a new session). No matcher is set on `SessionStart`, so it also fires on `fork`.

## Compute policy

This directory obeys `AGENTS.md` § "GITHUB ACTIONS IS THE ONLY AUTHORIZED COMPUTE
ENVIRONMENT". `state.mjs` performs **static file reads and read-only `gh` calls only**.
It must never run `pnpm`/`npm`/`dotnet`/`next`/`docker`, never install, never build and
never test. `record` and `verify` are the only networked commands and are never reachable
from CI. If you need a build, a test or a typecheck, push the branch or dispatch
`.github/workflows/qa-smoke.yml` — see `.github/instructions/validation.instructions.md`.
