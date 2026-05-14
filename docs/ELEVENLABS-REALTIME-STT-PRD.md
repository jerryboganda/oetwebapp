# ElevenLabs Realtime STT PRD

Status: In implementation  
Date: 2026-05-14  
Owner: OET platform engineering/content/admin

## Goal

Add server-mediated ElevenLabs realtime speech-to-text to live OET practice while keeping the backend authoritative for final transcripts, AI replies, storage, evaluation, and cost controls.

## User Decisions Captured

- Build target: Conversation, Speaking self-practice deep-links, native Speaking recorder, Pronunciation, and internal prototype surfaces.
- Architecture: server-mediated canonical STT. The browser never owns final transcript authority and never receives an ElevenLabs API key.
- Credentials: admin encrypted settings only for ElevenLabs STT credentials.
- Consent: versioned consent, first version `realtime-stt-v1-2026-05-14`.
- Retention: final transcript plus final audio for 30 days; raw realtime chunks are not persisted.
- Turn-taking: manual strict half-duplex. Learner audio is blocked while AI audio is playing.
- Language profile: provider auto-detect with OET keyterms where supported.
- Device promise: all surfaces eventually, with staged validation.
- Audience promise: all audiences eventually after testing; sponsor/school/minor gates must pass privacy/legal checks before enablement.
- Cost guardrail: hard auto-disable at a $100 pilot cap.
- Real provider testing: desired in protected CI smoke, gated by secrets, budget, and opt-in workflow controls.
- Rollback: disable Conversation audio entirely if cost/security/provider errors spike.

## Functional Requirements

1. Add a distinct `elevenlabs-stt` provider identity separate from `elevenlabs-tts`.
2. Store ElevenLabs STT secrets only in encrypted admin/provider settings; never expose them through `NEXT_PUBLIC_*` or API responses.
3. Add realtime STT feature flags, provider selection, quota fields, consent version, and rollback settings.
4. Add realtime SignalR methods for turn start, audio chunks, turn commit, and cancellation.
5. Persist only final committed learner turns and final audio; partial transcripts are UI-only.
6. Keep all AI tutor replies and evaluations routed through the grounded Conversation AI gateway.
7. Enforce idempotency for final realtime transcript events.
8. Enforce media ownership before streaming conversation audio.
9. Add consent/readiness UI, partial transcript display, fallback status, and strict half-duplex controls.
10. Add admin controls for realtime STT enablement, encrypted key, model, quotas, retention, provider health, and rollback.
11. Add mock-first automated tests and gated real-provider smoke.
12. Expand the shared realtime audio foundation to native Speaking and Pronunciation after Conversation is stable.

## Non-Goals For The First Slice

- No browser-direct ElevenLabs API key.
- No client-authoritative transcript persistence.
- No raw realtime chunk retention.
- No scoring or rulebook shortcut outside existing OET services.
- No uncontrolled public beta before security, privacy, cost, and device gates pass.

## Acceptance Criteria

Given a learner owns a conversation session, when they request their own conversation audio, then the API streams it; when another learner requests the same URL without ownership, the API returns not found.

Given realtime STT is disabled, when the learner opens a conversation, then the UI uses normal full-turn recording and shows fallback status.

Given realtime STT is enabled and a mock provider is selected, when the learner starts a realtime turn, sends chunks, and completes it, then the server emits partial transcript events, persists one final learner turn, and generates one grounded AI reply.

Given duplicate `turnClientId` or provider event IDs, when a final realtime event is received twice, then only one learner turn and one AI reply are created.

Given the AI partner is speaking, when the learner tries to record, then both UI and server-side turn state prevent capture.

Given provider errors contain sensitive text, when errors are logged, then logs contain only provider/status/category and never raw response bodies or transcript content.

Given admin settings are fetched, when STT secrets exist, then responses include only `ElevenLabsSttApiKeyPresent` and never raw secret material.

## Rollout Gates

1. Internal/admin testing only.
2. Limited adult direct-learner beta.
3. All direct learners.
4. Sponsors/schools after privacy/vendor approval.
5. Minors only after explicit guardian/school consent workflow.
6. Mobile/Electron/Capacitor surfaces only after real-device microphone and WebSocket QA.
