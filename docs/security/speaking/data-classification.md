# Speaking Module — Data Classification

Labels: **Public** (anyone), **Internal** (any logged-in user with module access), **Confidential** (owner + designated reviewers), **Restricted** (named role + audit logged).

## Per-entity classification

| Entity | Label | PII | Retention | Lawful basis | Subject rights |
|--------|-------|-----|-----------|--------------|----------------|
| `LearnerUser` | Confidential | email, displayName, professionId | Account lifetime + 7y audit | Contract | Access, erasure, portability |
| `RolePlayCard` | Internal | none | indefinite | Legitimate interest | n/a |
| `InterlocutorScript` | **Restricted** | none (clinical content) | indefinite | Legitimate interest | n/a — never exposed to learners |
| `SpeakingSession` | Confidential | UserId, ConsentVersion, timing | `RetentionDaysDefault` (90d) or `RetentionDaysWhenTutorReviewed` (365d) | Consent | Access, erasure |
| `SpeakingRecording` | **Restricted** | audio of learner's voice | Same as session | Consent | Access, erasure, portability |
| `SpeakingTranscript` | **Restricted** | learner speech transcription | Same as session | Consent | Access, erasure, portability |
| `SpeakingAiAssessment` | Confidential | references session/transcript | Same as session | Consent | Access, erasure |
| `SpeakingTutorAssessment` | Confidential | tutor identity, learner identity | Same as session + 7y audit | Consent | Access, erasure |
| `SpeakingComplianceConsent` | Restricted | IP address, user agent, consent timestamps | 7y audit | Legal obligation | Access |
| `SpeakingLiveRoom` | Confidential | learner + tutor identities | 30d post-session | Consent | Access |
| `SpeakingLiveRoomToken` | Restricted | per-participant JWT | session duration + 1h | Consent | n/a (ephemeral) |
| `AuditEvent` (speaking.*) | Restricted | actor identity, action | 7y | Legal obligation | Access |

## PII map

- **Voice biometric**: `SpeakingRecording` audio is special-category data under GDPR Article 9 (biometric). Explicit consent required (`SpeakingComplianceConsent.ConsentType = recording`).
- **Health data adjacency**: candidate role-play content may contain self-reported health stories or simulated clinical scenarios that resemble health data. Treat transcripts conservatively as adjacent to special-category data.

## Cross-border transfers

- AI provider calls (Anthropic + OpenAI) cross to the US — covered by SCCs + DPA + UK-EU adequacy.
- LiveKit Cloud regions configurable; default UK/EU. Pin region in `LIVEKIT__WSSURL`.
- S3 bucket region pinned via `AWS__REGION`.
