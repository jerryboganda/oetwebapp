---
name: "start-work"
description: "Create an execution-ready plan before implementation."
agent: "OET Planner"
argument-hint: "Goal or rough feature idea"
tools: ["read", "search", "web", "todo"]
---

Create a decision-complete plan for: `${input:goal:Describe the goal}`.

Read `AGENTS.md`, `SESSION_STATE.md`, `TASKS.json` and `PROGRESS.md`, run `pnpm run ax:next` to see the ready task, then inspect only the relevant docs, existing code, tests, and useful web documentation before planning. Ask only blocking questions. Include evidence gathered, scope, assumptions, likely files/contracts, implementation sequence, validation matrix, risks, rejected approaches, and any required user decision. Update `SESSION_STATE.md` and `TASKS.json` with the planned next step. Do not implement in this prompt.