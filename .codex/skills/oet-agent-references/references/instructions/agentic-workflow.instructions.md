---
name: "Agentic Workflow"
description: "Use when planning, implementing, debugging, refactoring, reviewing, or handing off work in the OET Prep Platform — covers continuity, routing, and autonomous-loop discipline."
---

# Agentic Workflow

How agents operate in this repo. Repo rules win over generic skill/agent/plugin defaults.

## Source of truth & precedence

1. `AGENTS.md` (always on) and `.github/copilot-instructions.md` (always on).
2. Matching `.github/instructions/*.instructions.md` for the files you touch.
3. Domain docs referenced by `AGENTS.md` and `docs/`.
4. Nearby code and tests.

`AGENTS.md` carries the authoritative map of all AI-direction files.

## Continuity protocol (canonical)

- For non-trivial work, first read `PROGRESS.md` and `.github/agent-state.local.md` if present.
- Continue from `.github/agent-state.local.md` only when it matches the newest user request;
  otherwise repoint it to the new goal.
- Keep `PROGRESS.md` compact — no pasted historical ledgers; history lives in git.
- Before ending substantial work, update `.github/agent-state.local.md` with goal, touched files,
  validation evidence, blockers, and the next concrete step.

## Default loop

- Classify the task area, inspect existing patterns, identify invariants before designing behavior.
- Use a visible todo list for multi-step work.
- Make minimal edits that fit existing boundaries; preserve unrelated user changes.
- Prefer focused tests for behavior changes and bug fixes.
- Review the diff for OET contracts, security, tests, and regressions.
- Validate with the lightest credible host command (`validation.instructions.md`) before reporting done.
- Ask only when a missing decision blocks correctness or safety; offer a recommended option.

## Lean context policy

- Do not eager-load broad skill catalogs, prompt libraries, generated bundles, or whole-codebase docs.
- Vendored catalogs are intentionally archived. Do not restore `.github/skills`, broad `awesome-*`
  agents, or global `awesome-copilot` assets without an explicit user request.
- Prefer targeted searches and local reads over Repomix or broad scans.

## Specialist agents (optional)

Use Superpowers as the general-purpose primary. For repo-specific lanes, the workspace OET agents are
available: Explorer (discovery), Planner (sequencing), Implementer (edits), Security Reviewer,
QA Validator, Reviewer. OmO/Ralph agents add PRD/PROGRESS loop memory for long autonomous runs.
Use specialist workflows when they fit; do not over-orchestrate simple tasks.
