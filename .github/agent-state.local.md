# Agent State - Full Platform Performance Optimization

Last updated: 2026-07-14

## Goal

Ship the evidence-backed frontend, backend, API, worker, storage, and database performance fixes,
then hand the measurement-gated follow-up work to ChatGPT Codex.

## Implemented

- Frontend: persistent authenticated notification/SignalR lifecycle, identity-safe feature-flag
  single-flight caching, TanStack Query caching for repeated learner reads, bounded leaderboard
  rendering, and lazy heavy imports for flags/select, Wavesurfer, SignalR, and charts.
- API/auth: one-command JWT validation with role/status fail-closed semantics, duplicate AuthService
  query removal, bounded request-scoped entitlement resolution, batched content hierarchy/access,
  and media authorization reduced from 13 commands to 3 on common paper assets.
- Learner/domain reads: bounded/projection/aggregate fixes across LearnerService, Vocabulary, Recalls,
  Engagement, campaigns, Marketplace, Sponsor, AI usage, billing metrics, pronunciation, mocks,
  adaptive selection, spaced repetition, gamification, and learner trends.
- Workers: SQL-side filtering/limits/aggregates/deletes, constant-query staleness/backfill, async
  chunk stitching, reduced TTS/recording buffers, and bounded bulk/folder import work.
- Runtime/providers: runtime-settings single-flight snapshots, async capability checks, and removal
  of request-path sync-over-async calls.
- Storage: async-first Local/S3 operations, multipart/streaming S3 writes, batched prefix deletes,
  preserved one-GET media reads, and no blocking S3 SDK waits or whole-file S3 memory buffers.
- Delivery/data: safe Nginx JSON/text compression, persisted hosted-checkout URL for zero-provider
  replay calls, concurrent ContentItems browse/provenance indexes, PostgreSQL atomic SKIP LOCKED job
  claiming with SQLite fallback, and O(N) Speaking card sampling.

## Measured Improvements

- Content hierarchy: 25 rules, 78 -> 2 database commands.
- Media authorization: common paper asset, 13 -> 3 commands; denied asset, 14 -> 2.
- JWT validation: 2 -> 1 command.
- Entitlements: constant 5-6 commands for 1 or 16 subscriptions; repeat in one request adds 0.
- Recalls today: 9 -> at most 3 commands.
- AI usage learner summary: 4 -> 1 aggregate; admin summary bounded to 5 queries/top 25.
- Billing metric upsert reads: 12 -> 1.
- S3 media read remains one GET; S3 sync waits and file-sized in-memory writes are zero.

## Validation

- `pnpm exec tsc --noEmit`: passed.
- Focused frontend performance tests: 77 passed; the two incomplete feature-nav mock failures were
  fixed and the affected file then passed 5/5.
- API project build: passed after fixing the ConversationOptions namespace regression.
- Final focused S3/schema/checkout/Speaking gate: 37/37 passed.
- Earlier focused backend waves passed their content/media/auth/entitlement/domain/learner/
  vocabulary/worker test filters.
- Independent learner-runtime review: no blockers.
- Independent worker-runtime review: cold-start Speaking runtime-settings issue fixed by awaiting
  runtime settings during startup before request routing.
- `git diff --check`: passed.

## Remaining Measurement-Gated Work For Codex

1. Measure production `/sign-in` bundle after deployment. If it still ships the authenticated provider
   tree, design a CSP-safe `(auth)`/workspace provider split; do not weaken nonce or auth behavior.
2. Profile authenticated learner navigation. Only pursue route-group file moves if the persistent
   notification provider does not remove reconnect/refetch churn.
3. Capture PostgreSQL `EXPLAIN (ANALYZE, BUFFERS)` for ContentItems search. DetailJson full-text/tsvector
   work requires product-approved search semantics; do not add an unhelpful title-only index.
4. Consider buffered analytics ingestion only with bounded channel capacity, shutdown draining,
   idempotency, and failure observability.
5. Consider parallel background-job execution only after two-replica idempotency/capacity tests.
6. Add production-PostgreSQL translation/integration coverage for date aggregates, ILIKE queries,
   concurrent indexes, and SKIP LOCKED; current focused relational coverage is primarily SQLite plus
   Npgsql SQL-shape checks.
7. Exercise authenticated production media, Vocabulary/Recalls, checkout replay, and notification
   navigation flows after deployment.

## Known Non-Blocking Risks

- Existing package advisories remain for Microsoft.OpenApi 2.4.1 and SQLitePCLRaw.lib.e_sqlite3
  2.1.11; dependency remediation was outside this performance task.
- Public provider-tree/force-dynamic changes, DetailJson FTS, analytics buffering, and worker
  parallelism were intentionally not implemented without measurement and safety prerequisites.

## Next Step

ChatGPT Codex should start from deployed `main`, read this file plus `AGENTS.md`, verify production
health and the Build & Deploy run, then execute the seven measurement-gated items above in order.
