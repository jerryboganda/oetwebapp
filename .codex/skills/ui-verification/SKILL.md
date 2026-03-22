---
name: ui-verification
description: Verify frontend behavior after UI or route changes. Use when working on Next.js pages, shared shell components, forms, tables, charts, loading states, or browser-only logic.
---

# UI Verification

## Purpose
- Validate the changed surface and the adjacent UI states that users will actually see.
- Keep browser checks narrow and evidence-driven.

## Workflow
- Identify the touched route group and any shared shell pieces it depends on.
- Check loading, empty, error, success, and responsive states when relevant.
- Verify browser-only logic, localStorage behavior, and external fetch surfaces when the change touches them.
- Use the smallest test or browser flow that proves the change.

## Repo context
- Shared shell and navigation live under `src/Component/Layouts`.
- Theme and layout state are stored in localStorage via `src/Component/Customizer`.
- Data tables and charts are heavy, so verify them only when the change touches them.
- Route groups are numerous, so verify the exact route path you changed.

## Output
- Report what was checked, what passed, and what still needs follow-up.
- Include commands or browser steps only when they matter to the result.
- Mention the exact files that own the verified behavior.
- Do not widen the scope beyond the changed surface unless a regression appears.
