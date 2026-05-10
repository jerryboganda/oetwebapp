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

---

# 2026-05-10 PRD Addendum: OET Reading Rulebook Closure

Source: user-requested ultrawork/Ralph loop for closing all OET Reading rulebook gaps. Existing repo context: Reading uses structured objective marking rather than `rulebooks/reading` JSON files; implementation must extend the existing Reading subsystem documented in `docs/READING-MODULE-A-Z-IMPLEMENTATION-PLAN.md` and avoid creating a parallel module.

## Goal

Ship Reading as a production-ready OET simulator: canonical structure, strict server-side marking and timing, one canonical learner route family, computer-delivered exam tools, paper-mode simulation, full regression coverage, and a green production build.

## Non-Negotiable Requirements

- Production build must pass after Reading changes: type-check, lint, relevant unit tests, backend build/tests, Playwright smoke, and `npm run build`.
- Canonical learner route is `/reading/paper/[paperId]` with `/reading/paper/[paperId]/results?attemptId=...`; legacy `/reading/player/[id]`, `/reading/results/[id]`, and legacy `/v1/reading/attempts/*` paths must be redirected, disabled, or removed without breaking valid saved links.
- Backend owns all structure, timing, and marking decisions. Published exam-mode papers require Part A 20 items, Part B 6 items, Part C 16 items, total 42, Part A 15 minutes, B/C shared 45 minutes, no answer-key leakage, idempotent submit, and canonical raw-to-scaled scoring through `OetScoring`/`lib/scoring.ts` only.
- Computer-delivered UI must include exam-faithful timer state, answered/unanswered/flagged navigation, per-question autosave, Part A lockout, B/C auto-submit, keyboard-accessible controls, highlight/notes/strike-through where supported, and mobile warning for strict exam mode.
- Paper simulation must support original PDF access, printable/paper practice mode, answer-sheet style entry/review, Part A collection behavior at 15 minutes, and clear labeling when paper practice is not a strict computer-delivered attempt.
- Tests must cover route shutdown, backend structure validation, answer redaction, strict timing, marking edge cases, computer UI tools, paper simulation flows, and build-time regressions.
- Validation evidence must be appended to `PROGRESS.md` before completion, including commands run, pass/fail status, known unrelated blockers, and reviewer signoff.

## Execution Slices

- R0: Baseline and production-build audit. Confirm current Reading failures, stale routes, and build blockers before code changes.
- R1: Legacy route shutdown. Replace internal links, add safe redirects or gone responses, and test old saved links.
- R2: Backend strictness. Harden publish validation, learner DTO redaction, timing enforcement, submit idempotency, and canonical scoring boundaries.
- R3: Computer UI tools. Complete timer/navigator/autosave/flag/highlight/strike-through/notes accessibility in the canonical player.
- R4: Paper simulation. Add or verify printable original paper, answer-sheet flow, Part A collection simulation, and paper-mode result review.
- R5: Regression tests. Add focused backend, Vitest, and Playwright coverage for every PRD rule above.
- R6: Final validation and review. Run full required checks, record evidence, and perform independent Reading gap review.

## Completion Criteria

- No active learner or backend code path depends on the legacy Reading attempt/player surfaces except intentional redirects or compatibility responses.
- A learner can start, autosave, complete, and review a full 42-item Reading paper in strict exam mode and paper simulation mode.
- Incorrect client behavior cannot bypass backend timing, structure, answer secrecy, or grading rules.
- All required validation commands pass or have documented unrelated blockers accepted before release.