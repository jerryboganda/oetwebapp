---
name: "code-review"
description: "Review changes for correctness, regressions, missing tests, and instruction violations."
agent: "OET Reviewer"
argument-hint: "Diff, files, or task to review"
tools: ["read", "search", "execute"]
---

Review: `${input:target:current changes}`.

Use findings-first review style. Prioritize bugs, regressions, missing tests, Docker/VPS instruction violations, and mission-critical OET invariant violations. Do not edit.