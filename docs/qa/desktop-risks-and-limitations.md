# Desktop Remaining Risks and Limitations

## External Release Dependencies

- Signed Windows release validation is still pending because no production signing certificate or signing service credentials were available in this audit.
- Auto-update validation is still pending because no live update endpoint or release metadata service was available.
- Local unsigned packaging proves build integrity only. It is not a substitute for signed installer trust validation.

## Test Scope Limits

- The Electron suite is intentionally smoke-level and does not replace the broader browser E2E matrix.
- Learner desktop coverage now includes one real reading workflow, but writing, speaking, listening, and broader admin and expert mutation depth still lean on the browser role-smoke suites.
- Recovery coverage is focused on the reproduced local Docker and packaged issues, not every possible host firewall, corporate proxy, or OS policy variation.

## Environment Assumptions

- The validated dev baseline assumes Docker Desktop and the compose stack in [`docker-compose.desktop.yml`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/docker-compose.desktop.yml).
- Seeded local data is disposable and may drift over time unless the Docker volumes are reset.
- The packaged backend currently validates against local bundled SQLite behavior and the included demo data only.

## Known Non-Blocking Technical Debt

- One existing nullable warning remains in [`backend/src/OetLearner.Api/Services/LearnerService.cs`](/C:/Users/Dr Faisal Maqsood PC/Desktop/New OET Web App/backend/src/OetLearner.Api/Services/LearnerService.cs).
- Unrelated worktree changes outside the desktop audit scope were intentionally preserved rather than reverted.

## Recommended Next Steps

1. Run one signed Windows build with real publisher credentials.
2. Exercise the same packaged smoke suite against a real update endpoint and release metadata feed.
3. Expand Electron learner coverage to one writing or speaking flow if desktop-specific editing behavior becomes a release-critical path.
4. Add a small CI wrapper that runs the desktop Playwright config after a packaged local build artifact is produced.
