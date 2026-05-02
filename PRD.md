# Phase 2 PRD: Platform Optimization & Navigation Architecture

Source: `OET_Platform_Functional_PRD.md` attached by the client on 2026-05-02.

## Business Goals

- Reduce registration abandonment by removing nonessential session and billing choices from initial account creation.
- Keep learner navigation simple by making Recalls the single surface for vocabulary recall and pronunciation playback.
- Protect click-to-hear Recalls pronunciation audio as a paid candidate feature.

## Functional Requirements

### Registration

- Remove the `Session` dropdown from all sign-up UI paths.
- Remove the `Session Summary` card from all sign-up UI paths.
- Remove `Published Billing Plans` from the sign-up view.
- Make `Target Country` mandatory.
- The target country options must be exactly:
  - United Kingdom
  - Ireland
  - Scotland
  - USA
  - Australia
  - New Zealand
  - Canada
  - Gulf Countries
  - Other Countries
- Backend registration must accept every visible target country option.
- Sign-up submission must not require `sessionId`.

### Navigation

- Remove `Pronunciation` from the learner sidebar.
- Retain `Recalls` as the primary learner study tab.

### Recalls Audio

- Clicking a Recalls word must attempt pronunciation audio playback.
- Recalls audio playback must be available only to registered/paid candidates.
- Free/frozen candidates must see an upgrade prompt instead of hearing audio.
- Backend must enforce the paid gate; frontend checks are only UX.
- Learner payloads must not leak cached playable audio URLs that bypass the paid gate.
- Recalls audio/listen-and-type calls must use vocabulary term IDs, not learner card IDs.

## Production Readiness Criteria

- TypeScript type-check passes.
- ESLint passes for touched frontend files.
- Full Vitest suite passes.
- Focused backend tests pass for registration country/session behavior and Recalls audio entitlement/redaction behavior.
- Independent review confirms no critical PRD gaps remain.