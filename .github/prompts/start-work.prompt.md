---
name: "start-work"
description: "Create an execution-ready plan before implementation."
agent: "OET Planner"
argument-hint: "Goal or rough feature idea"
tools: ["read", "search", "web", "todo"]
---

Create a decision-complete plan for: `${input:goal:Describe the goal}`.

Read `AGENTS.md`, `PROGRESS.md`, and `.github/agent-state.local.md` if present, then inspect only the relevant docs, existing code, tests, and useful web documentation before planning. Ask only blocking questions. Include evidence gathered, scope, assumptions, likely files/contracts, implementation sequence, validation matrix, risks, rejected approaches, and any required user decision. Update `.github/agent-state.local.md` with the planned next step. Do not implement in this prompt.