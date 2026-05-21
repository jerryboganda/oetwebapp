# Speaking Module — Incident Runbook

Severity definitions follow standard practice: Sev1 = customer-impacting major outage; Sev2 = degraded service; Sev3 = bug or quality issue. Every incident gets a post-mortem within 5 business days.

---

## Sev1: AI provider down (no AI assessment possible)

**Detection**: PagerDuty alert from `AiGatewayService` 5xx rate > 25% over 5 min; or `assessment_ready` SignalR events absent for > 10 min during peak hours.

**Immediate action**:
1. Page on-call Speaking + on-call Platform.
2. Flip `Features__SpeakingV2_AssessmentEnabled = false` so the UI stops promising "assessment in 12s" and instead shows "assessment queued".
3. Failover to secondary provider via `AiFeatureRouteResolver` (Anthropic → OpenAI fallback).
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
