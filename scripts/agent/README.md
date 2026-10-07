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
```

Direct invocation works too: `node ./scripts/agent/state.mjs <command>`.
The self-tests (`--self-test`) and their workflow `ax-check.yml` were deleted on 2026-10-08 with the test code.
`ax:check` remains the local verdict.
`check` accepts `--warn` (never exits non-zero — used by `ship:gate`) and `--json`.

## What each file is

- **`SESSION_STATE.md`** (tracked) — the current run: objective, acceptance criteria,
  decisions not to revisit, touched files, verification gates, blockers, next action.
  Section order is load-bearing. Schema: `session-state.template.md`.
  Two sessions writing it at once: take the newer `Updated:` block wholesale. On a merge conflict compare
  `Updated:` in both versions, then `git checkout --ours|--theirs -- SESSION_STATE.md TASKS.json` for the newer
  side and `git add` it (`ax:check` errors while conflict markers remain). `VERIFICATION.md` never conflicts:
  `.gitattributes` merges it by union and `ax:record` de-duplicates by run id.
- **`TASKS.json`** (tracked) — the execution queue. A task is *ready* when it is
  `pending` and every `blockedBy` id is `done`. Keep at most one `in_progress`.
- **`VERIFICATION.md`** (tracked) — **machine-written only.** One row per GitHub Actions
  run, written by `ax:record`, re-checkable by `ax:verify`. Never hand-edit a Result.
- **`.github/agent-state.local.md`** (gitignored) — the raw evidence journal. `ax:record`
  appends full `gh run view --log-failed` output here so large logs never enter git.

## The honesty rule

A gate may not be ticked without evidence. `ax:check` rejects a `Verification gates` row
whose `Result` is `PASS` while `Evidence` is blank, `-`, `n/a`, or prose. Legal evidence is
an Actions run id (`36824151971`) or `local:ship:gate` / `local:ax:check` / `local:ax:verify` for the sanctioned static checks.
A bare workflow file name proves nothing about any commit, and `local:` can never claim a build, test, lint or typecheck (those are Actions-only), so both are rejected.
A gate that genuinely did not run is recorded as `NOT RUN` — that is honest and passes.
A run id typed into a gate row is a claim too: `ax:verify` asks GitHub whether that run exists, is
completed and — for a `PASS` row — concluded `success`, so an invented or failed run id is caught.

## Claude Code hooks (auto-load + Stop gate + compute guard)

Three hooks make the ledger load itself, keep a completion claim honest, and keep builds and tests
off this machine. All three are **fail-open on error**: every path exits 0, errors are swallowed, and
a checkout without `scripts/agent/hook.mjs` is silent. `PreToolUse` is the one hook that acts — it
denies a command instead of advising against it.

| Event | What it does |
| --- | --- |
| `SessionStart` (start, resume, clear, compact) | Prints the status of **this branch's** ledger: goal, mode, open gates, next action, `ax:check` result. At the workspace root (not a repo) it prints one line per unfinished run across every registered worktree. |
| `Stop` | Blocks the stop when the ledger has structural errors, or when the last message makes a strong completion claim ("production-ready", "fully verified", "all gates pass") while a gate is not `PASS`. |
| `PreToolUse` | Denies a shell command that would build, test, lint, typecheck, serve, install or containerize on this machine (`AGENTS.md` § "GITHUB ACTIONS IS THE ONLY AUTHORIZED COMPUTE ENVIRONMENT"). Every other command passes through untouched. |

### What the compute guard denies

The rules are `LOCAL_COMPUTE_RULES` in `hook.mjs` and they were self-tested in `ax-check.yml` (deleted 2026-10-08), so a new
bypass becomes a failing test instead of a paragraph nobody reads. A rule only matches at a **command
position** — the start of the command or just after a shell separator — so
`git commit -m "drop pnpm test from the docs"` is allowed while `git pull && pnpm run build` is not.

- Denied: `pnpm`/`npm`/`yarn` script runs for `build`, `test*`, `lint`, `dev`, `start`, `typecheck`,
  `backend:*`, `mobile:*`, `desktop:dev`, `docker:*`, `check:encoding`; the same via `exec`/`dlx`;
  `vitest`, `jest`, `pytest`, `playwright test`, `run-playwright-matrix`; `dotnet test|build|ef|publish|restore`;
  `tsc`, `eslint`, `next build|dev`, `cargo`, `tauri dev`; `pnpm install|ci`; `docker build|run`
  and `docker compose up`.
- Allowed on purpose: `pnpm run ship`, `pnpm run ship:gate`, `pnpm run ax:*`, `pnpm run pipeline:check`,
  git and gh. `pnpm add|remove` is also allowed — a dependency edit has to write a lockfile diff
  somewhere and CI cannot do that for you. Remove that exception in `LOCAL_COMPUTE_RULES` if the rule
  should be absolute.

```powershell
node scripts/agent/hook.mjs --check "pnpm test"     # exit 2 + the reason
node scripts/agent/hook.mjs --check "pnpm run ship" # allowed
```

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
    ],
    "PreToolUse": [
      { "matcher": "Bash|PowerShell", "hooks": [ { "type": "command", "command": "node \"C:/Users/<you>/.claude/hooks/ax/ax-hook.mjs\" pre-tool-use", "timeout": 10 } ] }
    ]
  }
}
```

3. Refresh the shim whenever this file changes (it is a copy, not a link): re-copy step 1.
4. Open `/hooks` once (or start a new session). No matcher is set on `SessionStart`, so it also fires on `fork`.

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

## Ship wrapper (multi-agent safe) — `pnpm run ship`

`scripts/ship/ship.mjs` is the one command per ship (owner directive 2026-10-03):

1. **Ship lock** — `<git-common-dir>/ax-ship/lock.json`, shared by every linked worktree, TTL 30 min
   with a 60 s heartbeat. A live local owner retains its lock even if a synchronous
   watch delays the heartbeat. Exclusive creation prevents competing first acquirers;
   unreadable state or an inactive lock blocks for verified exact-file recovery,
   never automatic unlink/recreate against an old snapshot. No flag can steal a running ship.
2. **Rebase** — `git fetch origin main` + `git rebase --autostash origin/main`. Ledger conflicts
   auto-resolve by the documented rule (newer `Updated:` block wins wholesale for
   `SESSION_STATE.md` / `TASKS.json`; `VERIFICATION.md` merges by union), anything else aborts.
3. **Gate** — `scripts/ship/pre-push-gate.mjs` (seconds).
4. **Visibility lease** — `<git-common-dir>/ax-ship/visibility.json` plus the existing
   native `PUBLIC_WINDOW_HOLDERS` channel shared with the isolated console.
   Mutable state is atomically replaced; failed native cleanup keeps its ownership
   record recoverable rather than pretending it was released. The repo is made public
   *before* the push (a queued run that starts on a private repo is refused by hosted runners), and
   flipped private again only when no local or native holder and no Actions run is
   queued/in progress. `--may-flip-private` is the read-only guarded verdict.
   The internal watcher never flips visibility itself.
5. **Push** — `git push origin HEAD:main` (never `--force`) with fetch/rebase/gate retry.
6. **Watch** — `watch-deploy.ps1 -SkipPublic -SkipPrivateFlip`, supersede-aware: when a newer push
   replaced this SHA before its deploy ran, it adopts the newer run and prints
   `SHIP-WATCH_SUPERSEDED_BY <sha>`. The same applies after the rollout — if the router slot ends
   up carrying a **descendant** of this SHA, it prints `SHIP-WATCH_SUPERSEDED_BY_LIVE <sha>` and
   succeeds; `LIVE_SHA_MISMATCH` (exit 4) is reserved for a slot carrying neither this SHA nor a
   descendant, i.e. traffic genuinely still on an older build.
7. **Record** — `ax:record` on green; a recording failure makes shipment incomplete.

`AGENTS.md`'s mandatory accelerated baseline applies to every contributor and agent.
Supported diagnostics: `--dry-run` (static gate/report), `--no-push` (rebase/gate,
no visibility or release), `--status`. Recovery `--sha <sha>` performs a verified
watch under the same gate/lease/evidence/cleanup rules. `--release-lease` releases
only this process's lease. `--self-test` ran on Actions in `ax-check.yml` (deleted 2026-10-08).
`--no-watch`, `--no-record`, `--no-visibility`, `--force-release` and alternate
production-workflow overrides are rejected. Native visibility/queue read failures
block the action; an unreadable queue is never an empty queue.

## Compute policy

This directory obeys `AGENTS.md` § "GITHUB ACTIONS IS THE ONLY AUTHORIZED COMPUTE
ENVIRONMENT". `state.mjs` performs **static file reads and read-only `gh` calls only**.
It must never run `pnpm`/`npm`/`dotnet`/`next`/`docker`, never install, never build and
never test. `hook.mjs` enforces the same policy on agent shell commands through the
`pre-tool-use` guard (above), because the prose rule alone was not holding: agents kept
reaching for `pnpm test` / `dotnet test` to shorten their own feedback loop.
`record` and `verify` are the only networked commands and are never reachable
from CI. There is no CI test, lint or typecheck run (owner directive 2026-10-06): the build compiles in
`Build images` and the owner tests manually — see `.github/instructions/validation.instructions.md`.
