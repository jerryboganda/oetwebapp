# Desktop App Fix Summary

## Files Modified

- `backend/src/OetLearner.Api/Services/BackgroundJobProcessor.cs`
- `app/speaking/task/[id]/page.tsx`
- `electron/main.cjs`
- `electron-builder.config.cjs`
- `electron/runtime-config.cjs`
- `electron/__tests__/runtime-config.test.ts`
- `tests/e2e/fixtures/auth-bootstrap.ts`
- `tests/e2e/fixtures/diagnostics.ts`
- `tests/e2e/desktop/electron-smoke.spec.ts`
- `tests/e2e/desktop/electron-packaged-smoke.spec.ts`
- `tests/e2e/desktop/electron-surface-validation.spec.ts`

## Files Added

- `tests/e2e/desktop/electron-surface-validation.spec.ts`
- `docs/qa/desktop_app_audit_report.md`
- `docs/qa/desktop_app_issue_register.md`
- `docs/qa/desktop_app_test_matrix.md`
- `docs/qa/desktop_app_fix_summary.md`
- `docs/qa/desktop_app_run_guide_verified.md`
- `docs/qa/desktop_app_final_validation_report.md`

## Why Each Change Was Necessary

### `backend/src/OetLearner.Api/Services/BackgroundJobProcessor.cs`

- Fixed the packaged backend startup blocker.
- SQLite could not translate the queued-job `DateTimeOffset` ordering used during background processor startup.
- The packaged backend never became ready, so Electron timed out and the desktop app failed to open.
- The fix keeps server-side ordering for non-SQLite providers and switches SQLite to bounded materialization plus in-memory due-job ordering.

### `app/speaking/task/[id]/page.tsx`

- Fixed a desktop viewport usability defect in the learner speaking task.
- The role-card and notes toggle cluster overlapped primary recording controls.
- Moving the floating control group lower restored clear access to start, pause, resume, and submit controls.

### `electron/main.cjs`

- Fixed packaged desktop authentication routing.
- The packaged shell no longer assumes that every installed build must authenticate against a private bundled SQLite backend.
- Startup now prefers a configured shared API base URL and only falls back to the bundled backend when no real API target is configured.

### `electron-builder.config.cjs`

- Added packaged runtime-config emission into the Electron resources directory during `afterPack`.
- This preserves the intended shared production API target inside the packaged app instead of losing it after build time.

### `electron/runtime-config.cjs`

- Added a focused helper for packaged runtime API resolution.
- Centralized validation of absolute API URLs, packaged runtime-config loading, and fallback rules between build-time config and runtime overrides.

### `electron/__tests__/runtime-config.test.ts`

- Added regression coverage for packaged auth routing rules.
- Verifies that relative `/api/backend` values stay local, absolute configured API URLs are preserved, runtime overrides take precedence, and invalid persisted config fails safely.

### `tests/e2e/fixtures/auth-bootstrap.ts`

- Stabilized seeded desktop-role session bootstrap during longer learner, expert, and admin runs.
- Added reusable session hydration support and stronger cached/bootstrap handling used by desktop workflows.

### `tests/e2e/fixtures/diagnostics.ts`

- Improved desktop diagnostics so the suite can distinguish real regressions from known-benign notification reconnect noise during route transitions and teardown.
- Added explicit client `4xx` capture and scoped ignore rules for the SignalR long-poll reconnect patterns observed in Electron.

### `tests/e2e/desktop/electron-smoke.spec.ts`

- Added a rendered-UI readiness poll after `firstWindow()` so smoke tests do not interact with a visually blank Electron window.
- Added explicit process-tree cleanup on Windows to prevent leaked Playwright Electron processes between tests.

### `tests/e2e/desktop/electron-packaged-smoke.spec.ts`

- Applied the same rendered-UI readiness and Windows cleanup hardening to packaged desktop smoke coverage.
- This keeps packaged validation aligned with how the user actually launches the installed Windows shell.

### `tests/e2e/desktop/electron-surface-validation.spec.ts`

- Added full desktop learner, expert, and admin route/workflow coverage.
- Hardened startup readiness and Windows teardown.
- Scoped notification reconnect allowances only to the learner, expert, and admin flows that genuinely hit benign transport noise during route transitions or review submission.

## Scripts, Config, and Packaging Changes Verified

- Rebuilt the packaged desktop with:
  - `ELECTRON_ALLOW_UNSIGNED_WINDOWS_BUILD=true`
  - `npm run desktop:dist`
- Refreshed the local installed validation folder used for manual packaged verification:
  - source: `dist\\desktop\\win-unpacked`
  - target: `C:\\Users\\Public\\OETPrepTest8`

## Environment and Runtime Notes

- No schema migration was introduced.
- No new runtime environment variable was required for the backend code fix itself.
- Packaged desktop auth parity with the web app now depends on supplying a real shared API URL at build time or runtime:
  - `PUBLIC_API_BASE_URL`
  - `API_PROXY_TARGET_URL`
  - absolute `NEXT_PUBLIC_API_BASE_URL`
- Local Windows packaging still requires the explicit unsigned-build override unless real signing credentials are supplied.
- Dev-shell validation still requires the Docker-backed local stack from `docker-compose.desktop.yml`.

## Important Risks and Follow-Up Items

- Future packaged SQLite queries that sort on `DateTimeOffset` can reintroduce backend startup or runtime failures.
- The notification hub reconnect behavior is currently benign in the validated flows, but it remains noisy during some Electron route transitions.
- Local QA packaging is verified; production release-signing remains a separate release-engineering concern.
