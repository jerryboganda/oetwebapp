---
name: "qa-validate"
description: "Select and run the smallest credible validation matrix for a change."
agent: "OET QA Validator"
argument-hint: "What changed or what needs proof"
tools: ["read", "search", "execute", "todo"]
---

Validate: `${input:change:Describe the change}`.

Choose the smallest credible checks, run only Docker-safe heavy commands, and report what passed, what was skipped, and residual risk.