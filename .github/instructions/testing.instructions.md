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
- One test project: `backend/tests/OetLearner.Api.Tests`. Put a new test in the folder of the
  domain under test (`Speaking/`, `Writing/`, `Billing/`, `Services/`, ...) with namespace
  `OetLearner.Api.Tests.<Folder>`. Do not add new test files at the project root.
- Reuse `Infrastructure/` before writing a private fake: `TestWebApplicationFactory` (and
  `FirstPartyAuthTestWebApplicationFactory`, `BunnyMockedWebApplicationFactory`),
  `NotificationTestDoubles`, `[PostgreSqlFact]` + `PostgreSqlTestDatabase` (needs
  `OET_TEST_POSTGRES_CONNECTION`, set in `qa-smoke.yml`). Put a double shared by several classes in
  `Infrastructure/`; never declare top-level helper types inside a `*Tests.cs` file.
- Gate live-provider tests with a `Skip` attribute, never an early `return` that reports a pass.
  No permanent `Skip`: delete the test or fix it.
- When moving or renaming a test class, update the CI filters that name it:
  `writing-rev8-ci.yml` `DOTNET_FILTER`, `ai-control-plane-tests.yml` `paths` and `--filter`,
  `rulebook-conformance.yml` `--filter`, `deploy.yml` `syntax-gate` filter, and the pinned
  classes in `qa-smoke.yml` (`PlacementEndpointsTests`, `AuthFlowsTests`).

## When to add tests

- Add or update focused tests for behavior changes and bug fixes. Reproduce a bug with a failing
  test before fixing where practical.

## Running tests (GitHub Actions only)

Tests never run on the local machine — see `AGENTS.md` § "GITHUB ACTIONS IS THE ONLY AUTHORIZED
COMPUTE ENVIRONMENT". Push the branch or `gh workflow run qa-smoke.yml --ref <branch>`; that runs
tsc, lint, vitest, build, the sharded `dotnet test` and the Playwright smoke. See
`validation.instructions.md` for which job runs which check.
