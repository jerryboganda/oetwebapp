---
name: "build-fix"
description: "Triage and fix build, lint, type-check, or test failures with validation on GitHub Actions."
agent: "OET Implementer"
argument-hint: "Paste the failure or describe the broken check"
tools: ["read", "search", "edit", "execute", "todo"]
---

Fix this failure: `${input:failure:Paste or describe the failure}`.

Find the root cause, make focused edits, and validate on GitHub Actions only (push the branch or dispatch `qa-smoke.yml`; `pnpm run ship:gate` and `pnpm run ax:check` are the only local checks, per AGENTS.md). Do not run local or VPS builds/tests. Once the run is green, record it with `pnpm run ax:record` and update `SESSION_STATE.md` — never mark a gate PASS without a run id.