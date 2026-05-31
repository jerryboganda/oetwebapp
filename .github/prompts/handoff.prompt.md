---
name: "handoff"
description: "Create a concise continuation handoff for the current work."
agent: "OET OmO Orchestrator"
argument-hint: "Current task or area"
tools: ["read", "search"]
---

Create a handoff for: `${input:task:current work}`.

Read `PROGRESS.md` and `.github/agent-state.local.md` if present. Include current goal, relevant files, completed work, open risks, validation evidence, blockers, and the next concrete step. Keep it concise, then update `.github/agent-state.local.md` with the same compact state.