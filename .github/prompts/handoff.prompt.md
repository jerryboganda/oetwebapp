---
name: "handoff"
description: "Create a concise continuation handoff for the current work."
agent: "OET OmO Orchestrator"
argument-hint: "Current task or area"
tools: ["read", "search"]
---

Create a handoff for: `${input:task:current work}`.

Include current goal, relevant files, completed work, open risks, validation evidence, blockers, and the next concrete step. Keep it concise.