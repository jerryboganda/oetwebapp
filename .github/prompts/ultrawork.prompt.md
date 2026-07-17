---
name: "ultrawork"
description: "Run the full OET OmO autonomous loop: explore, plan, delegate, implement, review, validate, and continue until complete or blocked."
agent: "OET OmO Orchestrator"
argument-hint: "Goal to complete end to end"
tools: ["agent", "read", "search", "edit", "execute", "web", "todo"]
---

Run ultrawork for this goal: `${input:goal:Describe the task}`.

First read `AGENTS.md`, `PROGRESS.md`, and `.github/agent-state.local.md` if present. Continue from the state file only when it matches this goal; otherwise update it to this goal. Then perform evidence-based planning using only relevant domain docs, current code, tests, local patterns, and useful web documentation. Include acceptance criteria, touched files/contracts, risks, rejected approaches, and validation matrix. Enter autopilot implementation mode automatically; do not stop at the plan or offer manual proceed handoffs. Spawn only registered specialist agents from the Orchestrator allowlist when useful. Ask popup-style questions only when a missing user decision blocks correctness, and include recommended options. Implement, verify with lightweight host checks, review, fix confirmed issues, update `.github/agent-state.local.md`, and continue until complete or genuinely blocked.