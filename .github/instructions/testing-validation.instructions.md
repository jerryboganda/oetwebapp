---
name: "OET Testing And Validation"
description: "Use when: adding tests, fixing tests, validating changes, reviewing CI, running Playwright/Vitest/backend tests, or selecting a verification matrix."
applyTo: "**/*.test.ts,**/*.test.tsx,tests/**,playwright*.config.ts,vitest.config.ts,backend/**/*.Tests.cs,backend/**/*Tests.cs"
---

# Testing And Validation

- Use `@testing-library/user-event` for async UI interactions; avoid `fireEvent` for async click flows.
- Vitest globals are available. Do not add Jest-only flags such as `--runInBand`.
- Avoid ambiguous regex selectors when duplicate labels/headings exist; prefer exact text or role-scoped queries.
- Mock `motion/react` with the existing Proxy/strip-motion style used in the repo.
- Choose the smallest credible validation first, then broaden when shared contracts or user-facing flows changed.
- Heavy checks must run inside Docker: `docker exec oet-local-web npx tsc --noEmit`, `docker exec oet-local-web npm run lint`, `docker exec oet-local-web npm test`, `docker exec oet-local-web npm run build`, `docker exec oet-local-api dotnet build`, `docker exec oet-local-api dotnet test`.
- If Docker containers are unavailable, report the blocker instead of switching to host commands.