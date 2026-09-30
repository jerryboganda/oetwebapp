# Live Voice Agent and Tutor Room Design

## Scope

This release completes both Speaking realtime products before launch:

1. AI patient role-play on every published Speaking card.
2. Human tutor rooms using the real LiveKit lifecycle.

Speaking has one interaction mode: native realtime voice. The existing batch
ASR, text model, and TTS turn loop is not a user-facing fallback.

## AI voice architecture

The browser owns the low-latency media path. The API owns authorization,
provider selection, source-grounded card state, consent, transcript metadata,
Jev advisory judgments, persistence, and audit.

The primary provider is OpenAI GPT-Live 1. The backend probes the account in a
protected CI environment before a production model is pinned. OpenAI sessions
use the browser WebRTC offer and the backend `POST /v1/live/sessions` SDP
negotiation. The OpenAI API key never reaches the browser.

The compatibility provider is Gemini Live. The backend mints a single-use,
short-lived Gemini Live ephemeral token with server-locked model, audio
modality, session resumption, and system instructions. The browser connects
directly to the constrained WebSocket. The Gemini API key never reaches the
browser.

The session contract exposes the selected provider and model before the
microphone permission request. A session uses one provider. Provider switching
is allowed only before the first turn or during a reconnect boundary, never in
the middle of a spoken turn. Provider errors are visible and terminal for that
session. There is no silent switch to mock audio, batch text, or text-only mode.

> **Update 2026-09-30 (live voice close-out; supersedes the two sentences above where they differ, pending production
> verification).** Provider failure is no longer terminal while the connection is still being set up. The server returns
> an ordered `candidates` list on the preflight (the configured primary first, then the other provider, each only if it is
> configured, catalog-verified and its circuit is not open) and the browser tries them in order: a create call that fails
> (any provider status, a timeout, an unreadable answer; the browser sees a generic 503) is followed by the next real
> provider automatically. Once a provider's link is live it serves that card to the end: still no reconnect and no switch
> in the middle of a conversation. The error is visible and terminal only when every candidate failed (one generic
> message and a retry); when no provider is usable at page load the recorder fallback replaces live voice for the session.
> There is still no switch to mock audio, batch text or text-only. Health is fed by real session-creation outcomes (a
> per-provider circuit, admin health and reset endpoints); the server also enforces a hard duration cap (deadline, hard
> stop, an ai-worker sweeper, Gemini token expiry, an OpenAI hang-up) that this design did not have. The preflight
> disclosure now names every candidate, while the learner-facing consent copy stays generic: an open owner/legal
> decision. Current behaviour and numbers: [`docs/speaking/live-voice.md`](../../speaking/live-voice.md).

The server creates a safe card projection and source-grounded interlocutor
instructions. Candidate-visible card data is available to the client. Hidden
facts, resistance rules, closing cues, and state transitions are delivered to
the provider only through the server-controlled session configuration and
approved state tools. Raw hidden scripts are not returned to the browser.

The browser uses provider-native VAD, interruption, and streamed audio. It
renders partial transcripts and output captions, stops output on barge-in, and
records provider/session metadata. The existing Speaking recorder remains the
recorder for the Free Speaking Mock. No second recorder is introduced.

Jev runs in parallel with the voice loop. It receives narrow typed questions
about role adherence, clinical appropriateness, unsafe content, and source
contradictions. It never blocks audio, generates speech, or replaces the
canonical Speaking grader. Every call is server-side, budgeted, fail-soft, and
written to the existing AI usage ledger.

## Content and free access

Each profession has one admin-designated free Writing case note and one
admin-designated free Speaking card. Selection is deterministic:

1. use the admin choice when it is eligible and published for that profession;
2. otherwise use the first eligible published item ordered by stable publish
   order and ID;
3. return an explicit no-item state when none exists.

Generated missing content is created before publication from authoritative
source material. The persisted record includes provenance, source digest,
generator/model metadata, and Jev contradiction/grounding validation. A
content item with unresolved uncertainty is `needs_owner_input` and cannot be
selected for free access or a published voice session.

The zero-credit Free Speaking Mock uses the existing recorder and follows
`record -> submit -> AI grading -> result`. The free entitlement is exactly one
Speaking card per profession and does not debit the learner wallet. Submission
and grading are idempotent. A failed real provider call remains an error and
does not manufacture a result.

## Human LiveKit room

Human rooms remain separate from AI voice sessions. The complete state machine
is booking/payment or entitlement, tutor assignment, scheduled provisioning,
learner/tutor join, consent, realtime audio/video, cues, recording/egress,
verified webhooks, completion, assessment/review, cancellation, expiry,
retries, and audit.

Production cannot select the stub gateway. Provisioning is scheduled from the
booking start window, join tokens are scoped to the participant role, recording
requires consent, egress events are idempotent, and the final assessment/review
state is persisted before the room is released.

## Acceptance evidence

Launch is blocked until all of these have real evidence:

- provider probe identifies the enabled low-latency production models;
- OpenAI realtime voice completes every published card end to end;
- Gemini compatibility/parity completes every published card plus
  representative live conversations;
- the zero-credit free Speaking recorder flow completes through a real result;
- the free Writing/Speaking deterministic resolver and admin designation work;
- the complete LiveKit lifecycle completes with real participants and egress;
- consent, bounded retention, audit, no-fallback errors, Jev advisory
  telemetry, accessibility, and security checks pass;
- GitHub Actions builds and tests the committed SHA, deployment succeeds, and
  production health plus live provider smoke evidence are green.

The local machine is limited to inspection and edits. Build, test, lint,
provider probes, browser E2E, and deployment verification run in GitHub
Actions or production smoke infrastructure only.
