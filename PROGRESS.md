# PROGRESS — Ultrawork Completion

Last updated: 2026-05-22 02:30:20 +05:00

## Guardrails
- No destructive git actions.
- No heavy local npm/dotnet/docker/build/test commands.
- Use `ssh oet-dev "cd /opt/oetwebapp && <command>"` for heavy validation.
- Preserve existing modified/untracked work; `.codex/config.toml` remains isolated tooling/config unless intentionally committed.

## Completed This Session
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
- Typed SpeakingSession upload/assessment pipeline appears partially present already; needs remote compile/tests and targeted endpoint review before claiming complete.
- Real Content production audit/import/deploy verification has not started in this continuation because code implementation/compile safety is first.

## Next
- Run remote validation from `/opt/oetwebapp` after ensuring local changes are synced/available there.
- Fix compile/test failures.
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
