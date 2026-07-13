# Agent State - Learner Performance Hotspots

Last updated: 2026-07-14

## Goal

Finish the measurement-gated follow-up to the full-platform performance pass without changing
search semantics, worker concurrency, or ingestion architecture unless production evidence warrants it.

## Implemented This Run

- Moved NotificationCenter, SignalR, product-tour code/styles, and LearnerPasteGuard behind the
  persistent authenticated workspace boundary. Auth/public routes retain runtime/mobile bridges,
  shell update controls, and the global toaster.
- Added static regression coverage that prevents the heavy workspace providers from returning to
  the root auth client graph.
- Added opt-in real-PostgreSQL tests for UTC date aggregates, ILIKE wildcard escaping, concurrent
  content-index execution/reentrancy, and atomic `SKIP LOCKED` job claiming.
- Added an isolated PostgreSQL 17 service to backend QA shards so provider tests execute in CI;
  local runs skip them when `OET_TEST_POSTGRES_CONNECTION` is absent.

## Production Evidence

- Baseline deployment `9429af5c` completed successfully in Build & Deploy run `29279971902`;
  app, API, readiness, database, migrations, jobs, and storage checks were healthy on the green slot.
- `/sign-in` was 28 JS responses, 1,715,708 decoded bytes / 472,526 transferred bytes and 58,232
  transferred CSS bytes. The tour chunk alone was 60,064 decoded / 19,501 transferred bytes plus
  2,914 transferred CSS bytes, which justified the authenticated workspace split.
- ContentItems contains only 49 rows. PostgreSQL organically used the new provenance index; browse
  scans correctly remained sequential at this size, while a forced plan proved the browse index is
  usable. ILIKE search completed in 0.385 ms, so pg_trgm/FTS remains unjustified.
- AnalyticsEvents has 1,705 live rows, 0 dead rows, 76 events in 24h, and a 10-events/minute peak;
  bounded buffering is unjustified. `pg_stat_statements` is not installed.
- Background jobs had 0 ready, 9 delayed, 0 processing; 24h completion p50 1.775s, p95 3.928s,
  max 28.376s, and 0 recent failures. Additional worker parallelism is unjustified.

## Validation

- `pnpm exec vitest run tests/static/frontend-heavy-imports.test.ts components/providers/__tests__/authenticated-notification-center.test.tsx --reporter=dot`: passed, 2 files / 6 tests.
- `git diff --check`: passed.
- Targeted .NET compilation was attempted twice but the current API project exceeded the local
  two-minute and five-minute caps without emitting a compiler error. A no-dependency attempt used
  a stale API binary and surfaced unrelated existing test-suite interface errors, so it is not valid
  evidence for or against this change. Provider execution is delegated to the new CI PostgreSQL job.

## Blockers / Remaining Risk

- Authenticated production learner/media/checkout/notification smoke cannot run on this host:
  the protected synthetic credentials are not present in host environment variables or the browser
  vault, and repo policy forbids reading the VPS credential file.
- Authenticated navigation profiling remains blocked for the same reason. The provider boundary
  stays mounted above page-local shells, so learner navigation should preserve NotificationCenter
  and SignalR state; production confirmation still requires an approved synthetic profile.

## Next Step

After the focused commit reaches production, measure `/sign-in` again to confirm the tour and
notification chunks are absent. Then run the authenticated smoke/profile only when a synthetic
production browser profile or host-provided smoke credentials are available.
