# PROGRESS - Active Agent Continuity

Last updated: 2026-06-07

## Current Operating Goal

Implement the OET 2026 product portfolio plan on `feat/oet-2026-entitlement-conformance`, with GitHub Actions handling broad validation/build/lint/e2e gates after focused local TDD checks.

## Current State

- The canonical portfolio spec is now installed at `docs/OET_2026_Product_Portfolio_Claude_Code_Codex.md`.
- `AGENTS.md` and `.github/copilot-instructions.md` now point future agents to that spec before product, checkout, entitlement, dashboard, add-on, Tutor Book, or expiry work.
- OET 2026 seed conformance was tightened:
  - standalone private speaking products use canonical `speaking-1session` and `speaking-2sessions` plan codes.
  - extra speaking add-ons use distinct add-on codes `addon-speaking-1session` and `addon-speaking-2sessions`.
  - manifest tests pin the exact 22 product plan codes and 7 parent-required portfolio add-ons.
- Public OET 2026 catalog add-on output now filters to parent-required portfolio add-ons only, so standalone AI packages do not leak into the portfolio add-ons reference.
- OET parent-required add-ons are no longer published as standalone content packages by the seeder; existing generated packages are drafted/internalized.
- Real billing quote/session creation now enforces `IAddonEligibilityService` for parent-required add-ons, carries `parentSubscriptionId` through GET quote and checkout session paths, requires explicit parent selection when multiple eligible enrolments exist, rejects ineligible/wrong parents, and blocks Tutor Book bundle double charges for learners who already own Tutor Book.
- Checkout fulfillment now applies add-on grants to the quote-selected subscription instead of the first subscription for the learner.
- Learner dashboard tasks are filtered by `enabledModules` from `/v1/me/entitlement-snapshot` once the snapshot loads, so tasks for non-purchased modules are hidden.
- Learner skill navigation is also filtered by entitlement modules, with `SpeakingSession` mapped to the Speaking tab.
- Admin portfolio export is available at `GET /v1/admin/billing/portfolio/export` for product, enrolment, entitlement counter, Tutor Book, and add-on history inspection.

## Validation

- `pnpm exec vitest run app/page.test.tsx --reporter=dot`: passed, 6 files / 13 tests. Existing jsdom "navigation to another Document" notices appeared.
- `pnpm exec vitest run app/catalog/page.test.tsx app/billing/page.test.tsx components/billing/addon-purchase-modal.test.tsx --reporter=dot`: passed, 8 files / 58 tests. Existing jsdom navigation notices appeared.
- `pnpm exec vitest run app/page.test.tsx components/domain/__tests__/learner-ux-primitives.test.tsx --reporter=dot`: passed, 12 files / 52 tests. Existing jsdom navigation notices appeared.
- `git diff --check`: passed.
- Node manifest assertion against `backend/src/OetLearner.Api/Data/Seeds/oet-2026-catalog.json`: passed, 22 plans / 7 parent-required portfolio add-ons.
- Focused `dotnet test` attempts for catalog manifest/public catalog tests timed out locally before useful output. Per current rule, broad .NET/build/lint gates should run on GitHub Actions.
- Branch `feat/oet-2026-entitlement-conformance` was pushed to origin at commit `d11b7e10`.
- GitHub CLI Actions/PR follow-up is blocked locally because `gh auth status` reports no authenticated GitHub hosts and `gh pr list` requires `gh auth login` or `GH_TOKEN`.

## Next-Step Protocol For New Agent Runs

1. Read `AGENTS.md`, `.github/copilot-instructions.md`, this file, `.github/agent-state.local.md`, and `docs/OET_2026_Product_Portfolio_Claude_Code_Codex.md`.
2. Continue from `.github/agent-state.local.md` when it matches the newest request.
3. For validation/build/lint beyond focused pre-commit TDD checks, prefer GitHub Actions.
4. For production deploy, use GitHub Actions + GHCR images; do not build on the VPS.
5. Before handoff, update `.github/agent-state.local.md` with validation, blockers, and next concrete step.

## Active Risks

- Local .NET focused tests timed out; GitHub Actions must provide the authoritative backend build/test result after a PR is opened or workflows are manually dispatched from an authenticated GitHub session.
- Existing branch/workspace has an unrelated untracked `.codex/config.toml`; do not stage it unless explicitly requested.
