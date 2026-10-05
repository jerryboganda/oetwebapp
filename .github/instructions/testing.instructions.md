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
- Vitest does not support Jest `--runInBand`; a single file is selected with a path argument
  (`vitest run <path>`). Per "Running tests" below, that command belongs to CI, not this machine.

## E2E (Playwright)

- Keep smoke specs fast and deterministic. Use stable selectors and explicit waits, not arbitrary sleeps.
- Desktop/mobile flows use the dedicated Playwright configs (`playwright.desktop.config.ts`, etc.).

## Backend (xUnit)

- Cover service logic, scoring, rulebook resolution, authorization, and error paths.
- Keep tests isolated; do not depend on shared mutable external state.
- One test project: `backend/tests/OetLearner.Api.Tests`. Put a new test in the folder of the
  domain under test with namespace `OetLearner.Api.Tests.<Folder>`. Do not add new test files at
  the project root.
  - Skills and exam: `Writing/`, `Speaking/`, `Listening/`, `Reading/`, `Mocks/`,
    `Assessment/` (scoring), `Readiness/`, `Planner/`, `FreeSamples/`, `Rulebook/`,
    `Pronunciation/`, `Conversation/` (AI conversation, ElevenLabs TTS/STT), `Recalls/`
    (recalls, vocabulary, SM-2).
  - Learner and staff: `Learner/`, `Expert/`, `Admin/`, `Classes/`, `LiveClasses/`,
    `Notifications/`.
  - Commerce and access: `Billing/` (checkout, payments, wallet, subscriptions, sponsor, AI
    package credits), `Entitlements/`, `VideoLibrary/`, `Content/` (papers, uploads, storage,
    media).
  - AI platform: `Services/` (gateway, providers, credentials, assistant), `Companion/`,
    `OwnerAgent/`.
  - Cross-cutting: `Auth/`, `Platform/` (migrations, runtime settings, endpoint inventory,
    health), `Observability/`.
  - Only shared fixtures stay at the root (`TestRuntimeSettingsProvider`,
    `SpeakingSettingsTestDefaults`, `InMemoryFileStorage`, `MemoryFileStorage`, `AssemblyInfo`),
    plus a few tests held back while open branches edit them.
  - A root type (namespace `OetLearner.Api.Tests`) is visible from every folder. A type in a
    folder needs `using OetLearner.Api.Tests.<Folder>;` from any other folder.
- Reuse `Infrastructure/` before writing a private fake: `TestWebApplicationFactory` (and
  `FirstPartyAuthTestWebApplicationFactory`, `BunnyMockedWebApplicationFactory`),
  `NotificationTestDoubles`, `[PostgreSqlFact]` + `PostgreSqlTestDatabase` (needs
  `OET_TEST_POSTGRES_CONNECTION`). Put a double shared by several classes in
  `Infrastructure/`; never declare top-level helper types inside a `*Tests.cs` file.
- Gate live-provider tests with a `Skip` attribute, never an early `return` that reports a pass.
  No permanent `Skip`: delete the test or fix it.
- Test source files are inert manual tools (owner directive 2026-10-06): no CI runs them, so moving or renaming a test
  class needs no CI filter change.

## When to add tests

- Add or update focused tests for behavior changes and bug fixes. Reproduce a bug with a failing
  test before fixing where practical.

## Running tests (GitHub Actions only)

Tests never run on the local machine - see `AGENTS.md` "GITHUB ACTIONS IS THE ONLY AUTHORIZED COMPUTE ENVIRONMENT" -
and they do not run in CI either (owner directive 2026-10-06, "NO AUTOMATED QA ANYWHERE"): the owner tests manually and
reports bugs. Writing a test with a fix is optional and never required to ship.
