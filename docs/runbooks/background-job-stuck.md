# Runbook — "A background job is stuck"

> Alert: **Stuck Job Alert** (`AdminStuckJobAlert`, severity **Critical**)
> Admin surface: `/admin/review-ops`
> Source of truth: `backend/src/OetLearner.Api/Services/BackgroundJobProcessor.cs`

## What the alert means

A row in the `BackgroundJobs` table has been sitting in state `Processing`
longer than the stale threshold. In practice this means the worker that claimed
the job died before it could finish — a blue/green deploy restarting the
container mid-job, or a single job hanging past its execution ceiling.

A learner-visible symptom usually accompanies it: an attempt result that never
arrives, a video that never finishes processing, or a reminder that never sends.

## Which jobs are affected

The processor dispatches these job types (see the switch in `ExecuteJobAsync`):

| Job type | Learner-visible effect when stuck |
| --- | --- |
| `WritingEvaluation` | Writing result never appears |
| `WritingModelAnswerGeneration` | Model answer never generated |
| `SpeakingEvaluation` / `SpeakingTranscription` | Speaking result never appears |
| `NotificationFanout` / `NotificationDigestDispatch` | Notifications/emails stop |
| `BillingDunningRetry` | Dunning retry delayed |
| `LiveClassRecording*` | Recording stuck in `Processing` |

## Thresholds (do not change without a review)

| Constant | Value | Meaning |
| --- | --- | --- |
| `MaxJobExecutionTime` | 20 min | Per-job cancellation ceiling. A hung AI call is cancelled at 20 min and treated as an ordinary failure. |
| `StuckJobRecoveryInterval` | 5 min | How often recovery sweeps for orphans. |
| `StuckJobStaleThreshold` | 30 min | A `Processing` row older than this is considered orphaned. |
| `StuckJobRetryMaxAge` | 24 h | Orphans older than this fail terminally **without** learner notification. |
| `maxRetries` | 3 | Attempts before a job is marked `Failed`. |

## Triage — in order

1. **Confirm the alert is not a one-off.** Since 12 Sep 2026 the alert is
   deduplicated by **(event, job type, hour bucket)** — see `EmitFailureNotificationsAsync`
   and `NotificationScheduling.BuildIncidentBucket`. One incident produces **one**
   alert per job type per hour, not one per job. Several alerts for *different*
   job types in the same hour means several distinct incidents.
2. **Identify the job type** from the alert message
   (`Background job type {Type} failed ...`).
3. **Look at the queue:**
   ```sql
   SELECT "Type", "State", count(*)
   FROM "BackgroundJobs"
   WHERE "State" = 'Processing'
   GROUP BY 1, 2
   ORDER BY 3 DESC;
   ```
   and for the oldest orphans:
   ```sql
   SELECT "Id", "Type", "LastTransitionAt", "RetryCount", "StatusMessage"
   FROM "BackgroundJobs"
   WHERE "State" = 'Processing'
   ORDER BY "LastTransitionAt"
   LIMIT 50;
   ```
4. **Check the worker logs** for `Job {JobId} of type {JobType} failed` and
   `was orphaned in Processing for {N} minutes`. A long-running AI call is the
   usual culprit — `WritingModelAnswerGeneration` uses extended thinking with up
   to 4 sequential attempts and is the heaviest job in the system.
5. **Check for duplicate cost.** A retried AI job re-runs the model call. Cross-check
   AI spend in `/ai-usage` (see `AiUsageAnalyticsService`) for the same window
   before concluding the spend is normal.

## Recovery

- **Do nothing if the sweep is working.** Recovery re-queues recoverable orphans
  with backoff and fails the rest. It runs at the **start** of every processing
  tick (12 Sep 2026 change) so it cannot be starved by a hung job.
- **If the queue is not draining**, restart the API container to clear a wedged
  worker. In-flight jobs are recovered by the next sweep; no manual row surgery
  is needed.
- **Never** delete `BackgroundJobs` rows to silence the alert. That orphans the
  learner-visible resource (attempt, recording, notification) permanently.

## Admin checklist when this recurs

1. Note the **job type** and **hour** from the alert.
2. Run the two queries above; record `Processing` count and oldest orphan age.
3. Pull worker logs for the affected job type in the same window.
4. Check `/ai-usage` for a spend spike in the same window.
5. If a specific job type repeatedly wedges, open an issue against that job's
   handler — the per-job ceiling is a safety net, not a fix.
6. Confirm the alert count matches the number of distinct (type, hour) pairs. If
   you see many alerts for the *same* type in the *same* hour, the dedupe
   regressed — check `NotificationScheduling.BuildIncidentBucket`.

## Prevention already in place

- Per-job 20-minute cancellation ceiling (`jobCts.CancelAfter`).
- Stuck-job sweep every 5 minutes, run before the claim loop.
- Hourly-bucketed alert deduplication.
- Notification dedupe keyed on `NotificationScheduling.BuildDedupeKey`.

## Related

- [`docs/ops/incident-response-runbook.md`](../ops/incident-response-runbook.md)
- [`docs/ops/observability-slo-checklist.md`](../ops/observability-slo-checklist.md) — "Queue backlog > 10 min"
- [`docs/AI-USAGE-POLICY.md`](../AI-USAGE-POLICY.md)
