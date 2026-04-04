# Desktop App Final Validation Report

## Status

- Audit date: 2026-04-02
- Final state: completed
- Readiness assessment: desktop app is validated for local development QA and packaged Windows startup verification

## What Was Broken

- The installed packaged desktop shell could not start because the bundled backend never reached readiness.
- Packaged desktop authentication could reject valid web-app credentials when the installer was using the bundled SQLite backend instead of the shared production API.
- Desktop end-to-end coverage was too narrow to confidently validate learner, expert, and admin flows.
- The desktop Playwright harness had two stability problems:
  - early interaction before the Electron renderer had visibly painted
  - incomplete Windows Electron process cleanup between tests

## What Was Fixed

- Repaired packaged backend startup by making the background job processor safe for SQLite provider limitations.
- Repaired packaged desktop auth routing so installed builds can target the same shared API as the web app instead of always authenticating against a private seeded SQLite database.
- Fixed the learner speaking-task control overlap in the desktop viewport.
- Hardened desktop Playwright startup readiness so tests wait for visible UI before acting.
- Hardened Windows Electron teardown with full process-tree cleanup.
- Improved diagnostics so benign notification reconnect noise does not create false-negative desktop failures.
- Added richer desktop route and workflow validation for learner, expert, and admin flows.

## What Was Tested

### Manual packaged validation

- Installed Windows shell launch from `C:\Users\Public\OETPrepTest8\OET Prep.exe`
- Bundled backend readiness at `http://127.0.0.1:5199/health/ready`

### Automated packaged desktop validation

- `npx playwright test -c playwright.desktop.config.ts tests/e2e/desktop/electron-packaged-smoke.spec.ts --reporter=line`

### Automated dev-shell desktop validation

- `npx playwright test -c playwright.desktop.config.ts tests/e2e/desktop/electron-smoke.spec.ts --reporter=line`
- `npx playwright test -c playwright.desktop.config.ts tests/e2e/desktop/electron-surface-validation.spec.ts --reporter=line`

### Focused authentication parity validation

- `npm.cmd test -- electron/__tests__/runtime-config.test.ts lib/__tests__/backend-proxy.test.ts`
- result: `9 passed`
- `npm.cmd run build`
- result: success

### Final regression command

- `npx playwright test -c playwright.desktop.config.ts tests/e2e/desktop --reporter=line`
- Final result: `14 passed (2.2m)`

## What Passed

- Packaged Windows startup and bundled backend readiness
- Packaged desktop smoke coverage
- Dev-shell startup and preload bridge exposure
- Learner session reuse across reload and relaunch
- Learner dashboard and route matrix
- Learner reading workflow
- Learner listening completion flow
- Learner writing autosave and submit flow
- Learner speaking record, pause, resume, and submit flow
- Expert protected routes
- Expert route matrix and learner/detail workspaces
- Expert writing draft save
- Expert speaking review submit flow
- Admin protected routes
- Admin route matrix and detail views
- Admin content creation flow
- Admin user credit modal flow
- Serial desktop regression run with no leaked Electron process trees

## Remaining Risks

- Local Windows packaging is verified only through the unsigned QA path; production signing is still a separate release requirement.
- SQLite remains a packaged-backend risk area if future queries introduce unsupported `DateTimeOffset` ordering or similar provider-specific translation issues.
- Packaged desktop and web auth parity now depends on a shared API URL being configured during packaging or launch; if no shared API is configured, packaged desktop intentionally falls back to the bundled demo backend.
- Notification reconnect noise still appears during some Electron transitions, but it is bounded, classified, and did not affect validated user flows.

## Final Assessment

- No critical launch blocker remains.
- No verified learner, expert, or admin desktop workflow failed in the final regression pass.
- The desktop app is in a stable, validated state for the audited local QA and packaged Windows startup scenarios.
