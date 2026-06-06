---
name: "hyperplan"
description: "Create an adversarial multi-perspective plan before risky work."
agent: "OET OmO Orchestrator"
argument-hint: "Risky goal or architecture problem"
tools: ["agent", "read", "search", "web", "todo"]
---

Hyperplan this goal: `${input:goal:Describe the risky task}`.

Use planner, explorer, security, QA, reviewer, and external-docs perspectives. Ground the plan in repo files, tests, domain docs, and web research where external framework/library/API behavior matters. Return a converged plan with facts gathered, assumptions, rejected approaches, failure modes, validation, and user decisions needed. Do not edit files.