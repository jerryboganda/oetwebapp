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
