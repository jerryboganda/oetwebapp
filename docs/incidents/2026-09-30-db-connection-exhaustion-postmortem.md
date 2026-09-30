# Postmortem — 2026-09-30: `53300 too many clients` bursts from Listening integrity events

Production API slots logged bursts of `Npgsql.PostgresException 53300: sorry,
too many clients already`, each lasting 1–2 minutes, several times in one
evening. While a burst ran, **every** endpoint of the affected process
(not only Listening) failed its database calls.

## Evidence (30 Sep 2026, UTC)

- API log lines containing `53300: sorry`: ~3,000 at 21:23–21:24 (green slot),
  58 at 21:32, 818 at 21:40 and 741 at 21:41 (blue slot).
- The failing request that started each burst was always
  `POST /v1/listening-papers/attempts/{attemptId}/integrity-events`, logged as
  `Concurrency conflict on POST …/integrity-events` (`DbUpdateConcurrencyException`,
  "expected to affect 1 row(s), but actually affected 0").
- Postgres log at 21:32:35: ~230 new client connections refused within 1.3 s,
  then `canceling statement due to user request` on
  `UPDATE "ListeningAttempts" SET "LastActivityAt" …` (clients giving up).

## Root cause — three defects that only hurt together

1. **The integrity-event write raced the attempt's `RowVersion` token.**
   `ListeningAttempt.RowVersion` is a `[ConcurrencyCheck]` token that autosave,
   section-cursor and heartbeat writes bump. `RecordIntegrityEventAsync` loaded
   the attempt tracked and saved it, so EF added `AND "RowVersion" = @old` to
   its UPDATE even though it never bumps the token. Any autosave landing between
   the read and the write made the event fail with a **409 `retryable: true`**.
   The player posts one of these events per blur / focus / click / audio tick, so
   this happens constantly.
2. **The web client retried that telemetry.** `apiRequest` retries 5xx / 408 /
   429 / network errors — and any error body that says `retryable: true` — after
   exactly 1 s and 3 s, with no jitter. Every conflicted event was therefore
   replayed twice, all in the same two instants, each replay again racing the
   next autosave: synchronised retry waves.
3. **Nothing capped the app's database pool.** Npgsql's default
   `Maximum Pool Size` is 100 **per process**; Postgres' default
   `max_connections` is 100 for everything. Blue API, green API and the ai-worker
   share that one database, so a single process that opened ~97 connections in
   one wave took every slot and all endpoints in all processes got `53300`.

## Fix

| Where | Change |
|---|---|
| `ListeningLearnerService.RecordIntegrityEventAsync` | On relational providers the attempt row is touched with targeted `ExecuteUpdateAsync` statements that never carry the `RowVersion` predicate (same pattern as the Reading annotation autosave): `LastActivityAt`, the audio-cue timeline, and the `audio_playback_error` hold are set atomically; the hold is only released when it is still the one this endpoint raised. The AuditEvent is inserted separately. The in-memory test provider keeps the tracked save. |
| `lib/listening-api.ts` `recordListeningIntegrityEvent` | `maxRetries: 0`. A dropped telemetry event is fine; a retry storm is not. |
| `DatabaseConfiguration.ConfigureDbContext` | Npgsql connection strings get `Maximum Pool Size=25` unless the string already sets one (any alias). Three processes × 25 leaves ~20 slots free; a burst now queues inside its own pool (15 s acquire timeout) instead of starving the database. |

Tests: `ListeningAttemptEventLoggingTests` (SQLite twin — races a committed
`RowVersion` bump against an event, timeline bounds, hold/release rules),
`DatabaseConfigurationPoolTests`, `lib/__tests__/listening-integrity-event.test.ts`.
Each new test was confirmed red on the old code.

## Not changed

- Postgres `max_connections` stays at the default 100. Raising it needs a
  Postgres restart, and `docker-compose.production.yml` deliberately avoids
  restarting the database for config changes. Do it in a maintenance window if
  the pool cap ever proves too tight.
- Other mutation endpoints still surface a `RowVersion` conflict as a retryable
  409 — that is intended for real state changes (autosave, submit).

## How to check it stayed fixed

```bash
docker logs --since 24h oet-api-blue  2>&1 | grep -c "53300: sorry"        # expect 0
docker logs --since 24h oet-api-green 2>&1 | grep -c "Concurrency conflict on POST .*integrity-events"   # expect 0
```

An explicit `Maximum Pool Size` can still be set per environment by appending
`;Maximum Pool Size=N` to `ConnectionStrings__DefaultConnection`.
