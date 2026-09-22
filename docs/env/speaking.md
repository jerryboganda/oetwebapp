# Speaking Module — Environment Reference

Every Speaking-module env key, grouped by subsystem. Defaults are listed; required keys are marked.

## LiveKit Cloud — Live Tutor Rooms

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `LIVEKIT__PROVIDER` | optional | `disabled` | `livekit_cloud` swaps the stub gateway for real cloud. |
| `LIVEKIT__APIKEY` | yes (cloud) | — | LiveKit API key. |
| `LIVEKIT__APISECRET` | yes (cloud) | — | LiveKit API secret. |
| `LIVEKIT__WSSURL` | yes (cloud) | — | WebSocket URL (`wss://<project>.livekit.cloud`). |
| `LIVEKIT__WEBHOOKSIGNINGSECRET` | yes (cloud) | — | HMAC secret for webhook verification. |
| `LIVEKIT__EGRESSBUCKET` | yes (cloud) | — | S3 bucket for egress output. |
| `LIVEKIT__DEFAULTMAXDURATIONSECONDS` | optional | `1800` | Auto-end ceiling per room (seconds). |
| `LIVEKIT__EGRESSENABLED` | optional | `true` | Track-composite recording on/off. |

## Anthropic — Default Speaking AI Provider

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `ANTHROPIC__APIKEY` | yes | — | Anthropic key. Feature routes `speaking.score.v2`, `speaking.patient.turn.v1`, `card.draft.v1` default to Claude Sonnet 4.6 + Haiku 4.5 here. Prompt-caching enabled by default. |

## OpenAI — Fallback + OpenAI-compatible

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `OPENAI__APIKEY` | optional | — | OpenAI / OpenAI-compatible key (fallback when Anthropic unavailable). |
| `OPENAI__APIBASE` | optional | `https://api.openai.com/v1` | Override for compatible vendors (Groq, Together, Mistral La Plateforme, NVIDIA NIM, etc.). |

## Native realtime Speaking voice

Speaking role-play cards use native full-duplex voice only. The API keys stay
server-side. The configured model must pass the live provider probe before the
browser is allowed to request microphone access.

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `LIVEVOICE__PRIMARYPROVIDER` | yes | `openai` | `openai` or `gemini`; a session uses one disclosed provider. |
| `LIVEVOICE__OPENAIAPIKEY` | yes for OpenAI | — | Server-only OpenAI Realtime credential. |
| `LIVEVOICE__OPENAIBASEURL` | yes for OpenAI | `https://api.openai.com/v1/live/sessions` | OpenAI Realtime session broker endpoint. |
| `LIVEVOICE__OPENAIMODELSBASEURL` | yes for OpenAI | `https://api.openai.com/v1/models` | Model catalog endpoint used by the live account probe. |
| `LIVEVOICE__OPENAIMODEL` | yes for OpenAI | `gpt-live-1` | Lowest-latency production model enabled on the account, verified by the probe. |
| `LIVEVOICE__GEMINIAPIKEY` | yes for Gemini | — | Server-only Gemini Live credential. |
| `LIVEVOICE__GEMINIBASEURL` | yes for Gemini | `https://generativelanguage.googleapis.com/v1beta/auth_tokens` | Constrained ephemeral-token endpoint. |
| `LIVEVOICE__GEMINIMODELSBASEURL` | yes for Gemini | `https://generativelanguage.googleapis.com/v1beta/models` | Model catalog endpoint used by the live account probe. |
| `LIVEVOICE__GEMINIMODEL` | yes for Gemini | `models/gemini-3.8-live` | Gemini Live model enabled on the account. |
| `LIVEVOICE__GEMINIWEBSOCKETBASEURL` | yes for Gemini | constrained Live WebSocket | Browser WebSocket endpoint used with the short-lived token. |
| `LIVEVOICE__RETENTIONDAYS` | optional | `30` | Bounded retention for live voice transcript and connection audit data. |

## TypeSafe SystemOne / Jev

Jev validates generated role-player projections and receives non-blocking
conversation advisories. It never generates speech, replaces the live voice
provider, or blocks an active turn.

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `TYPESAFE__ENABLED` | yes for generated-content readiness | `true` | Master TypeSafe switch. |
| `TYPESAFE__APIKEY` | yes when enabled | — | Server-only TypeSafe credential. |
| `TYPESAFE__CONVERSATIONADVISORYENABLED` | optional | `true` | Enables asynchronous Jev turn advisories. |

## ElevenLabs — TTS

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `ELEVENLABS__APIKEY` | optional | — | When set, ElevenLabs is the default `IConversationTtsProvider`. Falls back to Azure / OSS / Mock providers per `ConversationTtsProviderSelector`. |

## Whisper / ASR

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `SPEAKING__WHISPER__APIKEY` | optional fallback | — | Server-only fallback for recorded Speaking grading. The preferred credential is the admin `whisper-asr` provider registry row. |
| `SPEAKING__WHISPER__BASEURL` | optional | `https://api.openai.com/v1` | OpenAI-compatible Whisper base URL. |
| `SPEAKING__WHISPER__MODEL` | optional | `whisper-1` | Whisper transcription model. |

## AWS S3 — Egress + Archive

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `AWS__ACCESSKEYID` | yes (cloud) | — | Programmatic access key. |
| `AWS__SECRETACCESSKEY` | yes (cloud) | — | Programmatic secret. |
| `AWS__REGION` | yes (cloud) | `eu-west-2` | Region of the egress bucket. |
| `AWS__BUCKET` | yes (cloud) | — | Default Speaking recordings bucket. |

## Speaking Compliance

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `SpeakingCompliance__CurrentConsentVersion` | optional | `recording.v1` | Versioned consent code stamped on every session. |
| `SpeakingCompliance__CurrentLiveVideoConsentVersion` | optional | `live_video_with_tutor.v1` | Versioned consent for live tutor rooms. |
| `SpeakingCompliance__RetentionDaysDefault` | optional | `90` | Retention window for recordings WITHOUT tutor review. |
| `SpeakingCompliance__RetentionDaysWhenTutorReviewed` | optional | `365` | Retention window WHEN tutor assessment exists. |
| `SpeakingCompliance__AuditLogRetentionDays` | optional | `2555` | 7-year `AuditEvent` retention. |

## Feature Flags

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `Features__SpeakingV2` | optional | `false` | Master flag for the v2 module rollout. Cohort-rollout: staging → 5% → 25% → 100%. |

## Postgres

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `ConnectionStrings__OetLearner` | yes | — | EF Core connection string. |

## Local dev quick-start

See `docs/dev/quickstart-speaking.md` for the 10-minute path. The local stack runs against a stubbed LiveKit gateway, mock TTS, and mock ASR by default — no provider keys required for the AI self-practice flow.
