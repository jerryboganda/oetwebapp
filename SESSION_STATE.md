# SESSION STATE

Session: ax-enforcement-hooks
Goal: Make the AX ledger enforce itself: auto-load at session start, a bounded Stop gate, run ids in gate rows checked against GitHub, and CI proof on Linux and Windows
Mode: verify
Updated: 2026-10-02T01:37:30Z
Branch: feat/agent-experience-ax-2026-10-02
HEAD: e27b55dbf

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

main already had the ledger, the task queue, machine-written evidence and the `ax:*` checker, but
nothing made an agent read it or kept a completion claim honest. This run adds (1) a fail-open Claude
Code hook adapter (SessionStart auto-load, Stop gate), (2) `ax:verify` coverage of run ids typed into
gate rows, which were never checked, and (3) Actions proof of the tooling on Linux and Windows.
Delivery is branch + CI proof; merging (one docs-only Build & Deploy) waits for the owner.

## Acceptance criteria

- [x] AC-1 `state.mjs` and `hook.mjs` self-tests pass in GitHub Actions on Linux and Windows (run 36950958768).
- [x] AC-2 `ship:gate` is OK on every changed file (the same gate `deploy.yml` re-runs on merge).
- [x] AC-3 Only the feature branch was pushed; remote `main` was not touched.
- [x] AC-4 The launcher is installed and wired in user-level settings (backup kept); the installed command behaves correctly on synthetic hook input.
- [ ] AC-5 Owner approves the merge; Build & Deploy for the merge SHA is green.

## Decisions (do not revisit)

- D-1 Tracked `SESSION_STATE.md` is the canonical ledger; `.github/agent-state.local.md`
  becomes the machine-written, gitignored raw evidence journal. Reversing costs the
  21-file repoint.
- D-2 `ship:gate` only ever **warns** about state; the hard failure lives in `ax:check`.
  Reversing risks `deploy.yml`'s `syntax-gate`, which runs `--self-test` then `--ci`.
- D-3 `VERIFICATION.md` is machine-written only — `ax:record` from real `gh` data. A
  hand-edited evidence row defeats the point of the file.
- D-4 No merge driver on the ledger files: `merge=ours` needs a one-time global `git config`
  on every machine and fails silently without it.
- D-5 Historical records citing the old path (`docs/releases/**`) are left untouched; the
  file still exists, so the citations remain true.
- D-6 The agent/skill surfaces point at the one canonical Actions-only validation ladder
  instead of restating it.
- D-7 Hooks are user-level, through one launcher in `~/.claude/hooks/ax`. On Windows Claude Code
  reads project settings only from the launch folder, so a project-level hook misses the workspace
  root and every worktree. The launcher holds no ledger logic: each checkout's own
  `scripts/agent/hook.mjs` does, so it is versioned and CI-tested with the repo.
- D-8 A ledger counts as this session's only when it differs from the branch's fork point on
  `origin/main` (committed or uncommitted). Anything else is someone else's tracked ledger and the
  hooks stay silent, so a peer's ledger can never block your session.
- D-9 The Stop gate fails open and is bounded: it never exits non-zero, blocks the same issue once,
  ignores `stop_hook_active`, and blocks a session at most twice.
- D-10 AX-07's own deploy (36911073618) was cancelled by a later push. Build & Deploy run 36946463004
  (sha 7e7632ace) succeeded and contains that commit, so AX-07 is closed on that run.
- D-11 A full Windows checkout of this repo fails (a tracked Medicine-pack PDF path exceeds 260
  characters), so `ax-check.yml` sparse-checks-out `scripts/agent`. Any future Windows job that needs
  the whole tree needs `core.longpaths` (as `tauri-ci.yml` sets).

## Touched files

| Path | Change |
| --- | --- |
| `scripts/agent/hook.mjs` | new |
| `scripts/agent/hook-shim.mjs` | new |
| `scripts/agent/state.mjs` | edit (gateRunEvidence, compareGateRuns, verify, self-test) |
| `scripts/agent/README.md` | edit (hooks section, install steps) |
| `AGENTS.md` | edit (loop line, hooks line) |
| `.github/workflows/ax-check.yml` | new |
| `SESSION_STATE.md`, `TASKS.json`, `VERIFICATION.md` | edit (this run's ledger) |

## Verification gates

| Gate | Command / workflow | Evidence | Result |
| --- | --- | --- | --- |
| ax-self-tests | ax-check.yml (linux node 22, windows node 22 and 24) | 36950958768 | PASS |
| ship-gate | pnpm run ship:gate | local:ship:gate | PASS |
| hook-smoke | installed launcher, 7 synthetic hook inputs | local:hook-smoke | PASS |
| deploy | deploy.yml | NOT RUN | NOT RUN |

## Blockers

- None. Waiting on the owner's go to open the PR and merge; a merge is one docs-only Build & Deploy.

## Next action

1. On the owner's go: open the PR, wait for a quiet window (no Build & Deploy in flight on `main`), squash-merge,
   then `powershell -ExecutionPolicy Bypass -File scripts/ship/watch-deploy.ps1 -Sha <merge sha>` (add
   `-SkipPublic -SkipPrivateFlip` only while another session shares the public window), followed by
   `pnpm run ax:record` and `pnpm run ax:verify`.
