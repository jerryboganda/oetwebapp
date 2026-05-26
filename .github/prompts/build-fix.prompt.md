---
name: "build-fix"
description: "Triage and fix build, lint, type-check, or test failures using Docker-safe validation."
agent: "OET Implementer"
argument-hint: "Paste the failure or describe the broken check"
tools: ["read", "search", "edit", "execute", "todo"]
---

Fix this failure: `${input:failure:Paste or describe the failure}`.

Find the root cause, make focused edits, and validate with Docker-approved commands only. Do not run host npm/dotnet or VPS validation.