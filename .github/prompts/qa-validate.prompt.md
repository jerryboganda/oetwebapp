---
description: "Choose and run the right validation checks for recent OET changes."
argument-hint: "Changed area or validation goal"
agent: agent
---
# OET QA Validate

Select the lightest sufficient validation ladder for the requested scope.

Use this order unless the changed files clearly need a different path:

1. Parse changed JSON/YAML/config.
2. Run focused tests for changed behavior.
3. Run `npx tsc --noEmit` for TypeScript changes.
4. Run `npm run lint` for frontend/shared changes.
5. Run `npm test` for shared behavior or broad UI changes.
6. Run `npm run backend:build` and `npm run backend:test` for backend changes.
7. Run `npm run build` for Next.js build-risk changes.
8. Run Playwright smoke/E2E for runtime flows.

Report commands, results, skipped checks, and remaining risk.