---
name: "Testing And QA"
description: "Test code was deleted on 2026-10-08. Read before touching any test-shaped file: there are no tests to write or run."
applyTo: "**/*.test.ts,**/*.test.tsx,**/*.spec.ts,tests/**,playwright*.config.ts,vitest*.config.ts,backend/**/*Tests.cs"
---

# Testing And QA (none)

All test code was deleted on 2026-10-08 (owner directive). The owner tests the live product by hand and reports
bugs; the agent fixes them on demand and ships with `pnpm run ship`.

- No test project, `*.test.*` / `*.spec.*` file, `__tests__/` folder, or Vitest, Playwright, pytest or xUnit config
  exists. The last commit that had them is tagged `last-commit-with-tests` (`git show last-commit-with-tests:<path>`).
- Do not re-add tests, a test runner or a test workflow. `scripts/deploy/verify-pipeline-contract.mjs` (rule 3) fails any
  workflow that runs one.
- Never claim a test passed. Report verification as "not tested - owner QA", with the Build images run that compiled it.
- Static checks that replace tests: typecheck and lint (the `language-checks` job), the pipeline contract and its Claude
  Max route source scan (`pnpm run pipeline:check`), and `pnpm run ship:gate`.
