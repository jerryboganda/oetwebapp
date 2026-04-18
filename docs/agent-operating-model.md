# Agent Operating Model

This document is the durable operating system for the OET Prep Platform repo.
Use it alongside [`AGENTS.md`](../AGENTS.md) and [`README.md`](../README.md) when starting any agentic task.

## Purpose

The repo spans four surfaces:

- Web: Next.js 15 frontend
- API: ASP.NET Core 10 backend
- Desktop: Electron shell
- Mobile: Capacitor shell

The repo works best when agents stay narrow, respect shared contracts, and verify changes against the real local baseline.

## Source Of Truth Order

When you need repo context, read in this order:

1. `AGENTS.md`
2. `README.md`
3. `docs/agent-operating-model.md`
4. Relevant domain docs such as `docs/SCORING.md`, `docs/RULEBOOKS.md`, and `docs/OET-RESULT-CARD-SPEC.md`
5. Plan docs in `docs/plan/` and `docs/superpowers/plans/`

Do not create parallel instruction surfaces unless the task truly needs them.

## Worktree Model

- Prefer an isolated worktree for bootstrap, multi-file, or high-risk work.
- Keep unrelated edits intact.
- Never revert files you did not author.
- If the repo is already dirty, inspect the diff first and preserve user changes.

## Agent Topology

Use shallow, direct-child agents when the task benefits from delegation.

- `repo_cartographer`: identify boundaries, entry points, and risky surfaces
- `execplan_strategist`: turn the request into a safe execution order
- `backend_owner`: own ASP.NET Core and API changes
- `frontend_owner`: own Next.js, React, and shared UI changes
- `api_contract_guard`: protect DTOs, endpoints, and cross-surface invariants
- `qa_validator`: confirm syntax, tests, and baseline consistency

Keep ownership disjoint. If two agents might touch the same file, serialize the work instead of racing.

## Shared Contracts

The following paths are high-ripple surfaces and should be edited deliberately:

- `backend/src/OetLearner.Api/Program.cs`
- `lib/api.ts`
- `lib/auth-client.ts`
- `lib/scoring.ts`
- `lib/rulebook/index.ts`
- `middleware.ts`
- `next.config.ts`
- `capacitor.config.ts`
- `electron/`
- `components/domain/OetStatementOfResultsCard.tsx`

If a task must touch one of these, call it out explicitly and verify the downstream impact.

## Bootstrap Rules

- Keep the current MCP stack unchanged unless the task explicitly asks for a new integration.
- Do not add project skills during bootstrap. Reuse the installed skill surface first.
- Keep the repo-local Codex config small and conservative.
- Prefer docs and config before code when the goal is to establish a new operating model.

## Validation Ladder

Use the lightest checks first, then expand:

1. Parse the config or schema you created.
2. Check that links and file paths are correct.
3. Verify local truth against the backend launch settings and app settings.
4. Run lint, type-check, unit tests, and build checks when code changed.
5. Run E2E checks only when the change can affect runtime flows.

## Guardrails

- All OET scoring must go through the scoring helpers, never inline logic.
- All rulebook enforcement must go through the rulebook helpers or services.
- All AI calls must use the grounded gateway.
- Content uploads must flow through `IFileStorage`.
- The statement-of-results card is contract-driven and should not be "improved" casually.

## Bootstrap Deliverables

The 2026-04-19 bootstrap added the following repo-level shape:

- refreshed operating docs
- a small repo-local Codex config
- a shallow agent set
- a durable bootstrap plan doc

Those items should be treated as the baseline for future agent work.

