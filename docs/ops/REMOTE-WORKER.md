# Remote workers (OET-RWP/1): the API side

Owner decisions of 2026-10-05 (D1-D7) bind this design. This page is the operator and maintainer summary of what the
**OET API** implements. The protocol itself (wire formats, state machines, conformance ids `RW-nnn`) is the OET-RWP/1 spec
that the helper agent and the fleet manager teams implement against; this page does not replace it.

Everything described here is **dark by default**: the tables exist after the deploy, nothing reads them, no route does
anything useful and no producer enqueues a job until the feature flags below are turned on. With the flags off the platform
behaves exactly as it did before this change.

## What it is

A pull-based job boundary between the primary VPS (the only host that talks to Postgres and `IFileStorage`) and rented
helper VPSs (one `Fleet.Agent` container each). Helpers hold no database, storage, provider or signing credentials, make no
AI calls, and keep job bytes on tmpfs only for the life of a job.

```
Helper agent  --HTTPS, per-node token-->  /v1/internal/remote-worker/*   (job plane; active API slot only)
Fleet manager --HTTPS, fleet credential-> /v1/internal/fleet/*           (service plane; loopback-reachable manager)
Owner         --admin session + unlock--> /v1/admin/remote-workers/*     (break-glass)
```

Routes are mapped only on PostgreSQL and never on the `ai-worker` (`OET_RUN_MODE=worker`). SQLite/InMemory hosts register
none of the services (`AddRemoteJobs(isNpgsql: false)` binds options only), because claim, the fenced completion and the
reaper are Postgres-only SQL (`FOR UPDATE SKIP LOCKED`, `clock_timestamp()`).

## Switches (all default OFF; an absent row is OFF)

Rows in `FeatureFlags` (seeded disabled at startup so `/admin/flags` lists them; an operator's choice is never overwritten):

| Key | Effect |
| --- | --- |
| `remote_jobs_enabled` | master switch for producers and `claim`; off = `204 disabled`, queued apply work falls back local within one reaper tick |
| `remote_jobs_kind_pdf_extract` | `pdf.extract` purpose `apply` |
| `remote_jobs_kind_pdf_extract_shadow` | `pdf.extract` purpose `shadow` (hash-only, never applied) |
| `remote_jobs_kind_companion_index_prep` | `companion.index-prep` |
| `remote_jobs_kind_media_audio_extract` | `media.audio-extract`: Live Class recordings over the 24 MiB single-call cap are extracted into mp3 chunks on a helper (also needs a pinned `RemoteJobs:Kinds:media.audio-extract:EngineVersion`) |
| `remote_jobs_kind_media_speaking_join` | `media.speaking-join`: optional precompute of the consented candidate-audio mp3 (also needs the `speaking_audio_assessment` flag and a pinned `RemoteJobs:Kinds:media.speaking-join:EngineVersion`) |
| `remote_fleet_service_enabled` | `/v1/internal/fleet/*` and the owner's read-only node list (off = `404 fleet_service_disabled`) |
| `remote_jobs_freeze_applies` | EMERGENCY: `complete` answers `503 applies_frozen` and cancels the job |

Flags are cached per process for at most 5 s. With the master flag off, `heartbeat`, `inputs`, `outputs`, `complete`,
`fail` and the applier keep working for leases that already exist; only `claim` and the producers stop.

Options (`RemoteJobs__<Name>` in .NET configuration; secrets are never configuration): `LeaseSeconds` (120),
`HeartbeatEverySeconds` (20), `NodeHeartbeatSeconds` (15), `NodeStaleAfterSeconds` (45), `NodeOfflineAfterSeconds` (600),
`MaxAttempts` (3), `ReleaseLimit` (5), `BackoffBaseSeconds`/`BackoffMaxSeconds`/`BackoffJitterPercent` (5/300/20),
`ReaperIntervalSeconds` (15), `ReaperBatch` (100), `FallbackAfterMinutes` (10), `FallbackHardAfterMinutes` (60),
`JobRetentionDays` (30), `DeferredResultRetentionDays` (7), `SpeakingJoinOutputTtlHours` (24), `IntegrityStrikeLimit`/`StrikeWindowMinutes` (3/60),
`FairShareGate` (false), `ClaimRatePerMinute` (60), `TokenTtlDays` (30), `FleetTokenTtlDays` (90),
`TokenRotationGraceSeconds` (3600), `FleetAllowedCidrs` (empty), `VerifySampleRate` (0), `Kinds:<kind>:EngineVersion`.
Out-of-range values are clamped (`RemoteJobsOptions.Normalized`).

**Production sets them in `.env.production`, not in .NET syntax.** `docker-compose.production.yml` has a closed environment list
(no `env_file`), so it forwards each option explicitly, to both API slots and the `ai-worker`, as the upper-case key
`REMOTEJOBS__<NAME>` (for example `REMOTEJOBS__VERIFYSAMPLERATE=0.05`, `REMOTEJOBS__FAIRSHAREGATE=true`). A list takes indexed
keys: `REMOTEJOBS__FLEETALLOWEDCIDRS__0=172.18.0.0/16` (and `__1`); blank entries are ignored. A media kind's engine pin is
`REMOTEJOBS__KINDS__MEDIA_AUDIO_EXTRACT__ENGINEVERSION` / `REMOTEJOBS__KINDS__MEDIA_SPEAKING_JOIN__ENGINEVERSION`. Defaults
in the compose file equal the code defaults (keep them in step by hand; the test source that compares them is a manual tool,
no CI runs it); an option that is not in that list cannot be set in production. `validate-production-env.sh` rejects a malformed `REMOTEJOBS__VERIFYSAMPLERATE` (must be 0 to
1), `REMOTEJOBS__CLAIMRATEPERMINUTE` (integer >= 1), `REMOTEJOBS__SPEAKINGJOINOUTPUTTTLHOURS` (integer >= 1) or
`REMOTEJOBS__FAIRSHAREGATE` (true/false). Changing one needs a normal
deploy (a container recreate) to take effect.

## Bring-up order (owner)

1. Deploy (migration `20270110090000_AddRemoteWorkersAndJobs` creates `RemoteWorkers`, `RemoteCredentials`, `RemoteJobs`,
   `RemoteJobOutputs`; additive, idempotent, previous slot unaffected).
2. Turn on `remote_fleet_service_enabled`. Mint the fleet credential:
   `POST /v1/admin/remote-workers/fleet-credential` (system-admin + owner unlock ticket). The value is shown once; place it
   in the manager's `/run/secrets/fleet_api_credential`. Rotation keeps the old credential valid for a grace period.
3. The manager registers a node (`POST /v1/internal/fleet/nodes`, idempotent on `nodeRef`; the node token is returned once,
   `tokens/rotate` issues another). The node starts `Pending`; its first accepted `workers/heartbeat` makes it `Probation`.
4. `POST /nodes/{id}/canary` enqueues the known-answer canary (embedded fixture, no learner data). Claims by a `Probation`
   node only ever receive canary jobs. A matching result sets `LastCanaryOk`; `POST /nodes/{id}/enable` then makes it `Active`
   (`409 canary_required` otherwise). A canary MISMATCH quarantines the node immediately. At most one canary is open
   (`Queued`/`Leased`) per node, enforced by the partial unique index `UX_RemoteJobs_OpenCanary` (`409 canary_in_progress`, also
   when a requeue of an old canary would reopen a second one). A canary no node claims within `FallbackAfterMinutes` is cancelled
   by the reaper (`canary_timeout`, audited `RemoteJob.CanaryTimeout`); just request a new one.
5. Turn on `remote_jobs_enabled` and `remote_jobs_kind_pdf_extract_shadow`. Helpers run hash-only shadow jobs; the
   ai-worker re-extracts each asset in-process (fresh) and writes `RemoteJob.ShadowMismatch` audit rows on any difference.
   Shadow reaches papers that are already fully extracted through a sweep, not through the extraction worker (which only
   visits papers that still have an uncached PDF): each minute the ai-worker enqueues up to 5 shadow jobs for PDF assets of
   non-archived papers that have no shadow job for the current engine version and settings (valid SHA-256, at most 100 MiB,
   only while a node can take the work). A job that failed or was already compared is not enqueued again by the sweep; an
   admin requeue, an engine bump or a settings change runs the asset again. Progress is
   `SELECT "ResultSummaryJson"->>'comparison', count(*) FROM "RemoteJobs" WHERE "Kind"='pdf.extract' AND "Purpose"='shadow' GROUP BY 1;`
   (no rows of `mismatch`, and the shadow count approaching the number of PDF assets, is the evidence).
   Review zero mismatches over real Listening Part B/C papers (the tracked corpus has none) before step 6.
6. Turn on `remote_jobs_kind_pdf_extract`. Set `REMOTEJOBS__VERIFYSAMPLERATE=0.05` in `.env.production` after cutover and deploy
   (the ai-worker re-checks a deterministic 5% of applied jobs; a mismatch audits, strikes the node and the in-process text
   replaces the helper's). The sampler scans the 50 oldest uncompared jobs of the last two days each minute, records the
   unselected ones as `comparison: not_sampled` so the window always advances to newer jobs, and re-extracts at most 3 sampled
   jobs per pass (shadow comparison: at most 3 per pass too). A job whose asset cannot be read is retried later, behind newer
   jobs; a vanished object is recorded `stale`. Check coverage with
   `SELECT "ResultSummaryJson"->>'comparison', count(*) FROM "RemoteJobs" WHERE "Kind"='pdf.extract' GROUP BY 1;`.
7. Optionally `remote_jobs_kind_companion_index_prep`.
8. Optionally the media kinds (section "Media kinds" below). They need a node whose agent image carries ffmpeg, the engine pin
   for the kind in `.env.production`, and the kind's flag; they never run on a PDF-only agent.

Kill switches, in order of severity: disable one kind flag; `remote_jobs_enabled` off; `remote_jobs_freeze_applies` on
(stops results being written); node `disable`/`drain`/`revoke` (manager or owner break-glass); stop the helper. Rollback of
the API (`production-deploy.yml -f sha=<previous>`) is safe: agents see 404/426 and back off (OET-RWP/1 section 5.6).

## State machines (summary)

Job: `Queued` -> `Leased` (claim, fence +1, attempt +1) -> `Succeeded` / `Failed` / `Quarantined` / back to `Queued`;
`Queued` -> `FallbackLocal` / `Cancelled`; terminal -> `Queued` only by a forced producer enqueue or an admin requeue (the
fence is never reset, so zombies of an earlier incarnation stay fenced).

* Every call on a job is guarded by `State='Leased' AND LeaseOwner AND FenceToken AND LeaseExpiresAt > clock_timestamp()`
  (database clock). An expired-but-unreaped lease cannot heartbeat, read inputs, write outputs or commit.
* `LeaseExpiresAt = LEAST(now + lease, DeadlineAt)`: a runaway job cannot hold a lease past its hard deadline.
* The **reaper** (`RemoteJobReaper`, every API process incl. the ai-worker; all statements are state-conditional and skip
  locked rows) is the ONLY code that turns an expired lease back into work (`claim` never selects a `Leased` row).
  R1 expiry (requeue with backoff, or `Quarantined` at the attempt cap), R2 fallback (queue wait ran out, or master off),
  R3 orphan outputs, R5 output expiry (the media kinds' binary outputs: a Speaking join after `SpeakingJoinOutputTtlHours`,
  any other output with its job row after `JobRetentionDays`; objects are deleted first, rows after), R4 retention (a row that
  still has outputs waits for R5, so no object is ever left without a row that names it). It never calls a hub or an AI
  provider.
* `complete` is one transaction: fenced CAS to `Succeeded` + the kind's applier; an applier exception rolls everything back
  and the job stays `Leased` (`503 apply_failed`, retryable). The same `(job, fence, resultSha256)` replays the stored outcome
  without running the applier. A different hash for the same fence is `409 result_conflict`.
* Attempts: incremented only at claim; refunded (while `ReleaseCount < ReleaseLimit`) only for `shutdown`, `drain`,
  `pressure_shed`, `protocol_mismatch`, node revoke/quarantine and orphan reconciliation. Three expiries end `Quarantined`
  (never auto-run locally: a poison PDF must not recreate the in-process crash loop).
* Node strikes (structural result defects, hash/engine mismatches, verify-sample mismatches): 3 within 60 min quarantine the
  node and requeue its leases with a refund. Content-class rejections (forbidden control characters) are NOT strikes: the
  job ends `Failed(content_rejected)` and the local path handles the asset.

Node: `Pending` -> `Probation` (first heartbeat) -> `Active` (canary + `enable`) <-> `Draining`/`Disabled`; any non-revoked ->
`Quarantined`; any -> `Revoked` (irreversible, credentials revoked, leases requeued with a refund, same transaction).
`PolicyRevision` bumps on EVERY policy or status change and is how agents learn of new desired state
(`X-Remote-Desired-Revision`, claim 200, `workers/heartbeat`).

## Job kinds and where the code lives

| Kind | Producer (API) | Handler (validator + applier) | Applier outcome |
| --- | --- | --- | --- |
| `pdf.extract` | `ContentTextExtractionWorker` -> `RemotePdfExtractionProducer` | `PdfExtractKindHandler` | merges ONE key (the `ContentPaperAsset` id) into `ContentPaper.ExtractedTextJson` with a compare-and-swap on the stored text AND `RowVersion` (+1), 3 attempts; `NoOp` for needsOcr/already cached; `Discarded` for a vanished or changed asset |
| `companion.index-prep` | `CompanionDocumentIndexer` -> `RemoteCompanionIndexPrepProducer` | `CompanionIndexPrepKindHandler` | `Deferred`: the validated result is parked in `RemoteJobs.ResultJson`; the reindex consumes it (embeddings, corpus guard and hash-gated commit stay on the API) |
| `media.audio-extract` | `LiveClassRecordingProcessingService` (transcribe stage) -> `RemoteAudioExtractionProducer` | `AudioExtractKindHandler` | `Applied`: the verified chunk manifest is written to `LiveClassRecordings.AudioChunksJson`; `Discarded` when the recording moved on (other stored file, real transcript arrived, deleted) |
| `media.speaking-join` | `RemoteSpeakingJoinSweeper` (ai-worker) -> `RemoteSpeakingJoinProducer`; consumed by `SpeakingAudioEvidenceService` | `SpeakingJoinKindHandler` | `Applied` writes nothing to a domain table (the output row + job ARE the record); `Discarded` when a clip was archived or erased meanwhile |

To add a kind: add a row to `RemoteJobKinds`, an `IRemoteKindHandler` (pure `Validate`, transactional `ApplyAsync`; it must not
call an AI provider or a hub), register it in `RemoteJobsServiceCollectionExtensions`, and write a producer. The lease, fence,
replay, strike, audit and reaper machinery is shared.

Rules the PDF producer follows: only the PdfPig tier is remote (`provider` `auto`/`pdfpig`; `azure`/`noop` stay local; OCR
never leaves the API); assets need a SHA-256 (backfilled with a streaming hash) and at most 100 MiB; the paper is either
all-remote or all-local; a `FallbackLocal` job runs locally only with primary headroom (cgroup v2 pressure) or after
`FallbackHardAfterMinutes`; an asset the local pass has given up on (listed under `extractionExhausted`) is not sent remote
either, and a remote apply clears that asset's failure marker and exhausted entry exactly as the local pass does. The producer
touches nothing on the paper to keep the worker rotating: the worker walks its candidates with an in-memory id cursor
(`ContentTextExtractionWorker`, 20 papers per tick), so a paper that remote jobs own is simply revisited on the next lap and
`ContentPaper.UpdatedAt` / `RowVersion` only change when text is really applied. The producer is resolved from the per-paper
scope the worker opens. Any failure of the remote path degrades to the unchanged local path.

## Media kinds (OET-RWP/1 sections 6.3 and 6.4)

Both are OFF by default and are never offered until their flag is on AND `REMOTEJOBS__KINDS__MEDIA_<KIND>__ENGINEVERSION` pins the
exact engine string the agent image reports (for example `ffmpeg:7.1.1/audio-extract:1`). Provider calls stay on the primary: no
media code references an AI gateway, a recorder or a key (a source-scan test fails the build otherwise), helpers hold no
credentials, and nothing here touches grading, credits or the Claude Max route.

### `media.audio-extract` (Live Class recordings)

Today a recording over 24 MiB cannot be transcribed at all (`ProcessTranscribeAsync` loads it into one `byte[]`). The transcribe
stage now behaves as follows; everything else about it is unchanged, including `LiveClasses.AiRecordingProcessingEnabled` (off =
the recording stays `Pending`, nothing is enqueued, nothing is called):

1. A recording that is small enough takes the single-call path exactly as before. The remote path is never consulted.
2. An oversize recording with no chunk manifest asks `IRemoteAudioExtraction.PlanAsync`. With the flag off, no pinned engine, no
   healthy node, an unsupported file type or any doubt the answer is "local" and the stage fails with the SAME message as before
   (there is deliberately no local ffmpeg chunker: that would put the CPU work back on the primary). Otherwise one idempotent job
   is enqueued (`ResourceType=LiveClassRecording`, input `media`, fingerprint = SHA-256 of the stored file, computed once) and the
   stage queues a continuation of itself 45 s later and finishes: the transcribe job never blocks and never burns its retries
   while a helper works. A terminal failure of the job (`no_audio_stream`, `duration_exceeded`, quarantine) fails the recording
   with the reason; a withdrawn job (`FallbackLocal`, `Cancelled`) is re-enqueued when a node can take it.
3. The completion applier verifies a contiguous, sample-exact manifest (6.3), writes it to `LiveClassRecordings.AudioChunksJson`
   in the same transaction that settles the job, and fills `DurationSeconds` only when it was 0. The chunks are NOT registered as
   `MediaAsset`s (nothing needs them as assets, no recording-retention worker exists to find them there, and they would show up in
   media listings); the manifest names their keys, which are server-side only (never in a DTO: guarded by a test).
4. With a manifest the stage makes ONE `IAiGatewayService` call per chunk (feature `class.recording.transcribe.v1`, so every call
   writes its own `AiUsageRecord` exactly like the single call), saves each chunk's transcript immediately (a retry resumes, it
   never pays twice), verifies each chunk's size and SHA-256 before sending it, and continues in a new job after 12 minutes of
   work so it never hits the 20-minute execution ceiling. The transcripts are joined in order into `TranscriptText`.
5. Delete-on-complete: after the transcript is saved the chunk audio is deleted (the transcripts stay in the manifest). A failed
   delete never fails the stage; reaper R5 removes what is left after `JobRetentionDays`. A chunk that vanished or changed
   drops the manifest and the recording is extracted again.

### `media.speaking-join` (candidate audio)

Replicates `FfmpegSpeakingAudioTranscoder.JoinToMp3Async` (`PcmJoiner` now lives in its own file `PcmJoiner.cs`, which the agent
link-compiles). The judge call (`openai-audio`) stays in `SpeakingAudioEvidenceService`.

* **Producer.** `RemoteSpeakingJoinSweeper` (ai-worker only, every 20 s) hands the sessions of waiting `speaking.grade`
  operations (queued, leased or scheduled for retry, at most two hours old) to `RemoteSpeakingJoinProducer`, which enqueues ONE job
  per session when all of these hold: master flag, kind flag, pinned engine, the `speaking_audio_assessment` flag, a healthy node,
  at most 64 clips of at most 16 MiB (64 MiB total) of the allowed audio types. It picks the clips through the SAME query as the
  audio stage (`SpeakingAudioClips`), after the same connectivity-chatter strip, so the key matches. A clip with no recorded hash
  (several Speaking paths leave it empty) is hashed once and the hash is recorded on its `MediaAsset`.
  Key = `(kind, apply, SpeakingSession, sessionId, sha256 of the ORDERED clip shas, engine, settings)`: per session, never
  content-addressed, so byte-identical audio of two learners gets two jobs and two outputs. `params` carry no identifier; the
  claim response shows the node only names, sizes, hashes and content types (never a storage key, recording id or session).
* **Consumer.** The audio stage asks `IRemoteSpeakingJoin.TryServeAsync` first; a hit must match the ordered clip hashes exactly,
  the flags must still be on, and the stored mp3 must match the hash the API itself verified at completion. Anything else, or any
  error, means the unchanged local join. The grade never waits for a remote job.
* **Learner-audio hygiene.** The derivative is deleted the moment it is used; reaper R5 deletes it after
  `SpeakingJoinOutputTtlHours`; `SpeakingAudioRetentionWorker` and a learner's erasure of a recording delete it with the clips
  (and withdraw an unclaimed job); the applier refuses (`Discarded`) a join whose clips were archived while the helper worked.
  It is NOT registered as a `SpeakingRecording`/`MediaAsset` linked to the session, because a recording row would make the audio
  stage join the derivative as if it were another clip: the `RemoteJobOutputs` row and the job (resource `SpeakingSession`) are
  the registry, and every cleanup path above walks it.

## Security and hygiene (what the code guarantees)

* Tokens `orw1_<id>_<secret>` / `ofs1_...`: 16-hex id, 43-char base64url secret, never a dot, at most 80 chars. Only SHA-256
  of the secret is stored; lookup by id; `CryptographicOperations.FixedTimeEquals`; unknown ids compare against a dummy hash;
  revoked/expired/unknown/wrong secret give the identical 401. 120 failed verifications per IP per minute -> 429 `Retry-After: 60`.
  Credential and node rows are cached (<= 5 s) only for the two heartbeat routes; everything else reads them uncached.
* Dedicated schemes (`RemoteWorker`, `FleetService`) selected explicitly by the `RemoteWorkerOnly` / `FleetServiceOnly`
  policies; the learner JWT pipeline short-circuits (`NoResult`) on our token prefixes, so a node token never reaches
  `OnTokenValidated`.
* Kinds, status and capacity are enforced from `RemoteWorkers` and the registry on the server; the agent's `kinds[]` only
  narrows the offer. Per-node limits are keyed by the verified node id (never by IP).
* Inputs stream through `IFileStorage.OpenReadWithMetadataAsync` with Range support after the database connection is
  released; the node never learns a storage key or asset id; a length that differs from the manifest is `409 stale_input`
  and the job is cancelled so the producer re-enqueues with a fresh fingerprint (a vanished object is the same `409`, also on
  the `HEAD` pre-flight, whichever storage provider is configured). Outputs are written to a private per-request temp key and
  moved onto the final key only after size and SHA-256 verify, so a failed re-PUT of an accepted output never damages the
  earlier object.
* Results are untrusted: strict UTF-8, depth-limited JSON, size caps, every hash recomputed from `pages`, character rules,
  fingerprint/engine echoes. Audit rows carry ids, kinds, outcomes, counts and hashes only, never content or secrets.

## Deliberate decisions and deviations from OET-RWP/1 (all safe readings; flagged for the spec owner)

1. **Instance supersede** (4.1.2 vs 4.7.2/5.6 conflict): the NEWER instance wins. `workers/heartbeat` adopts an instance whose
   reported `startedAt` is later (or when the current one is silent for 60 s) and gives the older `409 instance_superseded`;
   `claim` rejects a different instance only while the current one heartbeated within 60 s. Adds the column
   `RemoteWorkers.CurrentInstanceStartedAt`.
2. **Quarantined nodes may still claim and finish their own canary** (and nothing else): otherwise the rule "`enable` from
   `Quarantined` needs a fresh passing canary" could never be satisfied. Table 4.0 is otherwise followed exactly.
3. **Canary expectations are computed at runtime** by running `PdfPigPdfTextExtractor` (the oracle) over the embedded PDF
   instead of hard-coded constants (they cannot be generated without running code); a manual test source asserts the fixture
   yields text (nothing runs it automatically).
4. A secret never contains `_` (regenerated until true) so the issued token splits into exactly three parts (the base64url
   alphabet would otherwise sometimes contain `_`).
5. `IdempotencyKey` longer than 256 characters is replaced deterministically by a hash of its parts (companion keys can reach 259).
6. A repeated `revoke` converges (200) instead of `409`, so a retrying manager is idempotent.
7. Canary jobs and shadow jobs are never swept to `FallbackLocal` (they have no local path); a canary nobody claims is
   cancelled `canary_timeout` instead (reaper step R2b).
8. The companion consumer rebuilds drafts from the corpus rows once a result was consumed, so the source's metadata is still
   refreshed and missing embeddings retried on every reindex without a new extraction.
9. **Audio chunks are not `MediaAsset`s** (6.3 says the applier registers each chunk as one). They live only in the recording's
   manifest and are deleted once transcribed, which is stricter than a retention policy: no recording-retention worker exists to
   honour one, and the chunks carry learner voices. Reaper R5 is the backstop.
10. **The Speaking join is not registered on a `MediaAsset`/`SpeakingRecording`** (6.4 says it is linked to the session): it is
    keyed by session on its job and `RemoteJobOutputs` row, deleted on use, by TTL, and with the clips (retention sweep and
    erasure). See "Media kinds".
11. **No local chunker for oversize recordings** (3.7 lists a local fallback for the media kinds): extraction is net-new capability,
    not an offload, and doing it locally would put hundreds of MB of ffmpeg work on the primary. With no node, an oversize
    recording fails exactly as it did before this change.
12. **R5 and the R4 guard**: reaper R5 (output expiry) is not in the spec; it exists because R4 alone would delete a job row and
    leave its output objects behind. R4 now skips a row that still has outputs.
13. **Speaking join trigger**: the spec says jobs are enqueued "at session end"; the audio stage runs when the grade runs, so the
    precompute follows the queued `speaking.grade` operations instead (a hook inside the grade code was ruled out). The lead
    time is however long a grade waits (the Max lane is serial), so a hit rate below 100% is normal and costs nothing.

## Diagnostics

```sql
-- queue shape
SELECT "Kind","State",count(*) FROM "RemoteJobs" GROUP BY 1,2 ORDER BY 1,2;
-- stuck leases (should be empty right after a reaper pass)
SELECT "Id","LeaseOwner","LeaseExpiresAt" FROM "RemoteJobs" WHERE "State"='Leased' AND "LeaseExpiresAt" < clock_timestamp();
-- node health
SELECT "NodeRef","Status","LastHeartbeatAt","IntegrityStrikes","LastCanaryOk","PolicyRevision","AppliedRevision" FROM "RemoteWorkers";
-- recent remote audit trail
SELECT "OccurredAt","ActorId","Action","ResourceId","Details" FROM "AuditEvents" WHERE "Action" LIKE 'Remote%' ORDER BY "OccurredAt" DESC LIMIT 50;
```

The manager's `GET /v1/internal/fleet/stats` and `/status` expose the same facts without database access.

## Tests and verification status

**Not tested - owner QA.** No automated QA runs anywhere (owner directive 2026-10-06): the only automated check of this layer
is compilation in `Build images` (`dotnet publish`, plus the idempotent migration script generated from the same publish) and,
for a change under `Data/Migrations/**` or `LearnerDbContext.cs`, the pending-model-changes check in `speaking-ci.yml`
(`migrations-check`) - which is why the four remote entities are written into `LearnerDbContextModelSnapshot` by hand and must
stay identical to `LearnerDbContext.RemoteJobs.cs` (ADR 0001).

`backend/tests/OetLearner.Api.Tests/RemoteJobs/*` (xUnit; the `[PostgreSqlFact]` classes need
`OET_TEST_POSTGRES_CONNECTION`) are **inert manual tools** kept in git for the owner: no workflow runs them, agents do not run
them, and nothing in this repository may claim they passed. Conformance ids are in the test names or `[Trait("RW", "...")]`.
The media kinds' sources are `RemoteMediaKindValidatorTests` (pure), `RemoteMediaPostgresTests` (producers, completion,
serve/delete, sweeper, R5) and `RemoteMediaWiringTests` (migration, DI, source guards), plus
`LiveClasses/LiveClassRecordingChunkTranscriptionTests`, the remote-join cases of `Speaking/SpeakingAudioEvidenceServiceTests`
and the cleanup cases of `Speaking/SpeakingComplianceTests` - same status: manual tools, not run by any workflow.
The extractor golden-hash row in `RemoteEngineMigrationAndWiringTests` is a manual reminder to bump
`PdfTextEngine.LayoutRevision` when `PdfPigPdfTextExtractor` changes behaviour.
