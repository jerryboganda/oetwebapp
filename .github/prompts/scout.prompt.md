---
name: "scout"
description: "Run read-only exploration and return the file map, patterns, risks, and validation path."
agent: "OET Explorer"
argument-hint: "What to find or map"
tools: ["read", "search"]
---

Scout this area read-only: `${input:target:Describe what to find}`.

Return relevant files, ownership boundaries, existing patterns, risks, and a recommended validation path. Do not edit.