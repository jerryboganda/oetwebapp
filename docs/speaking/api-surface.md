# Speaking Module — API Surface

Auth scopes: `LearnerOnly`, `ExpertOnly`, `AdminOnly` (+ granular admin permissions like `AdminContentRead/Write/Publish`).

## Learner

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/v1/speaking/role-play-cards` | Published cards filtered by `ActiveProfessionId` + universal |
| GET | `/v1/speaking/role-play-cards/{id}` | Single card (404 on profession mismatch) |
| POST | `/v1/speaking/sessions` | Create session |
| GET | `/v1/speaking/sessions/{id}` | Session detail (owner); carries `liveVoiceAvailable` (at least one live voice provider usable, else the recorder fallback) and `rolePlayEndsAt` (the role-play **deadline**, null until it starts) |
| POST | `/v1/speaking/sessions/{id}/start-warmup` | WarmUp transition |
| POST | `/v1/speaking/sessions/{id}/finish-warmup` | WarmUp → Prep |
| POST | `/v1/speaking/sessions/{id}/start-roleplay` | Prep → Active; stamps `RolePlayStartedAt`, the anchor of every server deadline. Exam cards are timed by the exam clock: 409 `speaking_session_exam_managed` |
| POST | `/v1/speaking/sessions/{id}/end` | Active → Finished (learner). The server also finishes an abandoned Active role-play at its hard stop, see [live-voice.md](live-voice.md#hard-duration-cap) |
| POST | `/v1/speaking/sessions/{id}/submit` | Finished → submitted for marking (needs a recording or a transcript with words) |
| POST | `/v1/speaking/sessions/{id}/consent` | Stamp consent version |
| POST | `/v1/speaking/sessions/{id}/ai-assess` | Run AI assessment (202 `processing` while a recorder-fallback transcript is pending) |
| GET | `/v1/speaking/sessions/{id}/ai-assessment` | Latest assessment |
| GET | `/v1/speaking/sessions/{id}/results` | Grading state: `assessmentState` (processing / completed / failed), `retryable` |
| GET | `/v1/speaking/sessions/{id}/clock` | Server clock: stage, `secondsRemaining`, `expired` and, for an Active session only, `hardStopAt` (deadline + grace) |
| POST | `/v1/speaking/sessions/{id}/recording` | Recorder fallback upload (multipart `audio`, optional `durationSeconds`); 202 `{status:"received"}`; a repeat is 409 `recording_already_received` (the only 409 the client treats as success); after the write window a first upload is 409 `live_voice_transcript_window_closed` |
| POST | `/v1/speaking/sessions/{id}/technical-issue` | Flag a technical issue |
| GET | `/v1/speaking/sessions/{id}/transcript` | Latest transcript |
| GET | `/v1/speaking/realtime/sessions/{id}/preflight[?provider=]` | Live voice: disclosure plus `candidates` (providers to try in order: primary first, health only filters) and `pinned` |
| POST | `/v1/speaking/realtime/sessions/{id}/openai/offer` | Live voice: WebRTC SDP exchange; returns `hardStopAt`. Any provider failure is a generic 503 |
| POST | `/v1/speaking/realtime/sessions/{id}/gemini/token` | Live voice: single-use ephemeral token; returns `expiresAt` and `hardStopAt` |
| POST | `/v1/speaking/realtime/sessions/{id}/turns` | Live voice: advisory per-turn row (a failure never blocks the transcript) |
| POST | `/v1/speaking/realtime/sessions/{id}/transcript` | Live voice: the whole transcript for grading; frozen once grading has taken it |
| GET | `/v1/speaking/mock-sets` | Published mock sets |
| GET | `/v1/speaking/mock-sessions/{id}` | Mock session state |
| POST | `/v1/speaking/mock-sessions/{id}/bridge/start` | Finished1 → Bridge |
| POST | `/v1/speaking/mock-sessions/{id}/bridge/finish` | Bridge → Prep2 |
| GET | `/v1/speaking/drills` | Drill catalogue |
| POST | `/v1/speaking/drills/{id}/attempts` | Start drill |
| POST | `/v1/speaking/drills/attempts/{aid}/recordings` | Upload audio |
| POST | `/v1/speaking/drills/attempts/{aid}/score` | AI scoring |
| GET | `/v1/speaking/pathway` | Recommended drills |
| GET | `/v1/speaking/recordings/mine` | Own recordings |
| DELETE | `/v1/speaking/recordings/{id}` | Delete own recording |
| GET | `/v1/speaking/consents/me` | Consent history |
| POST | `/v1/speaking/consents` | Record consent |
| GET | `/v1/speaking/live-rooms/{id}` | Room detail |
| GET | `/v1/speaking/live-rooms/{id}/token` | Mint LiveKit JWT |
| POST | `/v1/learner/account/erasure-preflight` | GDPR pre-flight inventory |

## Expert / tutor

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/v1/expert/speaking/queue` | Review queue |
| POST | `/v1/expert/speaking/queue/{sessionId}/claim` | Claim session |
| POST | `/v1/expert/speaking/queue/{sessionId}/release` | Release claim |
| POST | `/v1/expert/speaking/sessions/{id}/tutor-assessment` | Create draft |
| POST | `/v1/expert/speaking/sessions/{id}/tutor-assessments/{aid}/submit` | Submit |
| POST | `/v1/expert/speaking/sessions/{id}/comments` | Timestamped comment |
| GET | `/v1/expert/speaking/sessions/{id}/assessments` | Dual (AI + tutor) |
| GET | `/v1/expert/training` | Interlocutor training modules |
| GET | `/v1/expert/calibration/samples` | Calibration samples |
| POST | `/v1/expert/calibration/samples/{id}/scores` | Submit score |

## Admin

| Method | Path | Purpose |
|--------|------|---------|
| GET/POST/PUT/DELETE | `/v1/admin/speaking/cards/*` | Card CRUD + publish |
| PUT | `/v1/admin/speaking/scripts/{cardId}` | Upsert interlocutor script |
| POST | `/v1/admin/speaking/cards/ai-draft` | AI-draft a card |
| POST | `/v1/admin/speaking/cards/batch` | Not implemented (no endpoint is mapped) |
| GET/POST | `/v1/admin/speaking/drills/*` | Drill CRUD + AI-draft |
| GET/POST | `/v1/admin/speaking/mock-sets/*` | Mock set CRUD + auto-pair |
| GET/POST | `/v1/admin/speaking/shared-resources/*` | Warm-up + criteria PDFs |
| GET/POST | `/v1/admin/speaking/calibration/*` | Calibration samples + drift |
| GET | `/v1/admin/speaking/calibration/drift` | Per-tutor MAE report |
| GET | `/v1/admin/speaking/analytics/{slug}` | Dashboard queries |
| GET | `/v1/admin/speaking/recordings/audit` | Recording-access audit log |
| POST | `/v1/admin/speaking/recordings/{id}/access` | Log + grant access |
| GET | `/v1/admin/ai/live-voice/health` | Live voice provider health: catalog probe, circuit, last failure, counters (`AdminAiConfig`) |
| POST | `/v1/admin/ai/live-voice/{provider}/reset` | Close a provider's circuit (`openai` \| `gemini`); audited as `LiveVoiceProviderCircuitReset` |

## Live voice notes

- The five `/realtime/sessions/{id}/...` routes and `start-roleplay` share the `AiLiveSpeaking` limiter: one in-flight
  request per user, the next is answered 429 `rate_limited`.
- Live voice error codes, retryable flags and the failover rules the client applies:
  [live-voice.md](live-voice.md#provider-failover). Numbers (deadline, hard stop, write window, session cap):
  [live-voice.md](live-voice.md#hard-duration-cap); keys: [../env/speaking.md](../env/speaking.md).

## Webhooks (unauthenticated, HMAC-verified)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/v1/speaking/live-rooms/webhooks/livekit` | LiveKit event ingestion |

## SignalR hubs

| Hub | Path | Methods |
|-----|------|---------|
| `ConversationHub` | `/v1/conversation/hub` | Warm-up + role-play turn loop |
| `SpeakingLiveRoomHub` | `/v1/speaking/live-rooms/hub` | `JoinRoom`, `LeaveRoom`; events `CueRaised`, `LiveRoomSnapshot`, `LiveRoomEnded` |
