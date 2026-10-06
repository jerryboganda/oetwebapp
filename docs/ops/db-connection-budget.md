# Database connection budget

One Postgres container (`oet-postgres`) serves every app process. This page is the
arithmetic behind the per-process pool cap, what each process holds, and how to read it
off the live database. Background: [`2026-09-30 53300 postmortem`](../incidents/2026-09-30-db-connection-exhaustion-postmortem.md).

## Facts (all from the repo)

| Fact | Where |
|---|---|
| Npgsql `Maximum Pool Size` is capped at **25** per process unless the connection string sets one | `backend/src/OetLearner.Api/Data/DatabaseConfiguration.cs` (`DefaultMaxPoolSize`) |
| Three app processes share the one database: `oet-api-blue`, `oet-api-green`, `oet-ai-worker` (same image; the worker runs `OET_RUN_MODE=worker`) | `docker-compose.production.yml` |
| Postgres runs with the stock `max_connections` (100); compose only preloads `pg_stat_statements` | `docker-compose.production.yml` (`postgres.command`), postmortem "Not changed" |
| The Owner Agent Console reaches Postgres as role `oet_owner_agent`, `CONNECTION LIMIT 10` (the socat forwarder allows 16 children, the role limit is the binding one) | `scripts/ops/create-owner-agent-db-role.sql`, `docker-compose.agent-console.yml` |
| The audio and video leader locks each hold ONE dedicated session-level advisory-lock connection on the replica that wins, on the raw (un-capped) connection string; losers close theirs | `Services/Vocabulary/IAudioWorkerLeaderLock.cs`, `Services/VideoLibrary/IVideoWorkerLeaderLock.cs` |
| The `db-backup` sidecar and the CI migration step each use one short-lived connection | `docker-compose.production.yml` (`db-backup`), `scripts/deploy/apply-migrations-from-ci.sh` |

Not verifiable from the repo (check on the host, see "Measure" below): the live
`max_connections` and `superuser_reserved_connections` values. The default reserve is 3,
which leaves 97 slots for non-superuser roles.

## Budget

Worst case = every pool full at once.

| Consumer | Today (previous slot kept warm) | Previous slot retired |
|---|---|---|
| Active API slot | 25 | 25 |
| Previous API slot (idle but running its ~45 hosted services) | 25 | 0 |
| `ai-worker` | 25 | 25 |
| Pools subtotal | **75** | **50** |
| Owner Agent Console role | up to 10 | up to 10 |
| Leader locks (audio + video) | up to 2 | up to 2 |
| `db-backup` + migration step | up to 2 | up to 2 |
| **Worst-case total** | **89** | **64** |
| Spare of 97 | **8** | **33** |

Reading this table:

- A rollout always has all three pools alive for a while (the new slot starts before the
  old one is retired), so the 75 row is the **rollout-window** peak either way. Retiring
  the old slot is what stops it being the **steady state**.
- A burst inside one process queues in its own pool (Npgsql's 15 s acquire timeout)
  rather than taking slots from the others; that is the 25 cap's job. The cap should only
  be raised if `pg_stat_activity` shows a process pinned at 25 with waiters, and then
  together with a Postgres `max_connections` change in a maintenance window.

## What the previous-slot setting does today

- `scripts/deploy/rollout-release.sh` (the manual, digest-pinned incident path) now
  stops the previous slot after a healthy, recorded rollout:
  `KEEP_PREVIOUS_SLOT_RUNNING` defaults to **false** (owner directive 2026-10-05). The stop
  is best effort and happens after the public gates, so every automatic router rollback
  still runs while the previous slot is up. Set `KEEP_PREVIOUS_SLOT_RUNNING=true` to keep
  it warm for one rollout.
- **The live pipeline path does not read that variable, on purpose.** `production-deploy.yml`
  runs `scripts/deploy/auto-deploy-ghcr.sh`, which never stops the previous slot (its closing
  line says it is "kept for rollback"), so the production steady state is still the
  left-hand column above. The 2026-10-05 approval covers the variable's default on the
  manual path only. Extending it to the live path is a separate owner decision with two
  costs:
  1. It defeats the smart component reuse of 2026-10-06. `service_matches` treats a
     non-running container as "update", so with the previous slot always stopped every
     release would force-recreate `learner-api-<target>` (about 70 s of API start on the
     prepare path) even for a web-only change, instead of only the first web-only release
     after an API change (`DEPLOY_REUSE service=learner-api-<slot>` would never print).
  2. It kills the old slot's in-flight long-lived connections (uploads, SignalR, live
     exams) right after cutover.
  If the owner wants it, the shape is one best-effort `compose stop` after the
  `DEPLOY_LIVE` line (`cutover_started=false` is already set there, so a failed stop can
  never trigger a router rollback); `scripts/deploy/verify-pipeline-contract.mjs` and the
  `guards` job's offline rollout fixtures cover that file, so it ships as its own reviewed
  change.
- `scripts/deploy/**`, `docker-compose.production.yml` and `docs/**` are not inputs of any
  image (`classifyInputs` in `release-manifest.mjs`; `buildInputParityFailures` checks it),
  so these edits start no build by themselves. They go live with the next API release.
  The new `Application Name` changes the compose config hash of both API slots and the
  `ai-worker` once: `service_matches` reports "update" for the target slot's API and the
  worker (the worker with its 90 s drain) and recreates only those; the other slot's API
  follows the next time it is the target. Nothing else is recreated.

## Rollback with a stopped previous slot

| Situation | Previous slot | What undoes it |
|---|---|---|
| A public gate fails inside a rollout | still running (the stop comes only after the release is recorded) | the script itself flips the routers back to the previous slot: `rollout-release.sh` re-creates the two routers for `$previous_slot`, `auto-deploy-ghcr.sh` runs `rollback_routers` |
| A release is found bad later | stopped (manual path) or running (live path) | `gh workflow run production-deploy.yml -f sha=<previous-sha>`: prepare targets the inactive slot, `service_matches` returns "update" for a non-running or missing container, `up --no-build --no-deps --pull never --force-recreate` recreates it from the immutable digest already in GHCR / on the host, the health gate runs, and promote has its own paired router rollback |
| The VPS reboots | a stopped slot stays stopped (`restart: unless-stopped` honours a manual stop) | nothing to do; the routers only reference the active slot, so nginx never resolves the stopped one |

## Process names

Each process now carries its own Npgsql `Application Name` (`oet-api-blue`,
`oet-api-green`, `oet-ai-worker`), set per service in `docker-compose.production.yml`.
Before this all three were indistinguishable in `pg_stat_activity` (and in the Postgres
log, where `%a` in `log_line_prefix` prints it). `pg_stat_statements` aggregates across
sessions and does not carry the name; use it for "which statements", and
`pg_stat_activity` for "which process". It is a plain connection-string keyword: no
behaviour change, no new connection.

## Load removed from the database by this change set

- `ContentTextExtractionWorker` runs only in the `ai-worker` (it ran in all three, each
  rewriting the same 20 `ContentPapers` rows every 10 minutes). A pass that extracts
  nothing now writes nothing.
- The freeze-lifecycle sweep ran on every 2-6 s tick in all three processes and loaded
  every Scheduled/Active freeze record. It now runs at most once a minute per process and
  its query only returns records that are actually due.
- `GamificationService.AwardXpAsync` no longer inserts and processes a no-op
  `AchievementCheck` job per XP award (one per answered Reading question) and saves once
  instead of twice.
- The Speaking transcription queue claims a row with a single atomic statement instead of
  read-then-write, and no longer shares a loop with the job pipeline.

## Measure (read-only SQL for the owner or ops)

```sql
-- Who holds the connections (needs the Application Name above)
SELECT application_name, state, count(*)
FROM pg_stat_activity
WHERE datname = current_database()
GROUP BY 1, 2
ORDER BY 3 DESC;

-- Headroom against the real settings
SELECT current_setting('max_connections')::int               AS max_connections,
       current_setting('superuser_reserved_connections')::int AS reserved,
       count(*)                                               AS in_use
FROM pg_stat_activity;

-- Heaviest statements. pg_stat_statements is preloaded by compose; if the view is
-- missing, CREATE EXTENSION IF NOT EXISTS pg_stat_statements; (owner action) first.
SELECT calls,
       round(total_exec_time::numeric, 0) AS total_ms,
       round(mean_exec_time::numeric, 2)  AS mean_ms,
       left(query, 120)                   AS query
FROM pg_stat_statements
ORDER BY total_exec_time DESC
LIMIT 20;
```

Record the result before and after any pool, `max_connections` or slot-retirement change;
numbers in the budget table above are worst-case arithmetic, not measurements.
