# Phase 2 PRD Progress

Updated: 2026-05-02

## Completed Before This Loop

- Active and legacy registration forms no longer render Session selection, Session Summary, or Published Billing Plans.
- Shared fixed target country list exists for the sign-up UI.
- Target Country select is required in the UI.
- Registration success page no longer displays session or billing fields.
- `sessionId` has been removed from frontend/backend registration contracts; legacy JSON `sessionId` input is ignored and never persisted.
- Learner sidebar no longer includes a Pronunciation nav tab and retains Recalls.
- Recalls word click path and upgrade modal were added.
- Initial Recalls audio backend endpoint returns 402 for learners without active entitlement.

## Gaps Found In Fresh Audit

- Backend registration still validates country against profession-specific country lists, which can reject PRD-required visible options.
- Recalls queue and quiz payloads still expose cached `audioUrl` values to learners.
- Recalls cards page passes learner card IDs to term-only audio/listen-and-type endpoints.
- Some Recalls components can play cached audio URLs directly before hitting the gated backend endpoint.
- Registration copy still references `session updates` after session removal.

## Current Implementation Tasks

- [x] Add backend canonical target country allowlist and validate registration against it.
- [x] Seed learner goals/bootstrap from the registered target country instead of a hardcoded default.
- [x] Enforce the canonical target country allowlist in goals and settings updates.
- [x] Replace the study settings target country free-text field with the fixed PRD select list.
- [x] Add `termId` to Recalls queue DTO and use it for audio/listen-and-type calls.
- [x] Redact learner-facing Recalls cached audio URLs from queue/quiz payloads.
- [x] Force Recalls playback components through the gated audio endpoint.
- [x] Consolidate remaining student-visible standalone Pronunciation entry points into Recalls.
- [x] Add frontend and backend regression tests for the PRD-critical behavior.
- [x] Remove signup catalog `sessions`/`billingPlans` from the backend public contract.
- [x] Verify legacy `sessionId` registration payloads are ignored at the API boundary.
- [x] Remove unused frontend `enrollmentSessions` fallback data to prevent session UI drift.
- [x] Align aggregate settings study output with canonical learner goal target country.
- [x] Re-run type-check, lint, frontend tests, backend tests, and production build.
- [x] Run independent final review.

## Validation Completed

- `npx tsc --noEmit` passed.
- `npm run lint` passed.
- Focused Vitest registration/scoring tests passed: 3 files, 80 tests.
- Full Vitest passed: 134 files, 860 tests.
- Focused backend auth/settings target-country tests passed: 89 tests.
- Focused backend auth/settings target-country tests passed after final cleanup: 89 tests.
- Focused backend auth/catalog tests passed after signup API-boundary cleanup: 63 tests.
- Full backend suite passed after final target-country cleanup: 876 tests.
- Final `npx tsc --noEmit` and `npm run lint` passed after cleanup.
- Final `npm run build` passed with the existing Prisma/OpenTelemetry/Sentry dynamic-import warning.
- Independent final review found no blocking PRD gaps across registration, target-country propagation, settings/goals enforcement, scoring, and Recalls audio.
