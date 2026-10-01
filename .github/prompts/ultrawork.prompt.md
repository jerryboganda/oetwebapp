---
name: "ultrawork"
description: "Run the full OET OmO autonomous loop: explore, plan, delegate, implement, review, validate, and continue until complete or blocked."
agent: "OET OmO Orchestrator"
argument-hint: "Goal to complete end to end"
tools: ["agent", "read", "search", "edit", "execute", "web", "todo"]
---

Run ultrawork for this goal: `${input:goal:Describe the task}`.

First read `AGENTS.md`, `SESSION_STATE.md`, `TASKS.json` and `PROGRESS.md`. Continue from `SESSION_STATE.md` only when its Goal matches this goal; otherwise re-goal it with `pnpm run ax:init` and update `TASKS.json`. Then perform evidence-based planning using only relevant domain docs, current code, tests, local patterns, and useful web documentation. Include acceptance criteria, touched files/contracts, risks, rejected approaches, and validation matrix. Enter autopilot implementation mode automatically; do not stop at the plan or offer manual proceed handoffs. Spawn only registered specialist agents from the Orchestrator allowlist when useful. Ask popup-style questions only when a missing user decision blocks correctness, and include recommended options. Implement, verify on GitHub Actions only (per AGENTS.md; `pnpm run ship:gate` and `pnpm run ax:check` are the only local checks), review, fix confirmed issues, record real run ids with `pnpm run ax:record`, update `SESSION_STATE.md` and `TASKS.json`, and continue until complete or genuinely blocked.