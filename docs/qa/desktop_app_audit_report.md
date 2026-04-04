# Desktop App Audit Report

## Scope

- Audit date: 2026-04-02
- Workspace: `C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App`
- Requested focus: desktop launch stability, root-cause analysis, learner/expert/admin validation, and verified run guidance

## Detected Desktop Stack

- Desktop shell: Electron
- Electron entrypoint: [electron/main.cjs](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\electron\main.cjs)
- Preload bridge: [electron/preload.cjs](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\electron\preload.cjs)
- Desktop packaging: `electron-builder`
- Packaging config: [electron-builder.config.cjs](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\electron-builder.config.cjs)
- Frontend renderer: Next.js App Router / React / TypeScript
- Backend API: ASP.NET Core / EF Core
- Desktop dev launcher: [scripts/electron-dev.cjs](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\scripts\electron-dev.cjs)
- Desktop build script: [scripts/desktop-dist.cjs](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\scripts\desktop-dist.cjs)
- Desktop E2E config: [playwright.desktop.config.ts](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\playwright.desktop.config.ts)

## Architecture Summary

### Desktop process model

- The Electron main process creates the BrowserWindow, applies navigation and external-link security policy, starts local packaged services when needed, and coordinates update checks.
- The Next.js app is the renderer for learner, expert, and admin product surfaces.
- Desktop-only capabilities are exposed through the preload bridge and typed in [types/desktop.d.ts](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\types\desktop.d.ts).

### Development startup path

- `npm run desktop:dev` runs [scripts/electron-dev.cjs](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\scripts\electron-dev.cjs).
- The dev desktop shell expects the Docker-backed local stack to be healthy first through [scripts/qa/assert-local-stack.mjs](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\scripts\qa\assert-local-stack.mjs).
- Verified dev renderer target: `http://localhost:3000`
- Verified dev backend target: `http://localhost:5198`

### Packaged startup path

- `npm run desktop:dist` builds the standalone renderer and bundled backend runtime.
- The packaging flow now persists a packaged runtime API target into `desktop-runtime-config.json` when `PUBLIC_API_BASE_URL`, `API_PROXY_TARGET_URL`, or an absolute `NEXT_PUBLIC_API_BASE_URL` is supplied.
- Electron Builder packages:
  - `.next/standalone` into `resources/standalone`
  - `.next/static` into `resources/standalone/.next/static`
  - `public` into `resources/standalone/public`
  - `desktop-backend-runtime` into `resources/backend-runtime`
- In packaged mode, [electron/main.cjs](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\electron\main.cjs) starts:
  - the standalone Next renderer on the packaged renderer port
  - the shared configured API target when one is available
  - otherwise the bundled backend on the packaged backend port
  - the BrowserWindow only after both readiness checks succeed

### Runtime isolation

- Electron isolates per-channel state using:
  - `ELECTRON_APPDATA_ROOT`
  - `ELECTRON_RUNTIME_CHANNEL`
- This keeps dev, test, packaged, and installed sessions isolated from each other.

## Required Services and Environment

### Development

- Docker Desktop stack from `docker-compose.desktop.yml`
- Renderer health must answer at `http://localhost:3000/api/health`
- Backend readiness must answer at `http://localhost:5198/health/ready`

### Packaged validation

- Local unsigned Windows packaging requires:
  - `ELECTRON_ALLOW_UNSIGNED_WINDOWS_BUILD=true`
- Verified installed test folder:
  - `C:\Users\Public\OETPrepTest8`

## Product Surfaces Confirmed in the Desktop Renderer

### Learner

- `/`
- `/study-plan`
- `/progress`
- `/readiness`
- `/reading`
- `/listening`
- `/writing`
- `/speaking`
- `/submissions`
- `/settings/profile`
- `/billing`
- `/mocks`
- `/diagnostic`

### Expert

- `/expert`
- `/expert/queue`
- `/expert/calibration`
- `/expert/metrics`
- `/expert/schedule`
- `/expert/learners`
- `/expert/learners/[learnerId]`
- `/expert/review/writing/[reviewRequestId]`
- `/expert/review/speaking/[reviewRequestId]`

### Admin

- `/admin`
- `/admin/content`
- `/admin/content/new`
- `/admin/content/[id]`
- `/admin/criteria`
- `/admin/taxonomy`
- `/admin/flags`
- `/admin/users`
- `/admin/users/[id]`
- `/admin/billing`
- `/admin/audit-logs`

## Existing Desktop Test and Validation Assets Found

- Desktop smoke tests:
  - [tests/e2e/desktop/electron-smoke.spec.ts](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\tests\e2e\desktop\electron-smoke.spec.ts)
  - [tests/e2e/desktop/electron-packaged-smoke.spec.ts](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\tests\e2e\desktop\electron-packaged-smoke.spec.ts)
- Extended surface validation:
  - [tests/e2e/desktop/electron-surface-validation.spec.ts](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\tests\e2e\desktop\electron-surface-validation.spec.ts)

## Failure Summary Reproduced During Audit

### Historical user-facing blocker

- Installed packaged shell showed:
  - `OET Prep failed to start`
- Startup blocker:
  - `Timed out waiting for the backend at http://127.0.0.1:5199/health/ready`

### Additional issues uncovered during audit

- Desktop tests could race the Electron renderer before visible UI had painted.
- Windows Playwright teardown could leak Electron process trees.
- Notification hub reconnect noise created false-negative E2E failures during route transitions and review submission.
- Learner speaking controls had a desktop viewport overlap defect.
- Packaged desktop auth could diverge from web auth because the installer always preferred a private bundled SQLite backend unless a shared API target was explicitly preserved.

## Root Causes Confirmed

### Packaged backend readiness failure

- The packaged backend runs on SQLite.
- [BackgroundJobProcessor.cs](C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\backend\src\OetLearner.Api\Services\BackgroundJobProcessor.cs) ordered queued jobs by `DateTimeOffset` in SQL.
- EF Core SQLite could not translate that ordering.
- Background startup processing failed, readiness never became healthy, and Electron timed out.

### Desktop test startup race

- `firstWindow()` plus `domcontentloaded` was not enough to guarantee a painted Electron UI.
- Some desktop tests hydrated session state and navigated while the renderer was still visually blank.

### Desktop teardown instability

- Previous Windows cleanup logic did not reliably kill the full Playwright Electron process tree.
- Longer serial runs could be poisoned by leaked Electron descendants.

### Notification reconnect false negatives

- The desktop notification center can emit benign SignalR long-poll reconnect errors during navigation and teardown.
- These were not user-facing product failures, but the diagnostics harness treated them as hard failures until explicitly classified.

### Packaged desktop auth mismatch

- The packaged shell previously always started a bundled ASP.NET Core backend backed by a private SQLite database.
- That backend seeds local demo accounts and can legitimately differ from the database behind the deployed web app.
- As a result, packaged desktop could return `Invalid email or password` for real web-app credentials even though the same credentials were valid on the web deployment.

## Current State After Audit

- Packaged backend readiness blocker: resolved
- Installed packaged startup: verified working
- Packaged desktop auth routing: resolved for builds that configure a shared API target
- Learner, expert, and admin desktop flows: validated through the full desktop suite
- Final desktop regression command:
  - `npx playwright test -c playwright.desktop.config.ts tests/e2e/desktop --reporter=line`
  - result: `14 passed (2.2m)`

## Known Issues Discovered During Audit

### Resolved

- Packaged startup blocked by SQLite `DateTimeOffset` ordering
- Desktop E2E startup race before UI paint
- Windows Electron process-tree cleanup instability
- False-negative notification reconnect failures in desktop E2E
- Learner speaking control overlap in desktop viewport
- Packaged auth mismatch between desktop SQLite demo data and the shared web-app account store

### Residual risks

- Future packaged SQLite queries can still regress if they rely on unsupported provider translations.
- Local QA packaging is verified through the unsigned path only; production signing remains outside this audit.
