# PROGRESS — Ultrawork Completion

Last updated: 2026-05-26 15:50 +05:00

## Guardrails
- No destructive git actions.
- Heavy validation/build/test commands run only inside local Docker Desktop containers per `AGENTS.md`.
- Do not use `oet-dev` for validation; the VPS is production deployment only.
- Preserve existing modified/untracked work; `.codex/config.toml` remains isolated tooling/config unless intentionally committed.

## Completed This Session
- Zoom live classes completion slice advanced across backend, expert UX, security headers, and focused test helpers.
- Routed Zoom effective settings through `IRuntimeSettingsProvider` with runtime-visible `ZoomSettings`, including SDK credentials, webhook secret token, retry tolerance, and sandbox fallback.
- Rebuilt `ZoomMeetingService` around runtime-backed settings for Zoom OAuth, meeting creation, SDK key/signature generation, webhook URL validation, and timestamp/signature verification.
- Hardened Zoom webhooks with a 1 MB request body cap, future/past timestamp tolerance, URL-validation response handling, and attendance finalization on `meeting.ended`.
- Moved live-class enrollment charges/refunds onto canonical `WalletService` debit/credit paths and made wallet transactions ambient-transaction aware.
- Added expert live-class listing support at `/v1/expert/live-classes` and wired `/expert/live-classes` to assigned class sessions with host Zoom token preparation and embedded/direct fallback behavior.
- Extended CSP allowlists for Zoom Meeting SDK connect/media/frame/worker/script needs in both middleware response CSP and root meta CSP without broad wildcarding all origins.
- Updated live-class/Zoom test helpers for the async runtime-backed join-token flow and new runtime settings contract.
- Reading pathway implementation slice stabilized end to end across backend DTOs/endpoints, frontend typed client, onboarding, diagnostic, diagnostic results, pathway CTAs, and lightweight practice completion.
- Added learner-safe diagnostic question projection at `/v1/reading-pathway/diagnostic/sessions/{sessionId}/questions`; it returns question/passage/options metadata without answer keys, accepted synonyms, or explanations.
- Added diagnostic result reload support at `/v1/reading-pathway/diagnostic/sessions/{sessionId}/results` and wired the results page to fall back from `sessionStorage` to the API.
- Routed diagnostic scaled-score estimates through canonical `OetScoring` and frontend grade labels through `lib/scoring.ts`, preserving the Reading invariant that `30/42 == 350/500`.
- Hardened diagnostic submit and practice answer boundaries: diagnostic submit now requires a learner-owned diagnostic session, repeat diagnostic submits return a structured error, diagnostic/mock sessions cannot use the per-question correctness endpoint, and practice answers must belong to the session question list.
- Replaced placeholder diagnostic UI with real rendered diagnostic questions/passages and normalized option handling for MCQ/text-answer flows.
- Updated Reading pathway client routes away from stale `/sessions`, `/daily-plan`, `/analytics`, and `/community` paths to the backend routes actually implemented under `/v1/reading-pathway`.
- Added a focused Reading pathway API contract test at `lib/__tests__/reading-pathway-api.test.ts` covering onboarding fields, diagnostic question route, daily-plan adapter, practice submit route, skill radar adapter, and mock-result adapter.
- Added generated Reading pathway schema migration `20260525225452_AddReadingPathwaySchemaGenerated` and refreshed `LearnerDbContextModelSnapshot.cs`; EF tooling reports no pending model changes.
- Added focused backend Reading pathway endpoint tests covering safe diagnostic projection, cross-user rejection, locked diagnostic answer rejection, out-of-session answer rejection, repeat diagnostic submit rejection, and lesson wrapper contracts.
- Captured initial dirty worktree and classified files by area in `PRD.md`.
- Added mock section contract fields for strict-player metadata (`partGroup`, replay/read/edit timing, case notes) in `lib/mock-data.ts` and `lib/api.ts`.
- Added strict mock player components:
  - `components/domain/mock-player/ListeningStrictLayer.tsx`
  - `components/domain/mock-player/PartAStrictTimer.tsx`
  - `components/domain/mock-player/WritingCaseNotePanel.tsx`
  - `components/domain/mock-player/WritingPhaseTimer.tsx`
- Added `/mocks/writing/[sectionAttemptId]` route with 5-minute read-only case-note phase, 40-minute editor phase, local autosave, and submit-on-expiry/manual submit path.
- Updated mock player to gate strict exam/final-readiness launches behind webcam preflight + fullscreen request, show Listening strict locks, and surface Reading Part A timer.
- Updated mock player unit test to satisfy strict webcam gate through a deterministic mock panel.
- Changed legacy booking policy default from 3 to 2 in `MockBookingService` and fixed learner booking copy.
- Pointed mock writing launch route generation to `/mocks/writing/[sectionAttemptId]` instead of the generic writing player.
- Exposed DigitalOcean Serverless Inference Qwen3 TTS as an admin-selectable conversation TTS provider using the existing encrypted ChatTTS/OpenAI-compatible endpoint fields.
- Added backend TTS selector/DI/provider support for `digitalocean-qwen3-tts`, preferring it ahead of ElevenLabs in auto mode when configured.
- Extended diagnostic speaking recording limit from the previous 3-second sample to a full diagnostic window.
- Improved visible speaking recorder errors for missing/blocked microphone, missing browser `MediaRecorder`, and recorder start failures.

## In Progress / Needs Validation

### Zoom live classes slice validation status

- Editor diagnostics are clean for all touched Zoom/live-class backend, frontend, middleware, and test files.
- Local Docker Desktop validation is pending for focused backend live-class/Zoom tests and frontend type/lint coverage.
- Independent review pass is pending after Docker validation.

### Reading pathway slice validation status

- Editor diagnostics are clean for all edited Reading pathway backend/frontend/test/migration files.
- Passed focused frontend contract validation in Docker Desktop using a Linux-side source copy to avoid Windows bind-mount Vitest hangs:
  - `node:22-alpine` + `vitest run lib/__tests__/reading-pathway-api.test.ts` -> 1 file / 6 tests passed.
- Passed focused frontend lint/type validation in Docker Desktop using the same Linux-side source copy:
  - `node:22-alpine` + focused `eslint` over the Reading pathway client/pages/test -> passed with `--max-warnings=0`.
  - `node:22-alpine` + temporary focused `tsconfig` for `lib/reading-pathway-api.ts`, `app/reading/diagnostic-results/page.tsx`, and `components/reading/TodayPlan.tsx` -> passed.
- Docker API package restore passed in `mcr.microsoft.com/dotnet/sdk:10.0.201` with the existing `NU1510` warning for `System.Text.Encoding.CodePages`.
- Backend build completed in `mcr.microsoft.com/dotnet/sdk:10.0.201` from a Linux-side source volume; build succeeded with existing nullability/unreachable-code warnings and 0 errors.
- Passed focused backend validation in Docker Desktop:
  - `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --no-build --filter FullyQualifiedName~ReadingPathwayEndpointTests` -> 5 tests passed.
- EF validation in Docker Desktop:
  - `dotnet ef migrations list --no-build --no-connect` lists `20260525225452_AddReadingPathwaySchemaGenerated`.
  - `dotnet ef migrations has-pending-model-changes --no-build` -> `No changes have been made to the model since the last migration.`
- Full frontend `npx tsc --noEmit` is not claimed green: the broad attempt exposed validation-environment/baseline noise outside this slice, including the cached Docker `node_modules` volume missing the `@testing-library/dom` peer and existing unrelated TypeScript errors in unmodified test/page files.

> Note: historical remote validation evidence below predates the current `AGENTS.md` rule. Future validation must run locally in Docker Desktop containers, not on `oet-dev`.

- Remote validation was run against an isolated `oet-dev` validation worktree at `/tmp/oet-ultra-validation` to avoid overwriting `/opt/oetwebapp` dirty work.
- Passed remote frontend/unit validation:
  - `ssh oet-dev "cd /tmp/oet-ultra-validation && npm test"` -> 218 files / 1395 tests passed.
  - `ssh oet-dev "cd /tmp/oet-ultra-validation && npx tsc --noEmit && npm run lint && npm run check:encoding"` -> passed after encoding cleanup.
  - `ssh oet-dev "cd /tmp/oet-ultra-validation && npm run build"` -> passed.
- Backend targeted regressions fixed and passed:
  - `AuthFlowsTests.AuthResponseContracts_SerializeAndDeserializeWithExpectedShape`
  - `LearnerSpecRegressionTests.MockAttemptCreation_PersistsReviewSelectionAndLaunchRoutes`
- Full backend test run still needs a clean uninterrupted completion. It initially exposed the two fixed contract regressions above, then a later full run exceeded 60 minutes with no final pass/fail summary. A follow-up diagnostic run with `--blame-hang` also exceeded its 15-minute watchdog after reaching later upload authorization tests and did not identify a new code assertion failure before timeout.
- Mock booking max-reschedule is now code-default 2; a full persisted admin setting still needs a DB-backed settings surface if strict runtime editability is required beyond the current admin/runtime provider work.
- LiveKit remains config-backed in `LiveKitOptions`; a full encrypted admin runtime settings panel for LiveKit secrets is still pending.
- Typed SpeakingSession upload/assessment pipeline appears partially present already; needs local Docker compile/tests and targeted endpoint review before claiming complete.
- Real Content production audit/import/deploy verification has not started in this continuation because code implementation/compile safety is first.

## Next
- Decide whether to refresh/fix the frontend dependency baseline (`@testing-library/dom` peer in Docker node_modules/package lock) before attempting a full repo `npx tsc --noEmit` again.
- Triage existing unrelated full-repo TypeScript errors in unmodified tests/pages separately from the Reading pathway slice.
- Run broader local Docker validation per `AGENTS.md` once the current baseline blockers are addressed.
- Continue with DB-backed admin settings for mock policy/LiveKit if validation is green enough to extend safely.
- Re-run production audit and Real Content import staging only after validation.

## Initial Git Status

````text
## mocks-phase6-verify...origin/mocks-phase6-verify
 M .codex/config.toml
 M .env.example
 M app/admin/analytics/mocks/page.tsx
 M app/admin/content/mocks/item-analysis/page.tsx
 M app/admin/onboarding/interlocutor/page.tsx
 M app/mocks/bookings/new/page.tsx
 M backend/src/OetLearner.Api/Domain/ReadingEntities.cs
 M backend/src/OetLearner.Api/Endpoints/MockAnalyticsEndpoints.cs
 M backend/src/OetLearner.Api/Services/LearnerService.cs
 M backend/src/OetLearner.Api/Services/Reading/ReadingAttemptService.cs
 M lib/api.ts
?? .github/CODEOWNERS
?? .github/PULL_REQUEST_TEMPLATE/
?? .github/actions/
?? .github/workflows/speaking-a11y.yml
?? .github/workflows/speaking-ci.yml
?? .github/workflows/speaking-content-batch.yml
?? .github/workflows/speaking-e2e.yml
?? .github/workflows/speaking-load.yml
?? CHANGELOG.md
?? app/admin/content/mocks/[bundleId]/review-pipeline/
?? components/domain/admin/MockItemAnalysisActions.tsx
?? components/domain/admin/MockReviewStageRail.tsx
?? components/domain/speaking/AiPatientAvatar.tsx
?? docs/analytics/
?? docs/ci/
?? docs/desktop/
?? docs/dev/
?? docs/env/
?? docs/load-testing/
?? docs/mobile/
?? docs/security/speaking/
?? docs/speaking/README.md
?? docs/speaking/ai-providers.md
?? docs/speaking/api-surface.md
?? docs/speaking/architecture.md
?? docs/speaking/changelog.md
?? docs/speaking/compliance.md
?? docs/speaking/content-model.md
?? docs/speaking/contributing.md
?? docs/speaking/data-model.md
?? docs/speaking/diagrams/
?? docs/speaking/glossary.md
?? docs/speaking/incident-runbook.md
?? docs/speaking/livekit.md
?? docs/speaking/post-mortem-template.md
?? docs/speaking/post-mortems/
?? docs/speaking/release-checklist.md
?? docs/speaking/scoring.md
?? docs/speaking/sla.md
?? docs/speaking/state-machines.md
?? lib/desktop/
?? lib/native/
?? ops/
?? scripts/seed-speaking-dev.ps1
?? scripts/seed-speaking-dev.sh
?? scripts/speaking-smoke.ps1
?? scripts/speaking-smoke.sh
?? tests/a11y/
?? tests/load/
````
