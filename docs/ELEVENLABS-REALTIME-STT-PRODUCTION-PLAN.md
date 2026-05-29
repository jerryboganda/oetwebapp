# ElevenLabs Realtime STT Production Plan

Status: active local gate; external provider launch remains blocked until evidence is attached.

## Purpose

This plan is the canonical production gate for ElevenLabs-backed realtime speech-to-text. It keeps the admin checklist, backend settings, protected smoke, spend controls, and rollback expectations aligned before any real paid provider stream is exposed to learners.

## Local Gates

| Gate | Requirement | Current enforcement |
| --- | --- | --- |
| G0 provider isolation | Realtime provider must stay mock/fallback unless explicitly enabled. | Conversation settings keep real-provider flags disabled by default. |
| G1 launch readiness | Legal, privacy, protected smoke, spend cap, topology, and evidence URL must be approved. | Admin conversation settings reject real-provider authorization until launch-readiness gates are approved. |
| G2 topology | Provider topology must be one of `single-instance`, `single-region-sticky`, or `distributed` before production. | Conversation settings validation rejects unsupported topology values. |
| G3 spend controls | Monthly cap and estimated cost per minute must be configured within bounded ranges. | Conversation settings validation and hub budget checks enforce caps before stream start. |
| G4 adult-learner policy | Direct adult learner rollout is the first allowed audience. Managed learner rollout stays opt-in. | Conversation options expose adult and managed-learner gates. |
| G5 protected smoke | Real provider smoke must run through the protected live-smoke workflow, never general CI. | Live smoke workflow remains opt-in and secret gated. |
| G6 transcript authority | Provider transcript output must be treated as untrusted input and converted through the server-authoritative conversation flow. | Conversation hub remains the authority for turn state and evaluation. |
| G7 rollback | Admin rollback must disable realtime STT or force batch fallback without code deploy. | Rollback mode is stored in conversation settings. |
| G8 audit trail | Settings changes and readiness changes must be audited without storing secret values. | Admin settings and launch-readiness updates emit audit events with secret-change booleans only. |

## External Evidence Still Required

- Rotated ElevenLabs STT key supplied through the protected secret/admin channel.
- Vendor and privacy approval for the target beta audience and region.
- Protected live smoke evidence with deterministic speech, final transcript assertion, and provider error capture.
- Spend-cap owner approval and provider billing reconciliation source.
- Topology proof for the selected deployment shape.
- Consent copy/version approval for realtime audio processing.

## RTSTT Backlog Register

| ID | Gate | Status | Evidence needed |
| --- | --- | --- | --- |
| RTSTT-001 | Audio/device compatibility | pending-external | Browser PCM, backend transcoding, and container streaming comparison with real device audio. |
| RTSTT-002 | Protected smoke | pending-external | Secret-gated live smoke with deterministic speech and final transcript assertion. |
| RTSTT-003 | Spend reservation | pending-external | Owner-approved cap, estimated cost per minute, provider billing reconciliation source. |
| RTSTT-004 | Circuit breaker and rollback | local-ready | Admin rollback mode plus proof that disabling realtime falls back without deploy. |
| RTSTT-005 | Transcript authority | local-ready | Server-side turn authority and evaluation flow remain authoritative over provider transcript events. |
| RTSTT-006 | Topology evidence | pending-external | Selected topology proof for beta and scale-out path. |
| RTSTT-007 | Sponsor/school/minor gates | pending-external | Legal/privacy approval plus server-side tests before managed-learner exposure. |
| RTSTT-008 | Consent model | pending-external | Approved consent version, copy, retention note, and learner-facing acceptance evidence. |

## No-Go Rules

- Do not enable `RealtimeSttAllowRealProvider` or `RealtimeSttRealProviderProductionAuthorized` without approved launch-readiness gates.
- Do not expose real provider streams to sponsor, school, or minor-managed learners until legal/privacy gates and server-side tests explicitly allow that audience.
- Do not store raw provider keys in documentation, logs, audit details, or normal admin response payloads.
- Do not treat a connection-only silence smoke as transcript-quality evidence.

## Rollout Sequence

1. Keep realtime STT in mock/fallback mode while local code gates are validated.
2. Attach legal, privacy, spend, topology, and protected-smoke evidence in launch readiness.
3. Run protected live smoke with deterministic audio and record the artifact URL.
4. Enable real-provider authorization for a small direct-adult beta cohort.
5. Monitor budget, provider errors, latency, and rollback behavior before widening exposure.
