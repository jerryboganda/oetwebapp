---
name: "handoff"
description: "Create a concise continuation handoff for the current work."
agent: "OET OmO Orchestrator"
argument-hint: "Current task or area"
tools: ["read", "search"]
---

Create a handoff for: `${input:task:current work}`.

Read `SESSION_STATE.md`, `TASKS.json` and `PROGRESS.md`. Record any newly finished GitHub Actions runs with `pnpm run ax:record` first, so every validation claim carries a real run id. Include current goal, relevant files, completed work, open risks, validation evidence, blockers, and the next concrete step. Keep it concise, then update `SESSION_STATE.md` and `TASKS.json` with the same compact state and make `pnpm run ax:check` pass.