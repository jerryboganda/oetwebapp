---
name: "plan"
description: "Create a phased OET implementation plan before writing any code."
agent: "OET Planner"
argument-hint: "Goal or rough feature idea"
tools: ["read", "search", "web", "todo"]
---

Create a decision-complete plan for: `${input:goal:Describe the goal}`.

Read `AGENTS.md`, relevant domain docs, existing code, and tests. Break work into ordered, independently verifiable phases. Include reuse opportunities, dependencies, OET contract risks (scoring, rulebooks, auth, uploads), validation matrix using lightweight host checks, and definition of done. Do not implement in this prompt.
