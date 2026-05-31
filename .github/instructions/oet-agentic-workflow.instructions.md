---
description: "Use when planning, implementing, debugging, refactoring, or reviewing work in the OET Prep Platform."
name: "OET Agentic Workflow"
---

# OET Agentic Workflow

- Start from `AGENTS.md`, then inspect only the relevant code, tests, and docs.
- Read `PROGRESS.md` and `.github/agent-state.local.md` before non-trivial work; continue from the state only if it matches the newest user request.
- Keep a visible todo list for multi-step work and update it as work progresses.
- Load domain docs only for touched surfaces: scoring, rulebooks, AI gateway, uploads, result card, reading, grammar, pronunciation, conversation, runtime settings, deployment, or admin UI.
- Prefer Superpowers as the primary all-purpose orchestrator for future OET build, fix, debug, review, research, and automation tasks unless a narrower top-level agent is explicitly required.
- Use user-level OmO/Ralph/Superpowers agents only when they clearly help; do not restore archived broad catalogs.
- Research in parallel when independent; serialize edits touching the same file or contract.
- Fix root causes, preserve unrelated changes, add tests proportional to risk, and run the lightest relevant Docker validation.
- End by updating `.github/agent-state.local.md`, then report changes, verification, skipped checks, and remaining risk.
