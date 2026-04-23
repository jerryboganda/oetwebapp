# Agentic Bootstrap For OET Prep Platform

> This bootstrap is additive and conservative. It refreshes the repo operating docs, adds a small repo-local Codex config, and creates a shallow agent set without changing the current MCP stack.

## Objective

Establish a durable operating model for Codex inside the OET Prep Platform repo so future agent work can stay narrow, safe, and consistent across web, backend, desktop, and mobile surfaces.

## Decisions

- Reuse the existing docs spine instead of creating a parallel instruction system.
- Keep the current MCP routing path unchanged.
- Keep the agent topology shallow and direct-child only.
- Prefer a separate worktree for implementation because the repo may already be dirty.
- Treat scoring, rulebooks, auth, and the statement-of-results card as high-ripple surfaces.

## Deliverables

- Refresh `AGENTS.md`
- Align `README.md`
- Create `.codex/config.toml`
- Create `docs/agent-operating-model.md`
- Create the shallow agent files under `.codex/agents/`

## Execution Order

1. Confirm local truth for the backend port, SDK baseline, and existing docs.
2. Refresh the repo operating docs.
3. Add the repo-local Codex config.
4. Add the agent definitions.
5. Validate links, config syntax, and local truth consistency.

## Agent Set

- `repo_cartographer`
- `execplan_strategist`
- `backend_owner`
- `frontend_owner`
- `api_contract_guard`
- `qa_validator`

Each agent should stay within one surface or one concern and should not expand the bootstrap scope.

## Validation

- Parse `.codex/config.toml`
- Verify the new docs and agent files exist
- Confirm the README and AGENTS file agree with the backend launch settings
- Recheck `git status` to confirm unrelated dirty files were not touched

## Completion Criteria

The bootstrap is complete when the repo has:

- a clean, accurate operating model
- a minimal repo-local Codex config
- the shallow agent set
- no accidental changes to unrelated work
- consistent local port and SDK references

