# Speaking Module — Incident Runbook

Severity definitions follow standard practice: Sev1 = customer-impacting major outage; Sev2 = degraded service; Sev3 = bug or quality issue. Every incident gets a post-mortem within 5 business days.

---

## Sev1: AI provider down (no AI assessment possible)

**Detection**: PagerDuty alert from `AiGatewayService` 5xx rate > 25% over 5 min; or `assessment_ready` SignalR events absent for > 10 min during peak hours.

**Immediate action**:
1. Page on-call Speaking + on-call Platform.
2. Flip `Features__SpeakingV2_AssessmentEnabled = false` so the UI stops promising "assessment in 12s" and instead shows "assessment queued".
3. Failover to secondary provider via `AiFeatureRouteResolver` (Anthropic → OpenAI fallback). For `speaking.grade` the first
   failover is built in (Claude subscription sidecar, then the default route): see "Sev2: Speaking grading slow or failing
   over" below.
4. Drain backlog after recovery using `SpeakingAiAssessmentService.RetryQueueAsync`.

**Communication template**:
> Status: Investigating. Some Speaking practice sessions are not receiving AI assessment immediately. Recorded sessions are safe and will be assessed once the upstream provider recovers. — Speaking team

---

## Sev1: PII exposure (recording or transcript leak)

**Detection**: Audit log shows admin access pattern outside policy; OR external report.

**Immediate action**:
1. Page Speaking + Security + Legal (CIRT).
2. Revoke all admin access tokens.
3. Rotate `LIVEKIT__APISECRET`, `AWS__SECRETACCESSKEY`.
4. Pull S3 access logs for the affected window.
5. Notify affected learners within 72h per GDPR Article 33 if confirmed breach.

---

## Sev2: LiveKit outage (live tutor rooms unavailable)

**Detection**: `SpeakingLiveRoomService` 5xx rate > 10%; or LiveKit Cloud status page red.

**Immediate action**:
1. Page on-call Speaking.
2. Auto-reschedule confirmed bookings via `MockBookingReminderWorker` to next available slot.
3. Notify booked learners + tutors via existing email pipeline.
4. Disable new bookings (`Features__PrivateSpeakingBookingsEnabled = false`).

---

## Sev2: Live voice provider failing ("The live AI patient could not start")

Added 2026-09-30 with the live voice close-out; details and numbers in [live-voice.md](live-voice.md) (pending production
verification). Before that, a provider failure was terminal for the session.

**Detection**: the production E2E fails with "no live voice provider could start the conversation"; log lines
`Live voice {Provider} session creation failed: class=... http=...` (Warning; **Error**, and so Sentry, for quota,
credentials and refusals such as an unknown model or URL); the admin health endpoint shows a provider `open`.

**What already happens**: the API offers the configured primary first, then the other; when a create call fails the browser
tries the next provider (Gemini answers in about 2-3 s). A provider's circuit opens for 10 minutes on quota or credential
failures, for its own `Retry-After` (5-120 s) on a rate limit, and for 60 s after two server or network failures within 60 s.
With no provider usable the pages load the recorder fallback (`liveVoiceAvailable=false`); a learner already on a card can
only retry.

**Immediate action**:
1. `GET /v1/admin/ai/live-voice/health`: per provider the circuit, the last failure (class, HTTP status, the provider's
   error type and code), the catalog probe and the counters. The failure log line has the request id and, for a 429 or a 5xx
   from the vendor host only, the redacted provider message.
2. Fix the cause: `quota_exhausted` is the provider project's billing or spend limit, `auth` a revoked or wrong key,
   `invalid_request` with 404 an unknown model or URL (`LIVEVOICE__*MODEL`, `LIVEVOICE__*BASEURL`).
3. Then `POST /v1/admin/ai/live-voice/{provider}/reset` closes the circuit at once (audited); otherwise the next real success
   after the open period closes it, and a restart clears it. The circuit is per API process.
4. `LIVEVOICE__PRIMARYPROVIDER` only changes which provider is tried first (an owner-approved env edit plus an API slot
   recreate, see live-voice.md); it is not needed to restore service while the other provider works. `?voiceProvider=` is
   honoured only for flagged QA accounts (`speaking_live_voice_pin:<learner user id>`, see
   [QA provider pin](live-voice.md#qa-provider-pin)); a normal learner cannot use it, and it is not a mitigation.

---

## Sev3: Learners waiting in the live Speaking queue ("You're in the queue")

Added 2026-10-05 with the live session admission gate; details in
[live-voice.md](live-voice.md#admission-control-live-session-cap-and-wait-queue).

**Detection**: learners report the queue screen; `GET /v1/admin/ai/live-voice/admission` (or `admission` in
`/v1/admin/ai/live-voice/health` and `/v1/admin/ops/snapshot`) shows `waiting > 0` with `free = 0`. This is the designed
behaviour above the cap (default 100 concurrent live AI sessions), not an outage: waiting learners hold **no credit** and run
**no clock**, and a place is taken by the same call that holds the credit.

**Immediate action**:
1. Read `admitted`, `waiting`, `oldestWaitSeconds`. A long `oldestWaitSeconds` with `admitted` far below the real number of
   live sessions means places are not freeing: exam or practice rows stuck in progress hold a place until they end or hit the
   safety TTL (exam 45 min, practice 20 min). Check the role-play sweep (`SpeakingExamAutoAdvanceWorker`, ai-worker) first.
2. Capacity is available (provider and VPS healthy) and the cap is the only limit: raise it,
   `PUT /v1/admin/ai/live-voice/admission {"maxConcurrent":150}` (no restart, audited, takes effect on the next decision).
3. The gate itself is suspect (learners cannot start although nothing is running, errors in
   `Live Speaking admission failed`): switch it off, `PUT /v1/admin/ai/live-voice/admission {"enabled":false}`. Waiters are
   let through on their next poll and nothing is counted until it is switched back on. The switch never waits on the decision
   lock (a waiting row is released by one UPDATE), so it works even when a lock holder is stuck; the gate's own lock wait is
   bounded at 5 s and then fails open. A running session is never cut off by any of these changes. The gate already fails
   open on its own errors. (The environment keys `SPEAKING__LIVEADMISSION__*` are NOT forwarded by the production compose file:
   the admin switch is the only lever, see [../env/speaking.md](../env/speaking.md#live-ai-session-admission-has-no-environment-keys-in-production).)
4. A learner reports "You already have a live Speaking session open or waiting" (409 `speaking_live_session_active`): one live
   place per learner is the designed rule. Their other place is a waiting row (frees by "Leave the queue" or after 90 s without
   a heartbeat) or a card/exam that is still running or in preparation (frees when it ends, is cancelled, or at the safety TTL:
   practice 20 min, exam 45 min). A learner who cannot pay is refused with the credit 402 and does not queue.
5. Do not touch `SpeakingLiveAdmissions` rows by hand: every decision expires stale rows itself, and the sweeper purges old
   ones.

---

## Sev2: Role-plays not ending / OpenAI sessions still billing

Added 2026-09-30 (hard duration cap; pending production verification).

**Detection**: OpenAI usage for role-plays far beyond their card time; ai-worker log lines (Error)
`Speaking role-play hard-stop sweep failed` or `Speaking role-play provider hang-up sweep failed`, (Warning)
`Failed to hard-stop overdue Speaking role-play {SessionId}` or
`Failed to hang up the provider sessions of ended Speaking role-play {SessionId}`; Active AI role-plays in the database
past their deadline + grace.

**Immediate action**:
1. The cap is the ai-worker's `SpeakingExamAutoAdvanceWorker` (container `oet-ai-worker`; every 20 s; an Active role-play is
   finished at deadline + 30 s, and rows more than 12 hours past that are left alone). No worker means no server-side cap:
   browsers still finish at 00:00 and the mint and write guards still hold, but an abandoned session stays Active.
2. Hang-up: read the ai-worker log for
   `OpenAI live session hang-up returned HTTP {status} for Speaking session {SessionId}`. 200 (or 2xx) means it works;
   every call answering 404 means the endpoint, which is not yet verified against the real provider, is probably wrong:
   rely on the OpenAI project spend limit.
   `OpenAI live session hang-up failed for Speaking session {SessionId}: {ErrorType}` is a transport fault or timeout.
   Each role-play is tried once per worker process (no retry); a worker restart repeats the ones at most 10 minutes past
   their hard stop.
3. An abandoned standalone practice role-play is finished but not graded: its 2-credit hold is refunded by the hourly
   stale-hold sweep after 24 hours and a free-sample use stays retryable. Abandoned exam cards are graded.

---

## Sev2: Speaking grading slow or failing over (Claude subscription sidecar)

Added 2026-09-30; chain, budgets and revert in [ai-providers.md](ai-providers.md).

**Detection**: the warning
`Speaking grading via pinned provider writing-claude-sub failed (<class>); falling back to the default route.` in the log of
the process that ran the grade (the ai-worker for queued grades, an API slot for a synchronous `/ai-assess`); learners see
slower grading, or 409 `speaking_ai_unavailable` (retryable, no charge) when both routes fail.

**Immediate action**:
1. Read `<class>`: `quota_exhausted` or `auth` (the subscription: re-auth the sidecar, `docs/ops/WRITING-AI-PROVIDERS.md`
   §8), `timeout` (the 900 s L1 budget ran out: a wedged or queued sidecar lane), `operation_indeterminate` (the pinned
   attempt's outcome was unknown, so the default route took over), or another failure class. The gateway line
   `AI provider call failed: ... provider=writing-claude-sub ...` gives status, class and error code.
   `GET /v1/admin/ai/usage?featureCode=speaking.grade&outcome=ProviderError` lists failures and `outcome=Cancelled` the L1
   budget expiries. The provider circuit is shared with Writing (`GET /v1/admin/ai/circuits`, reset
   `POST /v1/admin/ai/circuits/{key}/reset?kind=provider`).
2. To take the sidecar out of the grading path, set `SPEAKING_GRADING_PINNED_PROVIDER=` (empty) in the VPS env and recreate
   the API slots and `oet-ai-worker`; grading is then one call on the default route.

---

## Sev2: Recording loss

**Detection**: Egress webhook never arrives within 15 min of `room_finished`; OR `SpeakingRecording.MediaAssetId` missing for finished sessions.

**Immediate action**:
1. Page on-call Speaking + Platform.
2. Check LiveKit egress dashboard for the missing room.
3. Trigger re-egress via LiveKit REST `EgressService.StartTrackCompositeEgress` if recording is still in the room buffer.
4. If unrecoverable, notify learner + tutor and re-credit the session.

---

## Sev2: Calibration drift spike

**Detection**: Admin drift dashboard shows a tutor's MAE > 0.5 across last 5 samples.

**Immediate action**:
1. Page Tutor-ops.
2. Pause that tutor's queue claim ability (`TutorReviewQueueService.PauseTutorAsync`).
3. Schedule re-training via linked `InterlocutorTrainingModule` rows.
4. Re-mark the tutor's last 10 submitted assessments to confirm scope.

---

## Sev3: Content bug (typo, wrong tasks, broken interlocutor cue)

**Detection**: Learner support ticket or tutor flag.

**Action**:
1. File issue against `@content-team` with card id.
2. Admin sets card `Status = Archived`.
3. Author a corrected card via AI-draft tool, re-publish.
4. Backfill an apology credit if the card affected learner scoring.

---

## Post-incident process

1. Incident commander files a post-mortem within 5 business days using the template at `docs/speaking/post-mortem-template.md` (to be created on first need).
2. Action items tracked in the `speaking-postmortem` GitHub project.
3. Review at the next Speaking weekly.
