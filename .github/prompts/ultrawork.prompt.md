---
name: "ultrawork"
description: "Run the full OET OmO autonomous loop: explore, plan, delegate, implement, review, validate, and continue until complete or blocked."
agent: "OET OmO Orchestrator"
argument-hint: "Goal to complete end to end"
tools: ["agent", "read", "search", "edit", "execute", "web", "todo"]
---

Run ultrawork for this goal: `${input:goal:Describe the task}`.

First perform evidence-based planning: inspect `AGENTS.md`, `docs/agent-operating-model.md`, relevant domain docs, current code, tests, local patterns, and useful web documentation before deciding the final plan. Include acceptance criteria, touched files/contracts, risks, rejected approaches, and validation matrix. Then enter autopilot implementation mode automatically; do not stop at the plan or offer manual proceed handoffs. Spawn only registered specialist agents from the Orchestrator allowlist when useful. Ask popup-style questions only when a missing user decision blocks correctness, and include recommended options. Implement, verify with Docker-safe checks, review, fix confirmed issues, and continue until complete or genuinely blocked.