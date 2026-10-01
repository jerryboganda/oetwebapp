---
name: "ralph-loop"
description: "Run a Ralph-style PRD.md / PROGRESS.md autonomous loop for this OET workspace."
agent: "RalphCopilot"
argument-hint: "Start, continue, resume, or describe the PRD task"
tools: ["agent", "read", "search", "edit", "execute", "web", "todo"]
---

Run the Ralph loop for: `${input:goal:continue the current PRD}`.

Read `AGENTS.md`, `SESSION_STATE.md`, `TASKS.json`, compact `PROGRESS.md`, and then only the relevant `PRD.md` sections. Treat `AGENTS.md` as higher priority for compute-locality rules. Run `pnpm run ax:next` and execute one coherent task slice at a time, review it, move `TASKS.json` statuses as you go, and update `SESSION_STATE.md` with validation evidence (real run ids via `pnpm run ax:record`) and the next concrete step. Continue until complete or blocked.