---
name: "qa-validate"
description: "Select and run the smallest credible validation matrix for a change."
agent: "OET QA Validator"
argument-hint: "What changed or what needs proof"
tools: ["read", "search", "execute", "todo"]
---

Validate: `${input:change:Describe the change}`.

Choose the smallest credible host check from the validation ladder, run it, and report what passed, what was skipped, and residual risk. Avoid heavy builds, full CI suites, or Docker/VPS validation unless the user explicitly asks.