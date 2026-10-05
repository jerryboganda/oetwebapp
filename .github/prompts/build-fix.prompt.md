---
name: "build-fix"
description: "Triage and fix build, lint, type-check, or test failures with validation on GitHub Actions."
agent: "OET Implementer"
argument-hint: "Paste the failure or describe the broken check"
tools: ["read", "search", "edit", "execute", "todo"]
---

Fix this failure: `${input:failure:Paste or describe the failure}`.

Find the root cause, make focused edits, and fix and ship with `pnpm run ship` (the build compiles in `Build images`; there is no automated QA - owner directive 2026-10-06, the owner tests manually and reports bugs; `pnpm run ship:gate` and `pnpm run ax:check` are the only local checks, per AGENTS.md). Do not run local or VPS builds/tests. Record the finished build/deploy runs with `pnpm run ax:record` and update `SESSION_STATE.md` — never mark a gate PASS without a run id.