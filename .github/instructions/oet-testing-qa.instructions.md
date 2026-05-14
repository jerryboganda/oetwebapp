---
description: "Use when writing, updating, debugging, or reviewing Vitest, React Testing Library, Playwright, desktop E2E, or backend tests."
name: "OET Testing QA"
applyTo: ["**/*.test.ts", "**/*.test.tsx", "tests/**/*.ts", "playwright*.config.ts", "vitest*.ts", "vitest.setup.ts", "backend/**/*Tests/**/*.cs", "backend/**/*Test*.cs"]
---
# OET Testing And QA

- Test behavior and contracts, not implementation trivia.
- Use Arrange-Act-Assert structure and descriptive test names.
- Prefer `user-event` for user interactions; avoid `fireEvent` for async click flows.
- Avoid fuzzy selectors when labels and headings can collide. Prefer exact names, roles, or scoped queries.
- For motion components, use the existing Proxy/`stripMotion()` mocking pattern.
- Cover empty, null, boundary, permission, and error paths when logic changes.
- For Playwright, keep auth-state setup and role matrix expectations intact.
- For desktop E2E, validate Electron packaging/runtime assumptions separately from web-only flows.
- Do not skip or delete failing tests unless the test is genuinely obsolete and the replacement coverage is clear.
- If a full suite is too expensive, run focused tests plus the next useful broader check, then report the limit.