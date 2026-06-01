---
name: "Testing And QA"
description: "Use when writing, updating, debugging, or reviewing Vitest, React Testing Library, Playwright, desktop E2E, or backend xUnit tests."
applyTo: "**/*.test.ts,**/*.test.tsx,tests/**,playwright*.config.ts,vitest.config.ts,backend/**/*.Tests.cs,backend/**/*Tests.cs"
---

# Testing And QA

Frameworks: Vitest + React Testing Library (frontend unit), Playwright (E2E/desktop), xUnit (backend).

## Frontend unit tests (Vitest + RTL)

- Test behavior and accessible output, not implementation details.
- Prefer `@testing-library/user-event` over `fireEvent` for realistic interaction.
- Query by role/label/text. Use exact, unambiguous selectors; avoid broad regex that can match
  multiple nodes.
- Mock at boundaries (network, `apiClient`, timers). Do not mock the unit under test.
- For `motion/react`, strip or mock animations in tests so async timing does not flake assertions.
- Vitest does not support Jest `--runInBand`. Run a single file by path:
  `pnpm test -- path/to/file.test.tsx`.

## E2E (Playwright)

- Keep smoke specs fast and deterministic. Use stable selectors and explicit waits, not arbitrary sleeps.
- Desktop/mobile flows use the dedicated Playwright configs (`playwright.desktop.config.ts`, etc.).

## Backend (xUnit)

- Cover service logic, scoring, rulebook resolution, authorization, and error paths.
- Keep tests isolated; do not depend on shared mutable external state.

## When to add tests

- Add or update focused tests for behavior changes and bug fixes. Reproduce a bug with a failing
  test before fixing where practical.

## Running tests (host)

All validation runs directly on the Windows host — see `validation.instructions.md` for the full
command ladder. Common commands:

```powershell
pnpm exec tsc --noEmit
pnpm run lint
pnpm test
pnpm run backend:test
pnpm run test:e2e:smoke
```
