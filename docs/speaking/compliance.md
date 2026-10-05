# Speaking Module — Compliance

## Consent

- Versioned via `SpeakingComplianceConsent`. Defaults set in `SpeakingComplianceOptions`:
  - `recording.v1` — base recording consent.
  - `live_video_with_tutor.v1` — separate consent for tutor video sessions.
- Recorded at session creation; surfaced on every results screen.

## Retention

| Recording type | Window | Source |
|----------------|--------|--------|
| Self-practice (no tutor review) | `RetentionDaysDefault` (90 d) | `SpeakingComplianceOptions` |
| Tutor-reviewed | `RetentionDaysWhenTutorReviewed` (365 d) | same |
| Audit events | `AuditLogRetentionDays` (7 y) | same |

`SpeakingAudioRetentionWorker` sweeps every 6 hours; writes `AuditEvent` per deletion.

### Calibration retention (owner decision 2026-10-05)

A performance an admin promotes into the grader-calibration set keeps its audio for **365 days**
(`SpeakingRecording.RetentionExpiresAt` is extended, never shortened) and is purged by the normal sweep once that
elapses. This is allowed only because the learner consent wording from `recording.v3` onwards says so:
a recording may be selected, with the learner's identity hidden from the reviewer, for quality assurance and grader
calibration, kept up to 365 days and listened to by qualified expert reviewers for that purpose only. Promotion
is refused unless the learner held an active `recording.v3`-or-later consent when the performance was recorded.
A learner deleting the recording, withdrawing consent, or day-365 expiry makes the sample unusable (it stops
counting and is not graded); finished reports hold numbers only. Streaming a calibration clip writes a
`SpeakingRecordingAccessed` audit event without the learner's identity. Transcript-only samples keep the transcript
retention policy unchanged.

### Live voice (AI patient): what is stored

- Short, consent-gated clips are captured from the learner's microphone during local speech activity and stored as
  `SpeakingRecording` / `MediaAsset` rows through the existing `IFileStorage` path. The app does not make a full-session
  recording or directly record provider playback. Browser echo cancellation is enabled but cannot guarantee isolation:
  provider or background audio may still be picked up by the microphone. Transcript segments reference their source
  recording; clips with no verified transcript overlap are not attributed to a candidate segment. Microphone audio is
  also streamed live to the disclosed voice provider, which may retain bounded session audio under its own policy
  ([disclosure](live-voice.md#consent-and-disclosure)).
- The existing `SpeakingAudioRetentionWorker` applies the approved Speaking recording retention window and learner erasure
  path to these clips. `LIVEVOICE__RETENTIONDAYS` separately wipes per-turn working rows. The final transcript and v1.1
  turn evidence still have no expiry code; whether those should expire is an owner/legal decision.
- While a card runs, a copy of the conversation (that session's words only, no tokens or provider ids) is kept in the tab's
  `sessionStorage` for up to 15 minutes so a page refresh does not lose it; it is removed after the transcript is saved
  ([Refresh behaviour](live-voice.md#refresh-behaviour)).
- The Rules and consent screen discloses short live microphone clips linked to candidate transcript evidence, their retention period and provider processing
  before recording begins; the server still requires versioned recording, AI-processing and retention consents.

## Learner rights

- **Access**: `GET /v1/speaking/recordings/mine`, `/v1/speaking/consents/me`.
- **Erasure**: `DELETE /v1/speaking/recordings/{id}` (self-service per-recording).
- **Erasure pre-flight**: `POST /v1/learner/account/erasure-preflight` returns inventory before full GDPR erasure.
- **Portability**: each recording has a pre-signed download URL (short TTL).

## Admin access logging

Every admin recording access writes an `AuditEvent`:
- Actor (admin id + email at time of access)
- Recording id + session id
- Reason (free-text, required at the request endpoint)
- Timestamp

Reviewable at `/admin/speaking/recordings/audit`.

## Cross-border

- AI provider calls: covered by SCCs + DPA. Region: provider default (US for Anthropic / OpenAI / ElevenLabs).
- LiveKit Cloud: region pinned via `LIVEKIT__WSSURL`.
- S3: region pinned via `AWS__REGION` (default `eu-west-2`).

## Special-category data

- Voice recordings are biometric → GDPR Article 9 special category.
- Lawful basis: **explicit consent**.
- Recording cannot proceed without `SpeakingComplianceConsent` of `recording.v1` (or current version).

## Score disclaimer

Rendered on every results screen via `SpeakingScoreDisclaimer.tsx`:
> Estimated readiness band, not an official OET score.

## Onward

- Threat model: `docs/security/speaking/threat-model.md`
- Incident response: `docs/speaking/incident-runbook.md`
- DSR runbook: cross-references this module.
