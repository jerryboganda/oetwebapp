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

## ElevenLabs — TTS

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `ELEVENLABS__APIKEY` | optional | — | When set, ElevenLabs is the default `IConversationTtsProvider`. Falls back to Azure / OSS / Mock providers per `ConversationTtsProviderSelector`. |

## Whisper / ASR

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `WHISPER__APIKEY` | optional | — | Often reuses `OPENAI__APIKEY`. Used by `WhisperPronunciationAsrProvider` and `SpeakingTranscriptionPipeline`. |

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
| `ConnectionStrings__OetWithDrHesham` | yes | — | EF Core connection string. |

## Local dev quick-start

See `docs/dev/quickstart-speaking.md` for the 10-minute path. The local stack runs against a stubbed LiveKit gateway, mock TTS, and mock ASR by default — no provider keys required for the AI self-practice flow.
