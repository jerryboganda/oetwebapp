# Speaking Module — Attack Surface

Every externally-reachable surface. Grouped by trust boundary.

## Public unauthenticated

- `POST /v1/auth/sign-in` — rate-limited (`AuthBruteforce`).
- `POST /v1/speaking/live-rooms/webhooks/livekit` — HMAC-verified (`ILiveKitGateway.VerifyWebhookSignature`); no auth header, signature header instead.

## Authenticated — learner (`LearnerOnly`)

- `POST /v1/speaking/sessions` — create session
- `GET /v1/speaking/sessions/{id}` — owner-checked (404 for non-owners)
- `POST /v1/speaking/sessions/{id}/start-warmup` | `finish-warmup` | `start-roleplay` | `end` | `consent` | `ai-assess`
- `GET /v1/speaking/sessions/{id}/ai-assessment` | `transcript`
- `GET /v1/speaking/role-play-cards` — profession-filtered (no leak)
- `GET /v1/speaking/role-play-cards/{id}` — profession-filtered; non-owner profession returns 404
- `POST /v1/speaking/drills/{id}/attempts` + `/attempts/{aid}/recordings` + `/score`
- `GET /v1/speaking/recordings/mine` + `DELETE /v1/speaking/recordings/{id}`
- `GET /v1/speaking/consents/me`, `POST /v1/speaking/consents`
- `POST /v1/learner/account/erasure-preflight`
- LiveKit room JWT mint: `GET /v1/speaking/live-rooms/{id}/token`

## Authenticated — expert / tutor (`ExpertOnly`)

- `GET /v1/expert/speaking/queue` + `POST /queue/{id}/claim` | `release`
- `POST /v1/expert/speaking/sessions/{id}/tutor-assessment` (draft) + `/submit`
- `POST /v1/expert/speaking/sessions/{id}/comments`
- `GET /v1/expert/speaking/sessions/{id}/assessments` (dual: AI + tutor)
- `GET /v1/expert/training` (interlocutor training modules)
- `GET /v1/expert/calibration/samples` + score endpoints

## Authenticated — admin (`AdminOnly` + granular `AdminContent*` policies)

- `GET/POST/PUT/DELETE /v1/admin/speaking/cards` + `/scripts` + `/mock-sets` + `/drills` + `/shared-resources` + `/calibration`
- `POST /v1/admin/speaking/cards/ai-draft` | `/batch` | `/auto-pair`
- `GET /v1/admin/speaking/analytics/*`
- `GET /v1/admin/speaking/recordings/audit` + `POST /v1/admin/speaking/recordings/{id}/access`
- `GET /v1/admin/speaking/calibration/drift`

## SignalR hubs

- `/v1/conversation/hub` — `LearnerOnly`; session-scoped group; warm-up + role-play turn loop.
- `/v1/speaking/live-rooms/hub` — `LearnerOnly | ExpertOnly`; room-scoped group; `CueRaised`, `LiveRoomEnded`, `LiveRoomSnapshot`.

## Outbound calls

- Anthropic Messages API — over TLS; bearer key; provider has no callback into our stack.
- OpenAI Chat Completions — same.
- ElevenLabs TTS — same.
- LiveKit Cloud REST — TLS + signed JWT; receives webhook callbacks (signature-verified).
- AWS S3 — TLS + sigv4; LiveKit egress writes here.

## Storage surfaces

- Postgres — local network only; encrypted at rest.
- S3 (egress + archive) — bucket policy must deny anonymous; access only via pre-signed URLs minted server-side.

## Rate-limiting buckets (existing)

- `AuthBruteforce`, `AuthOtpSend`, `AuthRefresh`, `PerUser` (used by admin Speaking endpoints).

## Trust boundaries

```
[ Browser ] --TLS--> [ Next.js BFF ] --HTTPS--> [ .NET API ] --TLS--> [ Anthropic / OpenAI / LiveKit / S3 ]
                                                       |
                                                       v
                                                  [ Postgres ]
```
