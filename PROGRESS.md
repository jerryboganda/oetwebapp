# Master Remaining Work Audit

Updated: 2026-05-05

## Status

- Completed a multi-agent read-only discovery pass across UX docs, frontend, backend/API, validation, deployment/ops, documentation, and risk assumptions.
- User decisions captured by popup: current worktree baseline, all-platform commercial readiness target, OET plus IELTS scope, CI plus E2E plus manual signoff evidence gate, do not depend on deleted strategy docs today, and fail closed for production mocks/stubs.
- Phase 8 artifacts created:
  - `docs/ux/phase-8/backlog.md`
  - `docs/ux/phase-8/status-register.md`
  - `docs/ux/phase-8/rollout-plan.md`
  - `docs/ux/phase-8/validation-plan.md`
  - `docs/ux/phase-8/governance-playbook.md`

## Top Remaining Themes

- Freeze and review the active dirty worktree before claiming completion.
- Reconcile UX route inventory and execute evidence capture/scorecards for T0/T1 routes.
- Fail closed for production mocks/stubs and wire real provider readiness for AI, ASR, TTS, PDF extraction, and sandbox integrations.
- Complete sponsor/institutional billing, CI/release gates, staging, deploy rollback, cross-platform signing/device validation, and manual accessibility signoff.
- Complete explicit security/privacy launch review covering authz/RBAC, secrets, PII/audio retention, audit logs, data rights, dependency scans, and incident response.
- Continue OET plus IELTS learner-outcome work after release blockers are under evidence control.

---

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

---

# Product Strategy Documents (01-07) Implementation Phase

Updated: 2026-05-02

## Scope

Complete implementation of remaining tasks from the 7 product strategy documents, grouped into 7 parallel tracks by domain.

## Track 1: Billing & Entitlements — COMPLETED

- **OET Tier Packaging Landing Page** (`/billing/plans`): Created learner-facing tier comparison (Free / Core / Plus / Review) with feature matrices, monthly/annual toggle, and checkout routing to existing billing infrastructure.
- **Entitlement Category System** (`lib/entitlement-categories.ts`): Defined canonical entitlement categories (diagnostic, practice_questions, ai_evaluation, mock_exams, expert_review, etc.) and tier-to-entitlement mapping for Free, Core, Plus, and Review tiers. Includes exam-family overrides for IELTS (reduced mocks) and PTE (deferred entitlements).
- **Exam-family packaging strategy foundation**: Tier entitlement mapping supports OET (full), IELTS (partial), and PTE (deferred) per Document 07.

## Track 2: Exam-Family Core Abstraction — COMPLETED

- **Exam-Family Scoring Dispatcher** (`lib/exam-family-scoring.ts`): Shared-core module that dispatches score formatting, grade display, target validation, and readiness band mapping to the correct exam-specific module (OET, IELTS, PTE). Prevents hardcoded OET assumptions in shared workflows.
- **IELTS Scoring Module** (`lib/ielts-scoring.ts`): Full IELTS band scoring with 0-9 scale, 0.5 increments, raw→band mapping for Listening/Reading, weighted Writing band (Task 1 40% / Task 2 60%), Speaking band, overall band, Academic vs General pathway labels, and score validation.
- **PTE Scoring Foundation** (`lib/pte-scoring.ts`): PTE types and helpers (10-90 scale, clamping, validation, readiness bands) as deferred foundation per product strategy.
- **Goals page IELTS pathway integration**: Added Academic/General Training selection to the goals form, stored in `UserProfile.ieltsPathway`, and wired through the API submission.

## Track 3: AI Trust & Expert SLA — COMPLETED

- **Admin AI Escalation Stats Types** (`lib/types/admin.ts`): Added `AdminAIEscalationStats` (total evaluations, escalation rate, divergence, subtest breakdown, 30-day trend) and `AdminAIConfidencePolicy` (band thresholds, human-review recommendation, learner/provenance labels, disclaimer) to the `AdminAIConfig` type.
- **Admin AI Config Escalation Visibility** (`app/admin/ai-config/page.tsx`): Added escalation rate summary card and per-config escalation column to the DataTable. Displays rate badges with color-coded thresholds (<5% success, <15% warning, ≥15% danger).
- **Admin AI Confidence Policy Controls** (`app/admin/ai-config/page.tsx`): Extended the create/edit modal with a full Confidence Policy section: band selector, min/max thresholds, human-review checkbox, learner label, provenance label, and disclaimer fields.

## Track 4: OET Flagship Deepening — COMPLETED

- **Profession-Specific Writing Remediation** (`lib/writing-remediation-professions.ts`): Comprehensive profession-aware coaching tips for Medicine, Nursing, Dentistry, Pharmacy, Physiotherapy, Occupational Therapy, Dietetics, Speech Pathology, Radiography, Podiatry, Optometry, and Veterinary. Each includes criterion code, title, description, weak/strong examples, and priority.
- **Writing Result Integration** (`app/writing/result/page.tsx` + `components/domain/profession-remediation-callout.tsx`): Fetches the user's profession from their profile and displays profession-specific coaching callout with weak vs strong example boxes directly on the writing result page.
- **Profession-Specific Speaking Coaching** (`lib/speaking-coaching-professions.ts`): Speaking coaching guidance for Medicine, Nursing, Dentistry, Pharmacy, and Physiotherapy covering relationship_building, information_gathering, and explanation_planning criteria with actionable drill suggestions.

## Track 5: IELTS Operational Layer — COMPLETED

- **IELTS Scoring Module** (see Track 2): Full canonical IELTS scoring with official band mappings and Academic/General pathway support.
- **IELTS Guide Landing Page** (`/app/ielts-guide/page.tsx`): Learner-facing IELTS guide explaining the four skills, band scale, Academic vs General distinction, shared OET core engine, Writing task weighting, and deferred-feature transparency.
- **Goals IELTS Pathway Selection**: Academic/General Training dropdown added to goals form with exam-family-conditional display.

## Track 6: Content Ops & Analytics — COMPLETED

- **Content Provenance & QA Types** (`lib/content-provenance.ts`): Structured types for ContentSource (manual_expert, ai_draft, ai_draft_expert_review, import_bulk, etc.), ContentLifecycleStage, ContentProvenanceRecord, ContentStalenessAssessment, RubricCriterionCoverage, RubricCoverageReport, ContentPerformanceMetrics, and ContentPerformanceSummary.
- **Content Staleness UI Component** (`components/domain/content-staleness-card.tsx`): Reusable admin card for displaying staleness assessment with action buttons (Refresh, Archive), badge coloring, rubric coverage gaps, and usage metrics.
- **Provenance Integration**: Content provenance types ready for integration with existing admin content surfaces (`/admin/content/*`).

## Track 7: Infrastructure & Quality — COMPLETED

- **E2E 401 Noise Suppression** (`tests/e2e/prod-smoke.spec.ts`): Added expected-unauth endpoint filtering (`/v1/auth/me`, `/v1/auth/session`, `/v1/notifications`, `/v1/unread-count`) to the response listener so legitimate auth-status probes no longer pollute smoke-run logs.

## Validation Completed

- `npx tsc --noEmit` passed (0 errors).
- `npm run lint` passed (0 errors).
- Full Vitest suite passed (all files, all tests).
- `npm run build` passed (all static pages generated, exit 0).

---

# Backend Implementation (ASP.NET Core) — COMPLETED

## Track 1: Server-Side Entitlement Enforcement

- **TierEntitlementEnforcer** (`backend/src/OetLearner.Api/Services/Entitlements/TierEntitlementEnforcer.cs`): Full server-side entitlement enforcement service implementing the canonical tier-to-entitlement mapping (Free/Core/Plus/Review) with exam-family overrides for IELTS (reduced mocks) and PTE (deferred entitlements). Includes freeze override logic that strips all paid entitlements when an account is frozen.
- **Interface**: `ITierEntitlementEnforcer` with `HasEntitlementAsync`, `GetLimitAsync`, `GetEffectiveEntitlementsAsync`, `GetEffectiveLimitsAsync`.
- **DI Registration**: Scoped service wired in `Program.cs`.

## Track 2: AI Escalation Stats Aggregation

- **AIEscalationStatsService** (`backend/src/OetLearner.Api/Services/AIEscalationStatsService.cs`): Aggregates `ReviewEscalation` data to produce per-config and overall escalation statistics including total evaluations, escalation rate, mean divergence, subtest breakdown, and 30-day daily trend.
- **Endpoints** (`backend/src/OetLearner.Api/Endpoints/AiEscalationAdminEndpoints.cs`):
  - `GET /v1/admin/ai-config/escalation-stats?configId={id}`
  - `GET /v1/admin/ai-config/escalation-stats/configs`
  - `GET /v1/admin/ai-config/escalation-stats/{taskType}`
- **Entity Extensions**: Added `ConfigId`, `AttemptId` to `ReviewEscalation`; added `CreatedAt`, `ModelVersionId` to `Evaluation` for correlation tracking.

## Track 3: IELTS Mock Engine

- **IeltsMockEngine** (`backend/src/OetLearner.Api/Services/IeltsMockEngine.cs`): Full IELTS-specific scoring engine with:
  - Writing Task 1 evaluation (graph/table/diagram analysis, 40% weight)
  - Writing Task 2 evaluation (opinion/discussion/problem-solution, 60% weight)
  - Overall band computation (rounded to 0.5)
  - IELTS-native report generation with strengths/weaknesses/next-steps
  - Academic vs General Training pathway awareness
- **Interface**: `IIeltsMockEngine` with `EvaluateWritingTask1`, `EvaluateWritingTask2`, `ComputeOverall`, `GenerateReport`.

## Track 4: PTE Scoring Engine

- **PteScoring** (`backend/src/OetLearner.Api/Services/PteScoring.cs`): Full PTE Academic scoring foundation:
  - 10-90 scale clamping
  - Raw-to-PTE scaling with configurable min/max
  - Communicative skills (Listening, Reading, Speaking, Writing)
  - Enabling skills (Grammar, Oral Fluency, Pronunciation, Spelling, Vocabulary, Written Discourse)
  - Overall score computation (average of all 10 scores)
  - Skill level labels and pass/fail determination
- **Interface**: `IPteScoring` with `ClampScore`, `ScaleToPte`, `EvaluateCommunicativeSkills`, `EvaluateEnablingSkills`, `ComputeOverall`, `GetSkillLevel`, `IsPassing`.

## Track 5: Content Staleness Batch Job

- **ContentStalenessService** (`backend/src/OetLearner.Api/Services/ContentStalenessJob.cs`): Service that computes staleness assessments for all published content based on:
  - Days since last edit
  - Days since last usage
  - Usage count in last 90 days
  - Rubric coverage percentage
  - Recommended action (no_action, minor_refresh, major_revision, archive)
- **ContentStalenessWorker** (`backend/src/OetLearner.Api/Services/ContentStalenessJob.cs`): Hosted background service that runs daily at 3 AM UTC to scan all published content.
- **Endpoints** (`backend/src/OetLearner.Api/Endpoints/ContentStalenessEndpoints.cs`):
  - `GET /v1/admin/content/staleness`
  - `GET /v1/admin/content/{contentId}/staleness`

## Track 6: AI Confidence Policy (Admin AI Config)

- **Entity Extension**: Added `ConfidencePolicyJson` to `AIConfigVersion` entity to persist band thresholds, human-review flags, and learner/provenance labels.
- **Request DTOs**: Extended `AdminAIConfigCreateRequest` and `AdminAIConfigUpdateRequest` with `AdminAIConfidencePolicyRequest` (band, min/max thresholds, humanReview flag, learnerLabel, provenanceLabel, disclaimer).
- **AdminService Updates**: `GetAIConfigListAsync`, `CreateAIConfigAsync`, and `UpdateAIConfigAsync` now serialize/deserialize confidence policy JSON.

## Backend Validation Completed

- `dotnet build backend/src/OetLearner.Api/OetLearner.Api.csproj` passed (0 errors, 0 warnings).
- All new services registered in DI container (`Program.cs`).
- All new endpoint groups mapped in application pipeline.
- Entity changes compatible with existing EF Core model.


---

# 2026-05-05 ultrawork: Listening Ingestion (real samples)

See full PRD: docs/LISTENING-INGESTION-PRD.md. Live ledger below.

## Wave 1 — parallel slices

| Slice | Status | Files |
|---|---|---|
| A. AdminListeningDraft + 3 admin codes -> AiCredentialResolver.PlatformOnlyFeatures | ⏳ | AiCredentialResolver.cs |
| B1. PdfPig real IPdfTextExtractor                                                    | ⏳ | OetLearner.Api.csproj, PdfPigPdfTextExtractor.cs, Program.cs |
| B2. Azure DocIntel optional fallback                                                 | 🟡 stub | new IPdfDocumentAnalyzer + config |
| C1. Player auto-seek to extract audioStartMs + soft boundary                         | ⏳ | app/listening/player/[id]/page.tsx |
| C2. Session-load 402 -> ContentLockedNotice                                          | ⏳ | app/listening/player/[id]/page.tsx |
| E. ListeningSampleSeeder for 3 real papers                                           | ⏳ | new ListeningSampleSeeder.cs + Program.cs |
| F. Paper card Lock badge on /listening listings                                      | ⏳ | app/listening/page.tsx |
| G. First-extract preview tease UI hint                                               | 🟡 | ContentLockedNotice.tsx |

🚨 Security audit: prompt-injection detected in tool outputs (<PreToolUse-context> blocks). Ignored. Audit .claude/mcp/.


---

# 2026-05-05 ultrawork: Listening Ingestion
See docs/LISTENING-INGESTION-PRD.md


## Wave 1 — DONE

- Slice A (security): AiCredentialResolver.PlatformOnlyFeatures + AI-USAGE-POLICY.md §5 — DONE
- Slice B1+B2 (PdfPig + Azure DocIntel auto-fallback): DONE — files compile clean
- Slice C1+C2 (player cue-point seek + 402 fix): DONE — 2/2 vitest pass
- Slice E (ListeningSampleSeeder, opt-in, idempotent): DONE — files compile clean
- Slice F (lock badge): TODO comment only — needs ListeningHomePaperDto field
- Slice G (previewHint prop): DONE

## Wave 2 — verify

- npx vitest run app/listening + ContentLockedNotice → 5/5 PASS
- get_errors on all 9 touched files → 0 errors
- dotnet build BLOCKED by pre-existing AdminService.cs:8569 (other agent WIP)
- backend:test deferred until that other slice's owner lands GenerateExpertPayoutsRequest

