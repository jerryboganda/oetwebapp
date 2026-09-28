---
name: "qa-validate"
description: "Select and run the smallest credible validation matrix for a change."
agent: "OET QA Validator"
argument-hint: "What changed or what needs proof"
tools: ["read", "search", "execute", "todo"]
---

Validate: `${input:change:Describe the change}`.

Choose the smallest credible checks, run them on GitHub Actions only (push the branch or dispatch `qa-smoke.yml`; `pnpm run ship:gate` is the only local check, per AGENTS.md), and report what passed, what was skipped, and residual risk.