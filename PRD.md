# PRD — Ultrawork Completion: Mocks, Speaking V2, Real Content

Last updated: 2026-05-21 22:59:00 +05:00

## Goal
Complete the remaining work across mocks strict UX/business workflow, Speaking V2/live tutor/admin configurability, and Project Real Content production readiness while preserving all existing dirty worktree changes.

## Locked Decisions
- Preserve current modified and untracked work; no reset/clean/branch switch.
- Heavy validation/build/test runs only inside local Docker Desktop containers per `AGENTS.md`; the production VPS (`185.252.233.186`) is for deployment only.
- Production deploy remains explicitly gated after local validation; content publishing remains explicitly review-gated.
- Strict mock enforcement applies to final-readiness and explicit exam-mode mocks.
- Mock booking reschedule limit is admin-configurable, default `2`.
- Real Content import stages only missing/non-duplicate content as Drafts.
- LiveKit and AI routing choices are admin-panel configurable.
- All text-to-speech (vocabulary, conversation, recall, and listening audio) is generated via ElevenLabs, configured in admin Voice Design.

## Worktree Snapshot

````text
## mocks-phase6-verify...origin/mocks-phase6-verify
 M .codex/config.toml
 M .env.example
 M app/admin/analytics/mocks/page.tsx
 M app/admin/content/mocks/item-analysis/page.tsx
 M app/admin/onboarding/interlocutor/page.tsx
 M app/mocks/bookings/new/page.tsx
 M backend/src/OetWithDrHesham.Api/Domain/ReadingEntities.cs
 M backend/src/OetWithDrHesham.Api/Endpoints/MockAnalyticsEndpoints.cs
 M backend/src/OetWithDrHesham.Api/Services/LearnerService.cs
 M backend/src/OetWithDrHesham.Api/Services/Reading/ReadingAttemptService.cs
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

## Dirty Work Classified by Area

### ConfigDocsCi
- tracked-modified: `.codex/config.toml`
- tracked-modified: `.env.example`
- untracked: `.github/actions/setup-oet-stack/action.yml`
- untracked: `.github/CODEOWNERS`
- untracked: `.github/PULL_REQUEST_TEMPLATE/speaking.md`
- untracked: `.github/workflows/speaking-a11y.yml`
- untracked: `.github/workflows/speaking-ci.yml`
- untracked: `.github/workflows/speaking-content-batch.yml`
- untracked: `.github/workflows/speaking-e2e.yml`
- untracked: `.github/workflows/speaking-load.yml`
- untracked: `CHANGELOG.md`
- untracked: `docs/ci/speaking.md`
- untracked: `docs/desktop/speaking-recording.md`
- untracked: `docs/env/speaking.md`
- untracked: `docs/mobile/speaking-recording.md`
- untracked: `ops/dashboards/README.md`
- untracked: `ops/dashboards/speaking-funnel.json`
- untracked: `ops/dashboards/speaking-livekit.json`
- untracked: `ops/dashboards/speaking-quality.json`

### Mocks
- tracked-modified: `app/admin/analytics/mocks/page.tsx`
- tracked-modified: `app/admin/content/mocks/item-analysis/page.tsx`
- tracked-modified: `app/mocks/bookings/new/page.tsx`
- tracked-modified: `backend/src/OetWithDrHesham.Api/Endpoints/MockAnalyticsEndpoints.cs`
- untracked: `app/admin/content/mocks/[bundleId]/review-pipeline/page.tsx`
- untracked: `components/domain/admin/MockItemAnalysisActions.tsx`
- untracked: `components/domain/admin/MockReviewStageRail.tsx`
- untracked: `tests/a11y/helpers/axe-runner.ts`
- untracked: `tests/a11y/README.md`
- untracked: `tests/a11y/speaking-flows.a11y.spec.ts`
- untracked: `tests/a11y/speaking-home.a11y.spec.ts`
- untracked: `tests/load/lib/auth-helper.js`
- untracked: `tests/load/README.md`
- untracked: `tests/load/speaking-livekit-token.k6.js`
- untracked: `tests/load/speaking-session-create.k6.js`

### Other
- tracked-modified: `app/admin/onboarding/interlocutor/page.tsx`
- tracked-modified: `backend/src/OetWithDrHesham.Api/Domain/ReadingEntities.cs`
- tracked-modified: `backend/src/OetWithDrHesham.Api/Services/LearnerService.cs`
- tracked-modified: `backend/src/OetWithDrHesham.Api/Services/Reading/ReadingAttemptService.cs`
- tracked-modified: `lib/api.ts`
- untracked: `docs/analytics/speaking-events.md`
- untracked: `docs/dev/quickstart-speaking.md`
- untracked: `docs/load-testing/speaking-budgets.md`
- untracked: `lib/desktop/speaking-audio-bridge.ts`
- untracked: `lib/native/audio-recorder-bridge.ts`
- untracked: `lib/native/capacitor-permissions.ts`
- untracked: `scripts/seed-speaking-dev.ps1`
- untracked: `scripts/seed-speaking-dev.sh`

### Speaking
- untracked: `components/domain/speaking/AiPatientAvatar.tsx`
- untracked: `docs/security/speaking/abuse-cases.md`
- untracked: `docs/security/speaking/attack-surface.md`
- untracked: `docs/security/speaking/checklist.md`
- untracked: `docs/security/speaking/data-classification.md`
- untracked: `docs/security/speaking/key-rotation.md`
- untracked: `docs/security/speaking/penetration-test-scope.md`
- untracked: `docs/security/speaking/README.md`
- untracked: `docs/security/speaking/threat-model.md`
- untracked: `docs/speaking/ai-providers.md`
- untracked: `docs/speaking/api-surface.md`
- untracked: `docs/speaking/architecture.md`
- untracked: `docs/speaking/changelog.md`
- untracked: `docs/speaking/compliance.md`
- untracked: `docs/speaking/content-model.md`
- untracked: `docs/speaking/contributing.md`
- untracked: `docs/speaking/data-model.md`
- untracked: `docs/speaking/diagrams/sequence-self-practice.mmd`
- untracked: `docs/speaking/glossary.md`
- untracked: `docs/speaking/incident-runbook.md`
- untracked: `docs/speaking/livekit.md`
- untracked: `docs/speaking/post-mortems/.gitkeep`
- untracked: `docs/speaking/post-mortem-template.md`
- untracked: `docs/speaking/README.md`
- untracked: `docs/speaking/release-checklist.md`
- untracked: `docs/speaking/scoring.md`
- untracked: `docs/speaking/sla.md`
- untracked: `docs/speaking/state-machines.md`
- untracked: `scripts/speaking-smoke.ps1`
- untracked: `scripts/speaking-smoke.sh`


## Acceptance Criteria
- PRD/PROGRESS are current before implementation waves.
- Mocks strict player, writing 5+40, booking settings, entitlement/readiness/QC/analytics are coherent and tested.
- Speaking V2 has canonical session UX, typed recording pipeline, visible recorder errors, admin-configurable LiveKit/AI/TTS, and tests for configured/disabled states.
- Real Content production readiness is re-audited without using the VPS for validation; deploy/import/publish steps are evidenced, with publish gated by explicit approval.
- Local Docker validation commands are recorded with pass/fail evidence in PROGRESS.md.
