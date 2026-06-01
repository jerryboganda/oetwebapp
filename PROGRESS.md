# PROGRESS - Active Agent Continuity

Last updated: 2026-06-01

This file is intentionally compact. Historical progress was archived locally at `.github/context-archive-20260531/PROGRESS-before-continuity.md` and is also available through git history. Do not paste long historical ledgers back here.

## Current Operating Goal

The current production-facing task is complete: the Reading diagnostic finish-flow hardening is committed, pushed, and deployed to the live VPS green slot.

## Current State

- Commit `4345305e` is on `origin/main`.
- Production VPS rebuilt the green slot with a no-cache source build, updated `.deploy/active-slot.env` to `green`, stopped the stale blue slot, and passed public API/web health checks.
- The deployed green web bundle contains the `diagnostic_already_submitted` marker from the updated finish-flow logic.
- `.github/agent-state.local.md` now reflects the deployed state and the remaining follow-up is optional browser QA.
- A compact local task ledger is expected at `.github/agent-state.local.md`; update it during long work and before handoff.
- Heavy validation remains Docker-only per `AGENTS.md`; production VPS was used only for deployment and verification.

## Next-Step Protocol For New Agent Runs

1. Read `AGENTS.md`, `.github/copilot-instructions.md`, this file, and `.github/agent-state.local.md` if it exists.
2. Run a scoped `git status --short -- <relevant paths>` instead of dumping the whole tree.
3. Continue from `## Next Concrete Step` in `.github/agent-state.local.md` when it matches the user's newest request.
4. If state is stale or conflicts with the newest user request, update the state file and follow the newest user request.
5. Before ending substantial work, update `.github/agent-state.local.md` with goal, touched files, validation, blockers, and next concrete step.

## Active Risks

- Do not restore `.github/skills`, user-level standalone skills, or global `awesome-copilot` discovery unless explicitly requested.
- Prefer scoped `git status` output, but broad status is currently usable after local `skip-worktree` cleanup.
- The user wants high autonomy, but missing product/security decisions still require a concise question with a recommended default.
