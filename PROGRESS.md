# PROGRESS - Active Agent Continuity

Last updated: 2026-05-31

This file is intentionally compact. Historical progress was archived locally at `.github/context-archive-20260531/PROGRESS-before-continuity.md` and is also available through git history. Do not paste long historical ledgers back here.

## Current Operating Goal

Keep GitHub Copilot in VS Code efficient and stateful for the OET workspace by using compact, durable task memory instead of large catalogs or stale conversation context.

## Current State

- Startup-context cleanup is complete: repo `.github/skills` and broad `awesome-*` agents were archived locally; always-loaded OET instructions are compact.
- Broad global `awesome-copilot` skill/agent discovery was disabled in place by renaming discovery files with `.disabled-20260531` because Windows/VS Code locked the plugin folder.
- A compact local task ledger is expected at `.github/agent-state.local.md`; update it during long work and before handoff.
- Repo and user-level OmO/Ralph/Superpowers agents now have continuity-first instructions.
- Superpowers is now the preferred all-purpose primary agent for future OET build, fix, debug, review, research, and automation tasks.
- Archived catalog deletion noise is hidden locally with `skip-worktree`; broad status now shows only real modified customization files.
- Heavy validation remains Docker-only per `AGENTS.md`; never use the production VPS for validation.

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
