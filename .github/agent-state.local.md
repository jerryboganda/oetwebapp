# Agent State - Learner Performance Hotspots

Last updated: 2026-07-13

## Goal

Remove the highest-confidence learner-path performance costs without changing product behavior.

## Implemented

- Native-only Capacitor plugins now load after browser, desktop, and native guards instead of entering
  the initial web dependency graph.
- Local and S3 storage expose a combined asynchronous read with stream length; S3 uses one GET and
  keeps the response alive until ASP.NET disposes the stream.
- Stored media and Listening audio no longer call synchronous storage metadata methods on request
  paths, and only confirmed missing objects map to not-found or the next candidate extension.
- Program browsing now loads all track counts for the current page with one grouped EF Core query.
- Added focused frontend, storage, endpoint, and relational query-count regression tests.

## Validation

- `pnpm test -- lib/__tests__/mobile-runtime.test.ts`: passed, 3 tests.
- Focused storage/listening backend tests: passed, 14 tests.
- `ContentAccessServiceTests`: passed, 3 tests.
- `git diff --check`: passed.
- Independent diff review: no blocking findings.

## Blockers

- None.
- Bundle chunk composition and production S3 behavior remain CI/live verification concerns because
  production builds run in GitHub Actions and local tests use Local/in-memory storage.

## Next Step

After the Ship-It deployment, verify learner media playback and record the production web chunk
delta; then profile SignalR reconnects before considering the deferred shared learner layout.
