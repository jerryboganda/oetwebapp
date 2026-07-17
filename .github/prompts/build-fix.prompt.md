---
name: "build-fix"
description: "Triage and fix build, lint, type-check, or test failures with a lightweight, targeted check."
agent: "OET Implementer"
argument-hint: "Paste the failure or describe the broken check"
tools: ["read", "search", "edit", "execute", "todo"]
---

Fix this failure: `${input:failure:Paste or describe the failure}`.

Find the root cause, make focused edits, and validate with the smallest credible host command (e.g. `pnpm exec tsc --noEmit`, `pnpm run lint`, one test). Avoid heavy Docker builds, full CI suites, or VPS validation unless the user explicitly asks.