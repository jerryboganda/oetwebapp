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
| `LIVEKIT__EGRESSENABLED` | optional | `true` | Audio-only mixed-room recording on/off. |

Rooms are **audio-only**: tokens grant microphone publish only, and egress is an
audio-only room composite written as `{EgressBucket}/oet-speaking/{RoomName}.ogg`
(`file_type: OGG`). Joining needs the current `recording`, `tutor_review` and
`retention` consents; live-video consent is not required.

When LiveKit is not configured outside Development/Testing (the
`LiveKitProviderUnavailable` gateway), tutor slot listing, private-speaking
booking and Full Mock Speaking tutor booking return `503`
`{ "code": "tutor_rooms_unavailable", "message": "Live tutor sessions are temporarily unavailable." }`
before any credit or payment is taken, and `GET /v1/private-speaking/config`,
learner booking DTOs and `GET /v1/mocks/bookings` report `liveRoomsAvailable: false`.

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
browser is allowed to request microphone access. Behaviour (failover, health,
hard duration cap, hang-up): [../speaking/live-voice.md](../speaking/live-voice.md).

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `LIVEVOICE__PRIMARYPROVIDER` | yes | `openai` | `openai` or `gemini` (blank or unknown = `openai`). The provider tried first for a new session; the other configured provider is offered after it, and alone when the primary's circuit is open (or it is unconfigured or unverified). The order is never health-ranked: health only filters. A card runs on one provider (the one whose link went live). |
| `LIVEVOICE__OPENAIAPIKEY` | yes for OpenAI | — | Server-only OpenAI Realtime credential. |
| `LIVEVOICE__OPENAIBASEURL` | yes for OpenAI | `https://api.openai.com/v1/live/sessions` | OpenAI Realtime session broker endpoint. |
| `LIVEVOICE__OPENAIMODELSBASEURL` | yes for OpenAI | `https://api.openai.com/v1/models` | Model catalog endpoint used by the live account probe. |
| `LIVEVOICE__OPENAIMODEL` | yes for OpenAI | `gpt-live-1` | Lowest-latency production model enabled on the account, verified by the probe. |
| `LIVEVOICE__GEMINIAPIKEY` | yes for Gemini | — | Server-only Gemini Live credential. |
| `LIVEVOICE__GEMINIBASEURL` | yes for Gemini | `https://generativelanguage.googleapis.com/v1beta/auth_tokens` | Constrained ephemeral-token endpoint. |
| `LIVEVOICE__GEMINIMODELSBASEURL` | yes for Gemini | `https://generativelanguage.googleapis.com/v1beta/models` | Model catalog endpoint used by the live account probe. |
| `LIVEVOICE__GEMINIMODEL` | yes for Gemini | `models/gemini-3.8-live` | Gemini Live model enabled on the account. |
| `LIVEVOICE__GEMINIWEBSOCKETBASEURL` | yes for Gemini | constrained Live WebSocket | Browser WebSocket endpoint used with the short-lived token. |
| `LIVEVOICE__GEMINITOKENLIFETIMESECONDS` | optional | `900` | Kept only so existing config still binds; it has **no effect**. A Gemini token always expires at `min(now + 1800 s, hard stop + 15 s)`, so no value here can cut a conversation short (a 90 s lifetime did on 25 Sep 2026) or outlive the hard stop. |
| `LIVEVOICE__GEMININEWSESSIONLIFETIMESECONDS` | optional | `60` | Window (clamped 15-120 s) in which the browser must open the Gemini socket with a fresh token: `newSessionExpireTime = min(now + this, expireTime)`. |
| `LIVEVOICE__PROVIDERREQUESTTIMEOUTSECONDS` | optional | `10` | Timeout (clamped **2-20**) for one provider session-creation call. The ceiling stays below the browser's fixed 22 s create-call timeout, so a slower provider always ends in the server's generic 503 (the client fails over) and never in a client-side timeout. |
| `LIVEVOICE__MAXROLEPLAYSECONDS` | optional | `600` | Server-side ceiling (clamped 180-1800) on one role-play; a card's own time above it is capped, exam cards included (a card with no time uses 300). |
| `LIVEVOICE__HARDSTOPGRACESECONDS` | optional | `30` | Slack (clamped 0-120) after the role-play deadline before the server force-ends a role-play the client never ended. Exam cards are then graded (abandoned standalone practice is finished but **not** graded), and the OpenAI sessions are hung up; the hang-up also covers role-plays the learner or the exam clock ended, at the same point. |
| `LIVEVOICE__TRANSCRIPTFLUSHGRACESECONDS` | optional | `900` | How long (clamped 60-3600) after a role-play ended (after its hard stop if still Active) a late transcript, turn or recording is still accepted. A transcript is also frozen once grading has taken it; recordings are only time-bounded. |
| `LIVEVOICE__MAXPROVIDERSESSIONSPERROLEPLAY` | optional | `3` | Provider sessions (clamped 1-10) one role-play may open; retries, reloads and failover all count, refused creations do not. |
| `LIVEVOICE__RETENTIONDAYS` | optional | `30` | Bounded retention for live voice transcript and connection audit data: the turn rows and the `live_session` audit rows, which carry the raw OpenAI session id used by the hang-up. The retention sweep wipes them after this many days. |

The `ai-worker` container inherits these keys from the same env block. It needs the
OpenAI key and URLs for the hard-stop hang-up (without them the hang-up silently does
nothing); it does not run the catalog probe, and only the API slots hold the provider
circuit state.

## TypeSafe SystemOne / Jev

Jev validates generated role-player projections and receives non-blocking
conversation advisories. It never generates speech, replaces the live voice
provider, or blocks an active turn.

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `TYPESAFE__ENABLED` | yes for generated-content readiness | `false` | Master TypeSafe switch. Off by default everywhere (class, `appsettings.json`, compose); production opts in explicitly. Every per-surface flag is a no-op until this is `true`. |
| `TYPESAFE__APIKEY` | yes when enabled | — | Server-only TypeSafe credential. |
| `TYPESAFE__CONVERSATIONADVISORYENABLED` | optional | `false` | Enables asynchronous Jev turn advisories. Needs `TYPESAFE__ENABLED=true`. |

Every other TypeSafe variable (Writing, Companion, response-verify and
development-triage flags, thresholds, retry and breaker settings), the key
handling rules and the flag-flip order are in [typesafe.md](typesafe.md).

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

## Speaking grading route (Claude subscription sidecar)

Owner directive 2026-09-30: `speaking.grade` tries the dedicated Claude Max sidecar first
(`writing-claude-sub`, Opus 5.5, effort `high`) and falls back to the default route
(Anthropic API) when that call fails. All three `SPEAKING_GRADING_*` keys are optional; the
defaults apply when unset. Chain details and how to revert:
[ai-providers.md](../speaking/ai-providers.md). The pin needs the `writing-claude-sub` provider
row to be **active**. The "quota/budget refusals" that never fail over are the platform's own
(learner quota, platform budget, feature policy); the provider's own quota error
(`quota_exhausted`) is a provider failure and does fail over.

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `SPEAKING_GRADING_PINNED_PROVIDER` | optional | `writing-claude-sub` | Provider registry code tried first (`Speaking__Grading__PinnedProviderCode`). Set it to an empty value (`SPEAKING_GRADING_PINNED_PROVIDER=`) to turn the pin off; grading is then one plain call on the default route. |
| `SPEAKING_GRADING_PINNED_MODEL` | optional | `claude-opus-5-5` | Model requested from the pinned provider (`Speaking__Grading__PinnedModel`). Compose maps it as `${SPEAKING_GRADING_PINNED_MODEL-claude-opus-5-5}` (no colon): unset = `claude-opus-5-5`; set but empty (`SPEAKING_GRADING_PINNED_MODEL=`) = the provider row's default model. |
| `SPEAKING_GRADING_PINNED_TIMEOUT_SECONDS` | optional | `900` | Wall-clock budget for the WHOLE pinned call, the sidecar's queue wait, the CLI run and the gateway's own retries (`Speaking__Grading__PinnedTimeoutSeconds`). On expiry the pinned call is cancelled and grading falls back to the default route (the pinned attempt leaves a `Cancelled` usage row). `0` or less = no cap; values above `1500` are clamped to 1500. Keep it above the sidecar's 300 s CLI timeout and around 15 minutes, so a full level 1 budget plus level 2 (about 12 minutes at maximum reasoning) still fits the 30-minute operation lease. Compose maps it as `${...:-900}`, so empty also means 900. |
| `OET_INTERNAL_AI_HOSTS` | yes for the sidecar | `ubag-vps-gateway-1,oet-writing-claude,oet-writing-codex` | Hosts the SSRF guard accepts over plain HTTP. Must include `oet-writing-claude`. The seeded sidecar marker key is also honoured only for a provider row whose `BaseUrl` host is on this list: a missing host surfaces as `Platform API key missing for ... writing-claude-sub` (Anthropic adapter) or `... writing-codex-sub` (OpenAI-compatible adapter), not only as `BaseUrl must use https://`. Read by the API slots and by `oet-ai-worker`, which runs queued grades. |

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
| `SpeakingCompliance__CurrentLiveVideoConsentVersion` | optional | `live_video_with_tutor.v1` | Versioned live-video consent. Not required for the audio-only live tutor rooms. |
| `SpeakingCompliance__RetentionDaysDefault` | optional | `90` | Retention window for recordings WITHOUT tutor review. |
| `SpeakingCompliance__RetentionDaysWhenTutorReviewed` | optional | `365` | Retention window WHEN tutor assessment exists. |
| `SpeakingCompliance__AuditLogRetentionDays` | optional | `2555` | 7-year `AuditEvent` retention. |

## Feature Flags

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `Features__SpeakingV2` | optional | `false` | Master flag for the v2 module rollout. Cohort-rollout: staging → 5% → 25% → 100%. In Production, `true` also makes `/health/ready` require BOTH live voice providers to be configured and probe-verified, and LiveKit recording to be configured (it never reads the circuit). |

## Postgres

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `ConnectionStrings__OetLearner` | yes | — | EF Core connection string. |

## Local dev quick-start

See `docs/dev/quickstart-speaking.md` for the 10-minute path. The local stack runs against a stubbed LiveKit gateway, mock TTS, and mock ASR by default — no provider keys required for the AI self-practice flow.
