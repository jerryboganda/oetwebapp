# ElevenLabs Realtime STT Progress

Last updated: 2026-05-14  
Linked PRD: `docs/ELEVENLABS-REALTIME-STT-PRD.md`  
Linked plan: `docs/ELEVENLABS-REALTIME-STT-IMPLEMENTATION-PLAN.md`

## Current Status

Implementation is in the mock-first hardening slice. The learner UI now exercises the realtime SignalR transport and falls back to full-turn ASR, while the real ElevenLabs websocket adapter remains behind rollout gates until audio-format and credentialed smoke tests are complete.

## Progress Ledger

| Area | Status | Evidence | Next action |
| --- | --- | --- | --- |
| Product decisions | Done | Popup decisions captured 2026-05-14 | Keep rollout gates visible in this file |
| PRD/progress docs | Done | This PRD/progress pair exists | Update after each code slice |
| Media ownership auth | Done in code | Conversation media endpoint now checks `ConversationTurn.AudioUrl` joined to current learner session | Add focused backend security tests |
| STT/TTS config separation | Done in code | `ConversationOptions`, `ConversationSettingsRow`, admin request/response support distinct STT settings | Wire admin UI controls |
| Provider registry dialect | Done in code | `AiProviderDialect.ElevenLabsStt` and `elevenlabs-stt` seed support | Add provider registry admin display if needed |
| Provider log redaction | Done in code | Azure/Whisper/Deepgram ASR and ElevenLabs TTS no longer log raw error bodies | Add log-redaction tests |
| Realtime ASR contracts | Done in code | Realtime interfaces and mock provider exist beside batch ASR | Implement real ElevenLabs Scribe adapter |
| Realtime hub transport | Done in code | `BeginRealtimeTurn`, `SendRealtimeAudioChunk`, `CompleteRealtimeTurn`, `CancelRealtimeTurn` exist; in-memory chunks are bounded by total bytes, idle timeout, turn duration, global active-stream caps, global buffered-byte caps, pre-decode encoded-size checks, and partial-event throttle | Add hub/coordinator tests and live provider session lifecycle |
| Audio consent enforcement | Done in code | `ConversationSession` stores audio consent version plus recording/vendor acceptance timestamps; hub rejects batch/realtime audio without current consent | Add focused hub/integration tests |
| Idempotency fields | Done in code | `ConversationTurn` has client/provider event/finalized fields and indexes | Add hub-level duplicate-final tests |
| Frontend realtime UX | Done in code | Learner page acknowledges consent server-side, waits for SignalR before activation, requests realtime start before microphone capture, sends turn-relative MediaRecorder chunks, blocks end-session during in-flight turns, shows provisional transcript, and falls back to full-turn ASR only when permitted | Extract dedicated hook and add page-level SignalR/MediaRecorder tests |
| EF migration | Done | `20260514121544_AddConversationRealtimeStt` adds STT settings/turn idempotency fields; `20260514125903_AddConversationAudioConsent` adds session consent fields | Apply through normal deployment migration flow |
| Admin UI controls | Done in code | `app/admin/content/conversation/settings/page.tsx` exposes mock-first realtime STT controls with readiness warning; real provider choices and credentials stay hidden until the adapter is wired | Add browser/admin form tests |
| Production env guardrails | Done in code | `scripts/deploy/validate-production-env.sh` rejects `NEXT_PUBLIC_ELEVENLABS*` keys and requires an explicit acknowledgement before `Conversation__RealtimeSttEnabled=true` / `CONVERSATION__REALTIMESTTENABLED=true` can pass production validation | Add deployment smoke coverage |
| Real ElevenLabs provider | Pending | Mock provider only | Implement server-side websocket adapter with redacted errors |
| Speaking native integration | Pending | Conversation deep-links benefit automatically | Extend native Speaking recorder after Conversation passes QA |
| Pronunciation integration | Pending | Separate module still uses pronunciation ASR path | Share realtime capture/provider foundation later |
| Real provider CI smoke | Pending/risky | User selected real-provider CI smoke | Add protected, opt-in workflow with hard budget guard |
| Rollback drill | Pending | Config fields exist | Add admin action/runbook and tests |

## Safety Gates Still Open

- Apply EF migrations through the normal deployment migration flow.
- Add tests for conversation media ownership, consent enforcement, duplicate realtime final commits, and fallback-disabled browser behavior.
- Add admin UI controls and tests for STT secret masking.
- Add page-level SignalR/MediaRecorder regression tests for hub readiness, server-denied realtime start, and end-session blocking.
- Implement the real ElevenLabs Scribe provider only after protocol spike confirms endpoint, auth, event schema, and error contract.
- Add sponsor/school/minor server-side policy checks before broad rollout.
- Add hard STT spend metering; current `$100` cap is represented in config but not yet connected to provider billing telemetry.
- Add protected CI smoke workflow only when secrets, budget limits, and fork restrictions are ready.

## Verification Log

- 2026-05-14: Editor diagnostics reported no errors on changed backend foundation files.
- 2026-05-14: Editor diagnostics reported no errors on changed frontend conversation files.
- 2026-05-14: `npm run backend:build` passed.
- 2026-05-14: Focused backend tests passed: `ConversationRealtimeSttTests` and `AiVoiceProviderSeederTests` (14 tests).
- 2026-05-14: `npx tsc --noEmit` passed.
- 2026-05-14: `npm run lint` passed.
- 2026-05-14: Independent review found realtime buffer DoS risk and unimplemented-provider exposure; both were addressed in code for this slice.
- 2026-05-14: Second review passed for blockers; follow-up transport/config concerns were tightened with explicit SignalR receive size and mock-only provider normalization.
- 2026-05-14: Added server-side audio consent persistence/enforcement and generated `20260514125903_AddConversationAudioConsent`.
- 2026-05-14: Added browser chunk streaming to the existing mock realtime SignalR hub, with batch fallback and learner-friendly fallback copy.
- 2026-05-14: Focused frontend tests passed: `npx vitest run components/domain/conversation/conversation-realtime-controls.test.tsx` (4 tests).
- 2026-05-14: Independent review found two high-risk issues: client capture after server denial and destructive realtime buffer completion. Both were fixed by server-side start/commit result contracts, pre-capture realtime start, discard-on-error recording cleanup, consent re-check at completion, and non-destructive finalize-after-commit buffering.
- 2026-05-14: Focused backend tests passed: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~ConversationRealtimeSttTests|FullyQualifiedName~AiVoiceProviderSeederTests"` (18 tests).
- 2026-05-14: `npx tsc --noEmit`, `npm run lint`, and `npm run backend:build` passed after the continuation slice.
- 2026-05-14: Added frontend hub-readiness gating, end-session in-flight blocking, turn-relative chunk offsets, status/progress accessibility semantics, and screen-reader live transcript text.
- 2026-05-14: Added server pre-decode chunk checks, stream-existence validation, stale-buffer sweeping, global active-stream/buffer caps, and focused realtime store coverage.
- 2026-05-14: Focused backend tests passed: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~ConversationRealtimeSttTests|FullyQualifiedName~AiVoiceProviderSeederTests"` (19 tests).
- 2026-05-14: Focused frontend tests passed: `npx vitest run components/domain/conversation/conversation-realtime-controls.test.tsx` (4 tests, including realtime accessibility assertions).
- 2026-05-14: Independent reviewer found stale `TranscriptJson` after newly-added AI turns and asymmetric realtime chunk rejection cleanup. Fixed by persisting AI turns before rebuilding transcript JSON and cancelling active realtime buffers on terminal chunk rejection.
- 2026-05-14: Focused backend tests passed again after review fixes: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~ConversationRealtimeSttTests|FullyQualifiedName~AiVoiceProviderSeederTests"` (19 tests), followed by `npm run backend:build`.
