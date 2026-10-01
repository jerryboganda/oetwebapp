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

Externalized working memory. Three layers, exclusive ownership — no file has two jobs.

| Layer | File | Lifetime |
| --- | --- | --- |
| Permanent rules | `AGENTS.md`, `.github/instructions/**`, `docs/**` | months–years |
| Current run | `SESSION_STATE.md`, `TASKS.json` | hours–days |
| Objective truth | `VERIFICATION.md`, git, GitHub Actions runs | always |

- Non-trivial work: `pnpm run ax:status`, then read `SESSION_STATE.md`, `TASKS.json` and `PROGRESS.md`.
  Continue from `SESSION_STATE.md` only when its Goal matches the newest request; otherwise re-goal it
  with `pnpm run ax:init`. Schema: `scripts/agent/session-state.template.md`.
- Choose the next unit of work with `pnpm run ax:next`; move `TASKS.json` statuses as you go.
- Never tick a gate without evidence. `pnpm run ax:check` rejects a `PASS` row whose evidence is not a
  run id, a workflow file, or `local:<command>`.
- After the deploy for this SHA is green, `pnpm run ax:record` writes real run ids into `VERIFICATION.md`
  and raw logs into the gitignored `.github/agent-state.local.md`; `pnpm run ax:verify` re-checks them
  against GitHub. `VERIFICATION.md` is machine-written — never hand-edit a result.
- `PROGRESS.md` is the compact durable ledger only; history lives in `docs/PROGRESS-ARCHIVE-2026.md`
  and git.
- Before handoff: `pnpm run ax:check` passes and `SESSION_STATE.md` names the next concrete step.

## Default loop

- Classify the task area, inspect existing patterns, identify invariants before designing behavior.
- Use a visible todo list for multi-step work.
- Make minimal edits that fit existing boundaries; preserve unrelated user changes.
- Prefer focused tests for behavior changes and bug fixes.
- Review the diff for OET contracts, security, tests, and regressions.
- Validate with the lightest credible GitHub Actions run (`validation.instructions.md`) before reporting done.
- After a `main` push, `pnpm run ship:watch` until Build & Deploy for this SHA succeeds. Failure logs are the agent's job to fix and re-push. Do not stop at "deploy initiated" and do not wait for the owner to notice.
- Once live health is green, `pnpm run ax:record` then `pnpm run ax:verify` so `VERIFICATION.md` carries this SHA's real evidence.
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
