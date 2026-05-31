---
name: "ralph-loop"
description: "Run a Ralph-style PRD.md / PROGRESS.md autonomous loop for this OET workspace."
agent: "RalphCopilot"
argument-hint: "Start, continue, resume, or describe the PRD task"
tools: ["agent", "read", "search", "edit", "execute", "web", "todo"]
---

Run the Ralph loop for: `${input:goal:continue the current PRD}`.

Read `AGENTS.md`, `.github/agent-state.local.md` if present, compact `PROGRESS.md`, and then only the relevant `PRD.md` sections. Treat `AGENTS.md` as higher priority for Docker/local/VPS rules. Execute one coherent task slice at a time, review it, update `.github/agent-state.local.md` and `PROGRESS.md` when appropriate, and continue until complete or blocked.