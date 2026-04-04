# Desktop Audit Report

## Scope

- Audit date: 2026-04-02
- Primary baseline: [`docker-compose.desktop.yml`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/docker-compose.desktop.yml)
- Desktop shell under repair: [`electron/main.cjs`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/electron/main.cjs)
- Supported runtime targets:
  - `desktop:dev` against the Docker-backed local stack
  - `desktop:dist` packaged mode with bundled standalone renderer and bundled backend runtime

## Verified Baseline

- Frontend health: `http://localhost:3000/api/health`
- Backend health: `http://localhost:5198/health/ready`
- Seeded local accounts:
  - learner: `learner@oet-prep.dev`
  - expert: `expert@oet-prep.dev`
  - admin: `admin@oet-prep.dev`
  - shared password: `Password123!`

## Architecture Summary

- In dev mode, Electron now targets the Docker-backed renderer at `http://localhost:3000` and the Docker-backed API at `http://localhost:5198`. [`scripts/electron-dev.cjs`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/scripts/electron-dev.cjs) blocks startup until the local stack passes [`scripts/qa/assert-local-stack.mjs`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/scripts/qa/assert-local-stack.mjs).
- In packaged mode, Electron starts a bundled standalone Next.js server and a bundled self-contained ASP.NET backend. [`scripts/desktop-dist.cjs`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/scripts/desktop-dist.cjs) publishes the backend into `desktop-backend-runtime/` and packages it through Electron Builder.
- Electron runtime state is isolated by `ELECTRON_APPDATA_ROOT` and `ELECTRON_RUNTIME_CHANNEL`, which keeps dev, test, and packaged sessions from trampling each other.
- The preload bridge remains the only desktop integration surface for the renderer. Desktop-only instrumentation stays internal to Electron and Playwright.

## Root Cause Summary

- Shared provider drift caused auth and notification contracts to diverge between shell tests and the real app tree.
- Billing UI assumed add-on collections were always present and crashed on nullable plan data.
- Desktop reload and reopen behavior was unstable because renderer/appdata/session state was not isolated enough across runs.
- Packaged-mode startup had builder and pathing issues around standalone renderer assets, unsigned local Windows packaging, and bundled backend launch.
- Several dashboard, queue, billing, and notification queries translated on PostgreSQL but failed on bundled SQLite, breaking packaged learner, expert, and admin paths.
- Long-lived Docker auth state could leave privileged MFA seeds stale during repeated desktop QA runs.

## Fix Summary

- Shared provider tree was normalized in [`app/providers.tsx`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/app/providers.tsx), [`app/layout.tsx`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/app/layout.tsx), and [`app/expert/layout.tsx`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/app/expert/layout.tsx).
- Billing data is now null-safe in [`app/billing/page.tsx`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/app/billing/page.tsx).
- Electron now isolates runtime data roots, resolves dev and packaged URLs correctly, enforces desktop security policies, and launches the packaged backend cleanly in [`electron/main.cjs`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/electron/main.cjs).
- `desktop:dev` now aligns with the Docker-first validation baseline in [`scripts/electron-dev.cjs`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/scripts/electron-dev.cjs).
- Packaging flow was hardened in [`electron-builder.config.cjs`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/electron-builder.config.cjs) and [`scripts/desktop-dist.cjs`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/scripts/desktop-dist.cjs).
- SQLite-safe service fallbacks were added in:
  - [`backend/src/OetLearner.Api/Services/LearnerService.cs`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/backend/src/OetLearner.Api/Services/LearnerService.cs)
  - [`backend/src/OetLearner.Api/Services/ExpertService.cs`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/backend/src/OetLearner.Api/Services/ExpertService.cs)
  - [`backend/src/OetLearner.Api/Services/AdminService.cs`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/backend/src/OetLearner.Api/Services/AdminService.cs)
  - [`backend/src/OetLearner.Api/Services/NotificationService.cs`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/backend/src/OetLearner.Api/Services/NotificationService.cs)
- Desktop-specific Playwright coverage was added under [`tests/e2e/desktop`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/tests/e2e/desktop).
- Privileged Playwright auth bootstrapping now seeds fresh role sessions during setup and reuses them across worker processes, which removed the remaining expert MFA concurrency failure from the seeded browser role-smoke matrix.

## Validation Snapshot

- Backend tests: `149 passed`
- Vitest: `101 passed`
- Lint: passed
- Next build: passed
- Desktop Playwright suite: `8 passed`
- Seeded browser role smoke: `48 passed`, `90 skipped`
- Packaged local unsigned smoke: passed as part of the desktop suite

## Linked Deliverables

- Issue register: [`docs/qa/desktop-issue-register.md`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/docs/qa/desktop-issue-register.md)
- Implementation summary: [`docs/qa/desktop-implementation-summary.md`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/docs/qa/desktop-implementation-summary.md)
- E2E report: [`docs/qa/desktop-e2e-report.md`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/docs/qa/desktop-e2e-report.md)
- Run instructions: [`docs/qa/desktop-run-instructions.md`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/docs/qa/desktop-run-instructions.md)
- Remaining risks: [`docs/qa/desktop-risks-and-limitations.md`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/docs/qa/desktop-risks-and-limitations.md)
