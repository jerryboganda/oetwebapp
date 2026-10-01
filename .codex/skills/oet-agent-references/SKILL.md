---
name: oet-agent-references
description: Use when working in the OET Prep Platform and needing to adapt the repo's local agent, prompt, or instruction material for Codex without loading the whole catalog.
---

# OET Agent References

Use this as the navigation skill for repo-local agent, prompt, and instruction material.

## Workflow

1. Treat `AGENTS.md`, `.github/copilot-instructions.md`, `SESSION_STATE.md` and `TASKS.json` as higher priority than imported references.
2. Load only the specific source file needed: `.github/agents/oet-*.agent.md`, `.github/agents/{atlas-prime-planner,superpowers-oet}.agent.md`, `.github/prompts/*.prompt.md`, or `.github/instructions/*.instructions.md`.
3. Adapt role or prompt wording to Codex tools before acting; do not follow Copilot-only tool names literally.
4. Keep OET domain docs authoritative for scoring, rulebooks, AI usage, uploads, result cards, runtime settings, admin UI, and deployment.
