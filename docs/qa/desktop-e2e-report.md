# Desktop E2E Test Report

## Environment

- Docker baseline:
  - renderer: `http://localhost:3000`
  - API: `http://localhost:5198`
- Packaged local validation:
  - renderer served from the packaged standalone app
  - backend served from the packaged self-contained runtime on `127.0.0.1`
- Validation date: 2026-04-02

## Executed Validation

| Area | Command or suite | Result |
| --- | --- | --- |
| Backend regression coverage | `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj` | `149 passed` |
| Frontend unit coverage | `npm test` | `101 passed` |
| Frontend lint | `npm run lint` | passed |
| Production build | `npm run build` | passed |
| Desktop Electron smoke | `npx playwright test -c playwright.desktop.config.ts --reporter=line` | `8 passed` |
| Seeded browser role smoke | learner, expert, admin, and shared smoke matrix | `48 passed`, `90 skipped` |

## Desktop Smoke Coverage

### Shared Shell

- Booted the sign-in shell and verified the secure desktop bridge.
- Checked renderer stability after reload and after full relaunch.
- Checked packaged renderer boot and bundled backend health endpoints.

### Learner

- Verified learner session persistence after reload and relaunch in dev mode.
- Verified learner session persistence after reload and relaunch in packaged mode.
- Exercised a real reading workflow in both dev and packaged shells:
  - open the seeded reading player
  - answer a question
  - flag and unflag review state
  - navigate forward and back
  - verify answer persistence inside the Electron shell

### Expert

- Signed into the expert role in both dev and packaged shells.
- Verified guarded navigation from the expert dashboard into the review queue.

### Admin

- Signed into the admin role in both dev and packaged shells.
- Verified guarded navigation from the admin dashboard into the content library.

### Recovery and Stability

- Added a test-only self-healing path for stale privileged MFA state during Docker-backed desktop runs.
- Seeded fresh role sessions during Playwright setup and reused them from disk in worker processes so the learner, expert, and admin smoke matrix no longer races privileged MFA bootstrap across workers.
- Preserved strict diagnostics for page errors, 5xx responses, and failed browser requests while filtering expected reconnect noise.

## Evidence Summary

- Desktop dev shell: green
- Desktop packaged shell: green
- Learner flow in-shell: green
- Expert guarded route in-shell: green
- Admin guarded route in-shell: green
- Session reload and relaunch persistence: green

## Known Coverage Limits

- The Electron suite is intentionally smoke-focused, not a full replacement for the broader browser matrix.
- Learner desktop coverage includes a concrete reading workflow today; other learner subtests still rely on the broader browser role-smoke suites for deeper mutation coverage.
- Packaged validation is local and unsigned only. It proves package integrity, not signed-release trust or live update behavior.
