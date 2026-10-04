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

`SpeakingAudioRetentionWorker` runs hourly; writes `AuditEvent` per deletion.

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
