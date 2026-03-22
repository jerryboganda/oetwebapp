---
name: docs-sync
description: Keep repo docs aligned with current behavior. Use when commands, routes, architecture, validation steps, or decisions change and the durable docs need an update.
---

# Docs Sync

## Purpose
- Keep repo memory in sync with what the code actually does.
- Prevent future work from depending on chat-only context.

## Workflow
- Update the relevant durable docs first: AGENTS, plans, decision log, or architecture notes.
- Record the current behavior, the reason for the change, and any known backlog.
- Keep descriptions factual and short.
- Avoid rewriting product docs unless the behavior truly changed.

## Repo context
- Primary durable docs: `AGENTS.md`, `docs/PLANS.md`, and `docs/decision-log.md`.
- Project agent and skill config lives under `.codex/agents` and `.codex/skills`.
- External template docs live in `documentation/` and should remain untouched.

## Output
- Mention which docs changed and why.
- Capture any new assumptions, commands, or hotspots that future work should know.
- Keep the prose terse and maintenance-friendly.
- Do not change application code.
