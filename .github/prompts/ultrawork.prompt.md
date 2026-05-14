---
description: "Run a full autonomous OET implementation loop: explore, plan, implement, review, verify, and report."
argument-hint: "Goal to complete end to end"
agent: agent
---
# OET Ultrawork Loop

Run the full workspace automation loop for the requested goal.

1. Read `AGENTS.md` and matching `.github/instructions`.
2. Build a todo list.
3. Use read-only exploration before edits.
4. Implement focused changes using existing patterns.
5. Add or update tests when behavior changes.
6. Run the lightest sufficient validation, expanding when risk requires it.
7. Perform a review pass for security, OET invariants, regressions, and missing tests.
8. Finish with changed files, validation, skipped checks, and remaining risk.

Keep going until done or genuinely blocked.