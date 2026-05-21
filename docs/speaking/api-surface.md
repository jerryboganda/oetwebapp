# Speaking Module — API Surface

Auth scopes: `LearnerOnly`, `ExpertOnly`, `AdminOnly` (+ granular admin permissions like `AdminContentRead/Write/Publish`).

## Learner

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/v1/speaking/role-play-cards` | Published cards filtered by `ActiveProfessionId` + universal |
| GET | `/v1/speaking/role-play-cards/{id}` | Single card (404 on profession mismatch) |
| POST | `/v1/speaking/sessions` | Create session |
| GET | `/v1/speaking/sessions/{id}` | Session detail (owner) |
| POST | `/v1/speaking/sessions/{id}/start-warmup` | WarmUp transition |
| POST | `/v1/speaking/sessions/{id}/finish-warmup` | WarmUp → Prep |
| POST | `/v1/speaking/sessions/{id}/start-roleplay` | Prep → Active |
| POST | `/v1/speaking/sessions/{id}/end` | Active → Finished |
| POST | `/v1/speaking/sessions/{id}/consent` | Stamp consent version |
| POST | `/v1/speaking/sessions/{id}/ai-assess` | Run AI assessment |
| GET | `/v1/speaking/sessions/{id}/ai-assessment` | Latest assessment |
| GET | `/v1/speaking/sessions/{id}/transcript` | Latest transcript |
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
| POST | `/v1/admin/speaking/cards/batch` | Batch generation request |
| GET/POST | `/v1/admin/speaking/drills/*` | Drill CRUD + AI-draft |
| GET/POST | `/v1/admin/speaking/mock-sets/*` | Mock set CRUD + auto-pair |
| GET/POST | `/v1/admin/speaking/shared-resources/*` | Warm-up + criteria PDFs |
| GET/POST | `/v1/admin/speaking/calibration/*` | Calibration samples + drift |
| GET | `/v1/admin/speaking/calibration/drift` | Per-tutor MAE report |
| GET | `/v1/admin/speaking/analytics/{slug}` | Dashboard queries |
| GET | `/v1/admin/speaking/recordings/audit` | Recording-access audit log |
| POST | `/v1/admin/speaking/recordings/{id}/access` | Log + grant access |

## Webhooks (unauthenticated, HMAC-verified)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/v1/speaking/live-rooms/webhooks/livekit` | LiveKit event ingestion |

## SignalR hubs

| Hub | Path | Methods |
|-----|------|---------|
| `ConversationHub` | `/v1/conversation/hub` | Warm-up + role-play turn loop |
| `SpeakingLiveRoomHub` | `/v1/speaking/live-rooms/hub` | `JoinRoom`, `LeaveRoom`; events `CueRaised`, `LiveRoomSnapshot`, `LiveRoomEnded` |
