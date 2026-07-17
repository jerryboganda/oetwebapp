---
name: "tdd"
description: "Run RED → GREEN → REFACTOR for OET changes."
agent: "OET Implementer"
argument-hint: "Feature or behavior to implement"
tools: ["read", "search", "edit", "execute", "todo"]
---

Implement using TDD for: `${input:goal:Describe the behavior}`.

1. **RED** — write a failing test describing the desired OET behavior.
2. **GREEN** — write the minimum implementation to pass.
3. **REFACTOR** — clean up while keeping tests green.

Focus on behavior, not implementation details. Use existing OET patterns and helpers. Run the focused test after each phase; avoid heavy full-suite runs unless the user asks.
