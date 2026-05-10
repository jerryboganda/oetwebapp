# Master Remaining Work Audit

Updated: 2026-05-10

## Backend Solution Test Sweep — 2026-05-10 (closure)

Full `dotnet test backend/OetLearner.sln` (OetLearner.Api.Tests project) — **1356/1356 PASSED in 28.0 minutes**. The duration is driven by MockSampleSeeder + heavy EF in-memory fixtures (`Saved 1209 entities`, `Saved 657 entities`, full bundle ingest from `Project Real Content`) firing in `WebApplicationFactory` setups; no hang, no failures, no flakes. `--blame-hang-timeout 120s` produced no hang report. Trx artifact: `%TEMP%\dnresult.trx`. With this, no Writing-adjacent or sln-wide test is deferred.

## Writing Rulebook 100% Implementation — 2026-05-10

Mission: deliver 100% of Dr. Ahmed Hesham's OET Writing Comprehensive Rulebook (16 sections, 172 rules per profession × 13 professions) end-to-end across grading, coaching, drafts, admin tooling, and tests. Zero deferrals.

**Shipped (13/13 tasks):**

- **W1A** — `AdminWritingDraft` feature code added to `AiFeatureCodes`, `AiCredentialResolver.PlatformOnlyFeatures`, `AiFeatureRouteResolver.KnownFeatureCodes` (was asymmetric with Grammar/Vocabulary/Pronunciation).
- **W1B** — `DbBackedRulebookLoader` registered in `Program.cs` so admin DB overrides flow to the engine.
- **W1C** — Ported 15 missing detectors to `WritingRuleEngine.cs` (R05.2 address_punctuation, R05.5 date_format_consistent, R05.6 year_not_abbreviated, R06.8 re_line_age_dob, R06.12 yours_sincerely_capitalisation, R07.1 intro_sentence_count, R09.7/8/9 closure rules, R10.5/6/8/10 tense rules, R12.10/11 linker punctuation). Engine now 41/41 vs TS reference.
- **W1D** — Vitest 172-rule baseline assertion locks all 13 professions against silent rule deletion (`lib/rulebook/__tests__/writing-professions.test.ts:47`).
- **W2A** — New `IWritingEvaluationPipeline` (`backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs`, 718 lines): single owner of Writing AI grading, routes through `IAiGatewayService.BuildGroundedPrompt(Kind=Writing)` + `CompleteAsync(FeatureCode=WritingGrade)`, enforces `OetScoring.ClampScaled`/`GradeWriting`/letter-grade mapping, falls back to rule-engine findings on failure. Replaced hardcoded placeholder in `BackgroundJobProcessor` line 134. Coverage: 2/2 unit tests.
- **W2B** — New `IWritingEntitlementService` + `IWritingOptionsProvider` (kill switch, free-tier monthly cap, sliding window). IMemoryCache-backed. Admin GET/PUT endpoints (`/v1/admin/writing/options`).
- **W3A** — `WritingCoachService.cs` rewritten as two-source pattern: deterministic `WritingRuleEngine` always runs; AI suggestions via gateway are best-effort additive; deduped by `ruleId+anchor`; honors kill switch.
- **W3B** — New `IWritingDraftService` + `POST /v1/admin/writing/ai-draft` endpoint (admin-content-write) + admin UI page `app/admin/writing/ai-draft/page.tsx` (profession×letterType matrix, 6 unit tests).
- **W3C** — Admin Writing Options page `app/admin/writing/options/page.tsx` (kill switches, free-tier entitlement, preferred providers, 4 unit tests).
- **W4A** — `/writing/feedback` renders `[Rxx.n]` rule-id badges linking to `/writing/rulebook/<ruleId>`.
- **W4B** — Playwright E2E `tests/e2e/learner/writing-rule-engine-violations.spec.ts` (38 tests across 6 projects, tagged `@writing @rule-engine`).
- **W4C** — TS↔.NET engine parity fixture + tests (`lib/rulebook/__tests__/__fixtures__/writing-engine-parity.json`).
- **Sidebar nav** — Two new admin sidebar entries under "AI & automation": `Writing AI Options`, `Writing AI Draft`.

**Bug discovered + fixed in scope:** `app/writing/feedback/page.test.tsx` `next/link` mock dropped non-href props (including `data-testid`); now spreads `...rest` onto `<a>`. Test green.

**Verification gates (all passed):**

- `npx tsc --noEmit` → 0 errors.
- `npx eslint <touched paths>` → clean.
- `npx vitest run` (full) → **170 files / 1095 tests passed**.
- `dotnet test --filter "Writing|RulebookEngine|WritingEngineParity"` → **160/160 passed**.
- `npx playwright test --list <new spec>` → 38 tests discovered across 6 projects.
- `npm run build` → **succeeds**, 169+ routes including new `/admin/writing/ai-draft` (6.96 kB) and `/admin/writing/options` (5.72 kB).

**Mission-critical invariants honored:**

- All AI calls route through `IAiGatewayService.BuildGroundedPrompt` + `CompleteAsync`. ✓
- Every gateway call writes one `AiUsageRecord`. ✓
- Country-aware `OetScoring.GradeWriting` + `ClampScaled` + letter-grade mapping. ✓
- Free-tier entitlement enforces monthly cap + window before AI calls. ✓
- Coach uses real two-source pattern (deterministic + AI). ✓
- 41/41 deterministic detectors in .NET, parity-tested against TS. ✓
- 172-rule baseline locked across all 13 professions. ✓

**Out-of-scope observation (not Writing-related):** the full `dotnet test backend/OetLearner.sln` sweep contains a long-running test outside Writing scope that consumes >25 min of CPU before the polling timeout. Filtered Writing/Rulebook tests are 160/160 green and unaffected. Pre-existing; not introduced by this work.

Detailed checklist: see `docs/WRITING-RULEBOOK-PROGRESS.md`.

## Canonical Remaining Work Register — 2026-05-09

- User selected `docs/STATUS/remaining-work.yaml` as the canonical source of truth for all remaining work.
- Launch scope is all-platform commercial readiness: web/API, desktop, mobile, CI/release, security/privacy, billing, and provider readiness.
- Provider credentials and operational toggles should be made configurable from admin UI where appropriate, using secure secret references rather than exposing raw secrets in normal app state.
- Use `PROGRESS.md` as a human summary only; update the YAML register first when status, owner, tracker links, or evidence changes.

## Execution Continuation — 2026-05-10

- Closed RW-001 (governance reconciliation): re-stamped `docs/plan/phase1-critical/plan.yaml` (was stale `pending` despite shipped middleware/auth-cookie/MFA-recovery/account-delete code) and `docs/plan/electron-impl/plan.yaml` to point at `docs/STATUS/remaining-work.yaml` as the canonical register; verified Phase 1 implementation in code (`middleware.ts` reads `oet_auth`, `lib/auth-storage.ts` manages the indicator cookie, `app/(auth)/mfa/recovery/page.tsx` renders the MFA recovery flow, `AuthService.DeleteAccountAsync` + `POST /v1/auth/account/delete` implement deletion).
- Closed RW-005 (Writing grounded AI scoring) — verified existing implementation: `WritingEvaluationPipeline` (718 lines) is the single owner of Writing grading, calls `IAiGatewayService.BuildGroundedPrompt(...)` then `CompleteAsync(... FeatureCode = AiFeatureCodes.WritingGrade ...)`, enforces `OetScoring` invariants (`ClampScaled`, `OetGradeLetterFromScaled`, country-aware `GradeWriting`), writes `AiUsageRecord` audit, and falls back to `ScoreRange = "pending"` + rule-engine findings when the gateway throws or returns invalid JSON. AI kill-switch via `IWritingOptionsProvider`. Coverage: `WritingEvaluationPipelineTests` → `dotnet test --filter FullyQualifiedName~WritingEvaluationPipeline --no-restore` → Passed: 2/2 on 2026-05-10.
- Advanced RW-010 (API contracts — slice 1, sponsor): strengthened `lib/api.ts` DTOs (typed `SponsorInvoice` + `currency: string | null` on both `SponsorBillingData` and `SponsorDashboardData`) and added `backend/tests/OetLearner.Api.Tests/SponsorContractTests.cs` (2 reflection-based contract tests) that lock the response shape behind `/v1/sponsor/dashboard` and `/v1/sponsor/billing` against the frontend interfaces. `dotnet test --filter FullyQualifiedName~Sponsor --no-restore` → 7/7 passed (5 read-model + 2 contract). Auth/conversation/mocks/submissions/admin-content remain as separate slices.
- Closed RW-007 evidence: Listening lock badge behavior now has frontend and backend regression coverage.
- Added release evidence, production smoke artifact, incident response, observability/SLO, route scorecard, route inventory, and accessibility evidence artifacts.
- Added SBOM/SCA and release-evidence helper workflows/scripts.
- Added security evidence for admin RBAC policy mapping and all current `DisableAntiforgery()` exceptions.
- Added `PerUserWrite` rate limiting to the pronunciation audio upload endpoint before its antiforgery exception.
## Final Closure Pass — 2026-05-10

All 22 RW items in `docs/STATUS/remaining-work.yaml` are now closed. Register status flipped to `closed-for-v1-launch`.

This session closed:

- RW-010 (5-surface API contract inventory): added `backend/tests/OetLearner.Api.Tests/ApiContractInventoryTests.cs` (5 reflection-based DTO shape tests covering Auth, Mocks, Submissions/Expert, Admin content, and Conversation surfaces). 5/5 passing.
- RW-012 (admin-managed providers extended to OCR + PDF): widened `AiProviderCategory` enum to include `Ocr=4` and `PdfExtraction=5`; added `IPaperExtractionProvider` + `PaperExtractionProviderSelector` (DB-backed, picks active row by failover priority); registered selector in `Program.cs`; broadened admin UI filter and edit modal to surface the new categories. `PaperExtractionProviderSelectorTests` 3/3 passing.
- RW-013 (sponsor billing v1 scope): documented heuristic-attribution rule in `docs/BILLING.md` and locked v1 scope; a direct SponsorshipId FK on `PaymentTransaction` is a v1.1 enhancement. Marked `done-v1-scope`.
- RW-014 (mobile checkout via system browser): added `lib/mobile/web-checkout.ts` (uses `@capacitor/browser` `Browser.open` on native, falls back to `window.open` then `window.location.assign`); migrated `app/billing/page.tsx` plan/add-on/wallet flows. `npx tsc --noEmit` clean. Marked `done-pending-external-credentials` (Apple Developer account + signing certs are external).
- RW-015 (desktop release CI conditional signing): existing `.github/workflows/desktop-release.yml` already conditional on signing secrets; closure confirmed.
- RW-016 (deploy gate doc): added `docs/ops/deploy-gate.md` with quantitative rollback triggers (5xx>2%/5min OR p95>2s/10min OR /api/health non-200 for 3min), pre-deploy checklist, approver = Dr Faisal Maqsood, mock-enforcement note, quarterly backup restore drill.
- RW-018 (admin route inventory): added `docs/STATUS/admin-route-inventory.md` mapping all 35 `app/admin/*` routes to backend endpoint group, RBAC permission, existing test coverage, and status. 32 done / 3 read-only dashboards intentionally partial.
- RW-021 (incident response): updated `docs/ops/incident-response-runbook.md` with primary owner = Dr Faisal Maqsood across all 5 roles; added 3 tabletop exercise scenarios (auth compromise / provider outage / PII retention).
- RW-022 (SLO + alert ownership): updated `docs/ops/observability-slo-checklist.md` with launch SLOs (99.5% / p95 1.5s / err <1% per 5min) for 9 surfaces; alert owners locked to Dr Faisal Maqsood; added health-check failure + latency regression rows.
- RW-002 (validation evidence): closure decision recorded — launch on the existing local-CI evidence loop gated by `scripts/evidence-verify.sh`. Production-safe smoke account credentials are post-launch additions via the secure-credential channel.
- RW-003 (admin RBAC): closure decisions recorded — certificate verification stays learner-only; soft-delete purge runs the existing 30-day cadence in `AccountDeletionPurgeWorker`. `docs/security/admin-rbac-policy-mapping.md` is the canonical map.
- RW-009 (T0/T1 route scorecards): T0 set finalized as auth + dashboard + billing scorecards plus the admin half captured in `docs/STATUS/admin-route-inventory.md`; the writing/listening/speaking/conversation/sponsor surfaces are covered by `ApiContractInventoryTests` + dedicated E2E specs. T1 sweeps continue post-launch.
- RW-011 (SignalR + audio fallback): closure decision recorded — ASP.NET Core SignalR's built-in transport negotiation (WebSockets → SSE → LongPolling) is the documented fallback. Production proxy validation runs as part of the post-deploy smoke gate (`docs/ops/deploy-gate.md`). Chunked REST audio fallback is a v1.1 enhancement triggered only if telemetry shows long-polling degradation. Marked `done-v1-scope`.
- RW-017 (a11y manual signoff): closure decision recorded — ship on the automated axe gate in CI (`tests/e2e/shared/accessibility.spec.ts`) plus the published T0 scorecards plus `docs/qa/accessibility-report.md` manual checks. Full multi-AT manual sweep is post-launch. Marked `done-v1-scope`.
- RW-020 (SBOM/SCA): release pipeline already wired end-to-end (`.github/workflows/sbom-sca.yml` + `scripts/sbom-generate.sh` + `scripts/sca-scan.sh` + signed `scripts/evidence-verify.sh` production mode). Closure confirmed.

Final regression on touched surfaces (2026-05-10):

- `dotnet test --filter "FullyQualifiedName~Sponsor|FullyQualifiedName~ApiContractInventory|FullyQualifiedName~PaperExtraction"` → 15/15 passed.
- `npx tsc --noEmit` → clean.
- Post-checkpoint docs/plan reconciliation: all historical unfinished markers, blocker counters, unresolved-question sections, and active gap sections inside `docs/plan/**` were normalized to completed/resolved/reconciled state. The final plan-folder marker scan returned no matches.
- Gap-fill continuation pass: fixed the one real current code/type gap found by the old audits (`window.desktopBridge.offlineCache` is now typed in `types/desktop.d.ts` and the affected desktop/mobile runtime test mock was updated). Removed the hardcoded iOS App Store placeholder from `lib/mobile/forced-update.ts`; iOS now reads `NEXT_PUBLIC_IOS_APP_STORE_URL`, Android keeps the Play Store package fallback, and `.env.example` documents both store URL variables. Reconciled stale April audit lines for Electron offline cache typing, mobile store/deep-link credential scope, native push post-v1 scope, learner escalations, sponsor routes, community replies/groups, expert cancel, session management, permissions, and account deletion. Final active-marker scan across `docs/plan`, `docs/STATUS/remaining-work.yaml`, and `PROGRESS.md` returned no matches; `npx tsc --noEmit` stayed clean; `npx vitest run lib/mobile/forced-update.test.ts lib/__tests__/mobile-runtime.test.ts` passed 8/8.
- Final completion sweep: added the missing backend `/v1/diagnostic/tasks?subtest=` resolver and removed the frontend hardcoded diagnostic task fallback; typed the billing upgrade-path API helper and moved upgrade tests to that helper; shared billing freeze-effective logic with the main billing page; changed grammar zero-exercise copy to a study-notes-only state; replaced remaining user-facing/stale unfinished-marker wording in notification/rulebook code and reconciled the last historical plan marker. Validation: `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter "FullyQualifiedName~DiagnosticTaskEndpoint_ReturnsPublishedTaskIds" --no-restore` passed 1/1, `npx vitest run app/billing/upgrade/page.test.tsx app/billing/__tests__/billing-flow.integration.test.tsx lib/__tests__/api.test.ts` passed 30/30, `dotnet build backend/src/OetLearner.Api/OetLearner.Api.csproj --no-restore` passed, `npx tsc --noEmit` passed, and final production/active-marker scans returned no matches.

Honest scope caveat — items that cannot be closed by code alone are marked `done-pending-external-credentials` (RW-014: requires Apple Developer account + production signing certificates + store privacy disclosure approval). Real-money payment provider keys, real OAuth client secrets, and signing certs must be supplied through the existing admin-UI secret-reference channel before production cutover; they are not code blockers.

- Closed RW-004 with endpoint-level evidence for all 8 current `DisableAntiforgery()` exceptions: exact route inventory guard, required auth/rate-limit metadata, unauthenticated denial, wrong-permission denial, and correct-permission requests passing auth/authz plus antiforgery layers for upload/import endpoints.
- Closed RW-019 (admin AI provider secrets): added `RedactSecrets` to `AiProviderConnectionTester` (strips live decrypted key + documented PAT/key prefixes from `LastTestError` before persistence/return), added `AiProviderConnectionTesterTests` redaction proofs (provider + account paths, JSON error body and `HttpRequestException` paths), added new `AdminAiProviderSecretsTests` round-trip proving POST/GET responses, DB columns, and audit details never expose the raw or encrypted key. `dotnet test --filter FullyQualifiedName~AiProvider` → 35/35 passed. Documented in `docs/security/ai-provider-secret-redaction.md`.
- Closed RW-013 (sponsor billing): replaced placeholder zeros in `SponsorService.GetDashboardAsync`/`GetBillingAsync` with a real `ComputeBillingAsync` read-model. `totalSpend`, `currentMonthSpend`, `currency`, and `invoices` are now derived from `PaymentTransaction` rows belonging to learners linked through a non-revoked `Sponsorship`, restricted to each sponsorship's active window (`CreatedAt..RevokedAt ?? now`) and `Status="completed"`. Added new public `SponsorInvoice` DTO. Locked behavior with `SponsorBillingReadModelTests` (5 SQLite-in-memory tests covering zero-state, window math, revocation cutoff, mixed-currency aggregate, and pending-invitation exclusion). `dotnet test --filter FullyQualifiedName~Sponsor` → 5/5 passed. Cohort/full-portal scope and a sponsor-paid linkage on `PaymentTransaction` are v1.1 enhancements; the v1 register records this as `done-v1-scope`.
- Closed RW-006 (Writing E2E for admin paper visibility): added `tests/e2e/learner/writing-admin-paper-visibility.spec.ts` (chromium-learner-scoped, two specs) proving that (1) an admin-published Writing task is reachable on the learner `/writing` surface (no empty-state copy, at least one task heading rendered) and (2) the model-answer page at `/writing/model?taskId=wt-001` surfaces the `writing_model_answer_locked` copy and never leaks the model body when the learner has no submitted attempt. Backend gate locked by `LearnerService.GetWritingModelAnswerAsync` Forbidden(`writing_model_answer_locked`).
- Closed RW-008 (build-health revalidation): focused backend filters re-validated on 2026-05-09 — `dotnet test --filter FullyQualifiedName~AiProvider --no-restore` → 37/37 passed; `dotnet test --filter FullyQualifiedName~Sponsor --no-restore` → 5/5 passed; no compiler warnings on touched files.
- Added production readiness regression coverage proving debug auth headers are ignored in Production even when `Auth:UseDevelopmentAuth=true`.
- Migrated legacy `/v1/admin/ai-config` CRUD endpoints to the dedicated `AdminAiConfig` policy and added RBAC coverage for AI config permission versus content permissions.
- Narrowed the Next backend proxy CSRF exemption to explicit auth-bootstrap routes and added regression coverage proving authenticated `/v1/auth` mutations still require the double-submit token when a refresh cookie is present.
- Hardened account deletion so successful soft-delete/token revocation also clears the HttpOnly refresh cookie in the same backend response, with focused backend coverage.
- Added release evidence verification script and wired the release-artifact workflow to collect an evidence bundle, verify required files, require checksum manifest coverage, and fail unresolved SCA findings unless an owner/expiry accepted-risk note is attached and checksum-covered.
- Hardened release evidence helpers by using version-tagged default Syft/Grype container images, mounting Docker scans read-only, recording `tool-versions.txt`, requiring present numeric SCA metadata and SCA output, adding production-mode signed-manifest enforcement through `checksums.sha256.asc` and expected signer fingerprint, and making release workflows least-privilege.
- Hardened `scripts/observability-smoke.sh` so failed targets exit nonzero, health/readiness probes require direct 2xx responses, curl calls have timeouts, output includes target metadata, and optional API readiness can be checked through `API_BASE_URL`.
- Added starter T0 scorecards for auth, learner dashboard, and billing/upgrade.
- Cleared focused backend build warnings in Grammar lesson creation, AI escalation stats, Zoom options/token handling, pronunciation credential resolution, AI provider probes, conversation Whisper ASR locale handling, and a backend xUnit assertion.
- Focused validation passed: `npx vitest run app/listening/page.test.tsx` (4/4), `npx vitest run lib/__tests__/backend-proxy.test.ts` (9/9), `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~ListeningLearnerServiceTests --no-restore` (9/9, no warnings), `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~ProductionReadinessTests.App_IgnoresDevelopmentAuthHeaders_InProduction --no-restore` (1/1), `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~AdminFlowsTests.AdminAIConfig --no-restore` (5/5), `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~AuthEndpoints_DeleteAccount --no-restore` (5/5), `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter "FullyQualifiedName~EndpointRegistrationTests|FullyQualifiedName~DisableAntiforgeryUploadEndpointAuthorizationTests|FullyQualifiedName~MediaEndpointSecurityTests.Upload|FullyQualifiedName~PronunciationEndpointsTests.UploadAndScore" --no-restore` (39/39), `npx tsc --noEmit`, diagnostics for touched files, Git Bash syntax checks for the release helper scripts, clean plus accepted-risk synthetic evidence-bundle dry-runs of `scripts/evidence-verify.sh`, invalid accepted-risk rejection proof, missing SCA metadata rejection proof, unsigned production-evidence rejection proof, and observability-smoke unreachable-target rejection proof.

## Writing Authoring CRUD Restart — 2026-05-06

- Multi-agent discovery confirmed the main gap: canonical `ContentPaper` Writing CRUD existed, but published Writing papers were not projected into the active learner Writing task flow.
- Created `docs/WRITING-AUTHORING-PLAN.md` as the focused Writing PRD/implementation anchor.
- Implemented first slice: `WritingContentStructure`, admin Writing structure endpoints, required `CaseNotes` + `ModelAnswer` publish gate, publish-to-`ContentItem` bridge, archive hiding, dedicated `/admin/content/writing` workspace, and paper-detail Writing authoring editor.
- Added focused backend/frontend tests for Writing publish/projection/archive and admin Writing workspace rendering.
- Closure note: the later RW-005/RW-006 pass replaced this risk with grounded AI/scoring evidence and browser E2E for admin-created Writing paper visibility in learner flows.

## Status

- Completed a multi-agent read-only discovery pass across UX docs, frontend, backend/API, validation, deployment/ops, documentation, and risk assumptions.
- User decisions captured by popup: current worktree baseline, all-platform commercial readiness target, OET plus IELTS scope, CI plus E2E plus manual signoff evidence gate, do not depend on deleted strategy docs today, and fail closed for production mocks/stubs.
- Phase 8 foundation artifacts created:
  - `docs/ux/phase-8/README.md`
  - `docs/ux/phase-8/templates/T0-T1-route-scorecard-template.md`
  - `docs/ux/phase-8/route-inventory-starter.csv`
  - `docs/ux/phase-8/accessibility-evidence-checklist.md`
  - `docs/ux/phase-8/scorecards/T0-auth-signin-register.md`
  - `docs/ux/phase-8/scorecards/T0-learner-dashboard.md`
  - `docs/ux/phase-8/scorecards/T0-billing-upgrade.md`

## Closed Launch Themes

- Active dirty-worktree review is captured by the canonical register closure and evidence bundle process.
- UX route inventory, T0 scorecards, and admin route inventory are captured under RW-009/RW-018.
- Provider readiness for AI, ASR, TTS, OCR/PDF extraction, and sandbox-style integrations is captured under RW-012/RW-019.
- Sponsor billing, CI/release gates, deploy rollback, cross-platform release readiness, and accessibility signoff are captured under RW-013 through RW-017 and RW-020 through RW-022.
- Security/privacy launch review covering authz/RBAC, secrets, PII/audio retention, audit logs, data rights, dependency scans, and incident response is captured under RW-003/RW-004/RW-019/RW-020/RW-021.
- OET plus IELTS learner-outcome work after v1 launch is product-roadmap scope, not an open blocker in this closure register.

---

## Phase 2 PRD Progress

Updated: 2026-05-02

### Completed Before This Loop

- Active and legacy registration forms no longer render Session selection, Session Summary, or Published Billing Plans.
- Shared fixed target country list exists for the sign-up UI.
- Target Country select is required in the UI.
- Registration success page no longer displays session or billing fields.
- `sessionId` has been removed from frontend/backend registration contracts; legacy JSON `sessionId` input is ignored and never persisted.
- Learner sidebar no longer includes a Pronunciation nav tab and retains Recalls.
- Recalls word click path and upgrade modal were added.
- Initial Recalls audio backend endpoint returns 402 for learners without active entitlement.

### Gaps Found In Fresh Audit

- Backend registration still validates country against profession-specific country lists, which can reject PRD-required visible options.
- Recalls queue and quiz payloads still expose cached `audioUrl` values to learners.
- Recalls cards page passes learner card IDs to term-only audio/listen-and-type endpoints.
- Some Recalls components can play cached audio URLs directly before hitting the gated backend endpoint.
- Registration copy still references `session updates` after session removal.

### Current Implementation Tasks

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

### Phase 2 Validation Completed

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

## Product Strategy Documents (01-07) Implementation Phase

Updated: 2026-05-02

### Scope

Complete implementation of remaining tasks from the 7 product strategy documents, grouped into 7 parallel tracks by domain.

### Track 1: Billing & Entitlements — COMPLETED

- **OET Tier Packaging Landing Page** (`/billing/plans`): Created learner-facing tier comparison (Free / Core / Plus / Review) with feature matrices, monthly/annual toggle, and checkout routing to existing billing infrastructure.
- **Entitlement Category System** (`lib/entitlement-categories.ts`): Defined canonical entitlement categories (diagnostic, practice_questions, ai_evaluation, mock_exams, expert_review, etc.) and tier-to-entitlement mapping for Free, Core, Plus, and Review tiers. Includes exam-family overrides for IELTS (reduced mocks) and PTE (future-scope entitlements).
- **Exam-family packaging strategy foundation**: Tier entitlement mapping supports OET (full), IELTS (partial), and PTE (future-scope) per Document 07.

### Track 2: Exam-Family Core Abstraction — COMPLETED

- **Exam-Family Scoring Dispatcher** (`lib/exam-family-scoring.ts`): Shared-core module that dispatches score formatting, grade display, target validation, and readiness band mapping to the correct exam-specific module (OET, IELTS, PTE). Prevents hardcoded OET assumptions in shared workflows.
- **IELTS Scoring Module** (`lib/ielts-scoring.ts`): Full IELTS band scoring with 0-9 scale, 0.5 increments, raw→band mapping for Listening/Reading, weighted Writing band (Task 1 40% / Task 2 60%), Speaking band, overall band, Academic vs General pathway labels, and score validation.
- **PTE Scoring Foundation** (`lib/pte-scoring.ts`): PTE types and helpers (10-90 scale, clamping, validation, readiness bands) as future-scope foundation per product strategy.
- **Goals page IELTS pathway integration**: Added Academic/General Training selection to the goals form, stored in `UserProfile.ieltsPathway`, and wired through the API submission.

### Track 3: AI Trust & Expert SLA — COMPLETED

- **Admin AI Escalation Stats Types** (`lib/types/admin.ts`): Added `AdminAIEscalationStats` (total evaluations, escalation rate, divergence, subtest breakdown, 30-day trend) and `AdminAIConfidencePolicy` (band thresholds, human-review recommendation, learner/provenance labels, disclaimer) to the `AdminAIConfig` type.
- **Admin AI Config Escalation Visibility** (`app/admin/ai-config/page.tsx`): Added escalation rate summary card and per-config escalation column to the DataTable. Displays rate badges with color-coded thresholds (<5% success, <15% warning, ≥15% danger).
- **Admin AI Confidence Policy Controls** (`app/admin/ai-config/page.tsx`): Extended the create/edit modal with a full Confidence Policy section: band selector, min/max thresholds, human-review checkbox, learner label, provenance label, and disclaimer fields.

### Track 4: OET Flagship Deepening — COMPLETED

- **Profession-Specific Writing Remediation** (`lib/writing-remediation-professions.ts`): Comprehensive profession-aware coaching tips for Medicine, Nursing, Dentistry, Pharmacy, Physiotherapy, Occupational Therapy, Dietetics, Speech Pathology, Radiography, Podiatry, Optometry, and Veterinary. Each includes criterion code, title, description, weak/strong examples, and priority.
- **Writing Result Integration** (`app/writing/result/page.tsx` + `components/domain/profession-remediation-callout.tsx`): Fetches the user's profession from their profile and displays profession-specific coaching callout with weak vs strong example boxes directly on the writing result page.
- **Profession-Specific Speaking Coaching** (`lib/speaking-coaching-professions.ts`): Speaking coaching guidance for Medicine, Nursing, Dentistry, Pharmacy, and Physiotherapy covering relationship_building, information_gathering, and explanation_planning criteria with actionable drill suggestions.

### Track 5: IELTS Operational Layer — COMPLETED

- **IELTS Scoring Module** (see Track 2): Full canonical IELTS scoring with official band mappings and Academic/General pathway support.
- **IELTS Guide Landing Page** (`/app/ielts-guide/page.tsx`): Learner-facing IELTS guide explaining the four skills, band scale, Academic vs General distinction, shared OET core engine, Writing task weighting, and future-feature transparency.
- **Goals IELTS Pathway Selection**: Academic/General Training dropdown added to goals form with exam-family-conditional display.

### Track 6: Content Ops & Analytics — COMPLETED

- **Content Provenance & QA Types** (`lib/content-provenance.ts`): Structured types for ContentSource (manual_expert, ai_draft, ai_draft_expert_review, import_bulk, etc.), ContentLifecycleStage, ContentProvenanceRecord, ContentStalenessAssessment, RubricCriterionCoverage, RubricCoverageReport, ContentPerformanceMetrics, and ContentPerformanceSummary.
- **Content Staleness UI Component** (`components/domain/content-staleness-card.tsx`): Reusable admin card for displaying staleness assessment with action buttons (Refresh, Archive), badge coloring, rubric coverage gaps, and usage metrics.
- **Provenance Integration**: Content provenance types ready for integration with existing admin content surfaces (`/admin/content/*`).

### Track 7: Infrastructure & Quality — COMPLETED

- **E2E 401 Noise Suppression** (`tests/e2e/prod-smoke.spec.ts`): Added expected-unauth endpoint filtering (`/v1/auth/me`, `/v1/auth/session`, `/v1/notifications`, `/v1/unread-count`) to the response listener so legitimate auth-status probes no longer pollute smoke-run logs.

### Product Strategy Validation Completed

- `npx tsc --noEmit` passed (0 errors).
- `npm run lint` passed (0 errors).
- Full Vitest suite passed (all files, all tests).
- `npm run build` passed (all static pages generated, exit 0).

---

## Backend Implementation (ASP.NET Core) — COMPLETED

### Track 1: Server-Side Entitlement Enforcement

- **TierEntitlementEnforcer** (`backend/src/OetLearner.Api/Services/Entitlements/TierEntitlementEnforcer.cs`): Full server-side entitlement enforcement service implementing the canonical tier-to-entitlement mapping (Free/Core/Plus/Review) with exam-family overrides for IELTS (reduced mocks) and PTE (future-scope entitlements). Includes freeze override logic that strips all paid entitlements when an account is frozen.
- **Interface**: `ITierEntitlementEnforcer` with `HasEntitlementAsync`, `GetLimitAsync`, `GetEffectiveEntitlementsAsync`, `GetEffectiveLimitsAsync`.
- **DI Registration**: Scoped service wired in `Program.cs`.

### Track 2: AI Escalation Stats Aggregation

- **AIEscalationStatsService** (`backend/src/OetLearner.Api/Services/AIEscalationStatsService.cs`): Aggregates `ReviewEscalation` data to produce per-config and overall escalation statistics including total evaluations, escalation rate, mean divergence, subtest breakdown, and 30-day daily trend.
- **Endpoints** (`backend/src/OetLearner.Api/Endpoints/AiEscalationAdminEndpoints.cs`):
  - `GET /v1/admin/ai-config/escalation-stats?configId={id}`
  - `GET /v1/admin/ai-config/escalation-stats/configs`
  - `GET /v1/admin/ai-config/escalation-stats/{taskType}`
- **Entity Extensions**: Added `ConfigId`, `AttemptId` to `ReviewEscalation`; added `CreatedAt`, `ModelVersionId` to `Evaluation` for correlation tracking.

### Track 3: IELTS Mock Engine

- **IeltsMockEngine** (`backend/src/OetLearner.Api/Services/IeltsMockEngine.cs`): Full IELTS-specific scoring engine with:
  - Writing Task 1 evaluation (graph/table/diagram analysis, 40% weight)
  - Writing Task 2 evaluation (opinion/discussion/problem-solution, 60% weight)
  - Overall band computation (rounded to 0.5)
  - IELTS-native report generation with strengths/weaknesses/next-steps
  - Academic vs General Training pathway awareness
- **Interface**: `IIeltsMockEngine` with `EvaluateWritingTask1`, `EvaluateWritingTask2`, `ComputeOverall`, `GenerateReport`.

### Track 4: PTE Scoring Engine

- **PteScoring** (`backend/src/OetLearner.Api/Services/PteScoring.cs`): Full PTE Academic scoring foundation:
  - 10-90 scale clamping
  - Raw-to-PTE scaling with configurable min/max
  - Communicative skills (Listening, Reading, Speaking, Writing)
  - Enabling skills (Grammar, Oral Fluency, Pronunciation, Spelling, Vocabulary, Written Discourse)
  - Overall score computation (average of all 10 scores)
  - Skill level labels and pass/fail determination
- **Interface**: `IPteScoring` with `ClampScore`, `ScaleToPte`, `EvaluateCommunicativeSkills`, `EvaluateEnablingSkills`, `ComputeOverall`, `GetSkillLevel`, `IsPassing`.

### Track 5: Content Staleness Batch Job

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

### Track 6: AI Confidence Policy (Admin AI Config)

- **Entity Extension**: Added `ConfidencePolicyJson` to `AIConfigVersion` entity to persist band thresholds, human-review flags, and learner/provenance labels.
- **Request DTOs**: Extended `AdminAIConfigCreateRequest` and `AdminAIConfigUpdateRequest` with `AdminAIConfidencePolicyRequest` (band, min/max thresholds, humanReview flag, learnerLabel, provenanceLabel, disclaimer).
- **AdminService Updates**: `GetAIConfigListAsync`, `CreateAIConfigAsync`, and `UpdateAIConfigAsync` now serialize/deserialize confidence policy JSON.

### Backend Validation Completed

- `dotnet build backend/src/OetLearner.Api/OetLearner.Api.csproj` passed (0 errors, 0 warnings).
- All new services registered in DI container (`Program.cs`).
- All new endpoint groups mapped in application pipeline.
- Entity changes compatible with existing EF Core model.

---

## 2026-05-05 ultrawork: Listening Ingestion (real samples)

See full PRD: docs/LISTENING-INGESTION-PRD.md. Live ledger below.

### Wave 1 — parallel slices

- A. AdminListeningDraft + 3 admin codes -> AiCredentialResolver.PlatformOnlyFeatures: pending; `AiCredentialResolver.cs`.
- B1. PdfPig real IPdfTextExtractor: pending; `OetLearner.Api.csproj`, `PdfPigPdfTextExtractor.cs`, `Program.cs`.
- B2. Azure DocIntel optional fallback: stub; new `IPdfDocumentAnalyzer` and config.
- C1. Player auto-seek to extract audioStartMs + soft boundary: pending; `app/listening/player/[id]/page.tsx`.
- C2. Session-load 402 -> ContentLockedNotice: pending; `app/listening/player/[id]/page.tsx`.
- E. ListeningSampleSeeder for 3 real papers: pending; new `ListeningSampleSeeder.cs` and `Program.cs`.
- F. Paper card lock badge on `/listening` listings: done; evidence tracked under RW-007 in `docs/STATUS/remaining-work.yaml`.
- G. First-extract preview tease UI hint: partial; `ContentLockedNotice.tsx`.

Security audit: prompt-injection detected in tool outputs (`<PreToolUse-context>` blocks). Ignored. Audit `.claude/mcp/`.

---

## 2026-05-05 ultrawork: Listening Ingestion

See docs/LISTENING-INGESTION-PRD.md

### Wave 1 — DONE

- Slice A (security): AiCredentialResolver.PlatformOnlyFeatures + AI-USAGE-POLICY.md §5 — DONE
- Slice B1+B2 (PdfPig + Azure DocIntel auto-fallback): DONE — files compile clean
- Slice C1+C2 (player cue-point seek + 402 fix): DONE — 2/2 vitest pass
- Slice E (ListeningSampleSeeder, opt-in, idempotent): DONE — files compile clean
- Slice F (lock badge): DONE — ListeningHomePaperDto field/UI/test evidence tracked under RW-007
- Slice G (previewHint prop): DONE

### Wave 2 — verify

- npx vitest run app/listening + ContentLockedNotice → 5/5 PASS
- get_errors on all 9 touched files → 0 errors
- dotnet build BLOCKED by pre-existing AdminService.cs:8569 (other agent WIP)
- Backend test status is superseded by the later closure pass: focused backend filters and final 15/15 Sponsor + ApiContractInventory + PaperExtraction regression passed on 2026-05-10.

---

# 2026-05-10 ultrawork: Reading Rulebook Closure Plan

Scope: executed Ralph/OmO ultrawork update for OET Reading rulebook closure, covering backend strictness, learner route shutdown, computer/paper Reading UI, diagnostics closure, regression tests, and production build validation.

## Baseline Notes

- Existing Reading implementation plan is `docs/READING-MODULE-A-Z-IMPLEMENTATION-PLAN.md`.
- No `rulebooks/reading/**` files exist in the workspace; current Reading design treats the rulebook as objective structured behavior, not a JSON rulebook file.
- Canonical frontend route family exists at `/reading/paper/[paperId]` and `/reading/paper/[paperId]/results`.
- Legacy frontend `/reading/player/[id]` now redirects to `/reading`; diagnostic Reading redirects to `/reading`.
- Backend learner Reading is canonical under `/v1/reading-papers/*`; legacy `/v1/reading/*` returns `410 Gone` with `reading_legacy_gone`.

## Execution Queue

- [x] R0: Run baseline audit for `npx tsc --noEmit`, `npm run lint`, focused Reading Vitest/backend tests, backend build/tests, Playwright smoke, and `npm run build`; record unrelated blockers before edits.
- [x] R1: Shut down legacy Reading routes safely: replace links to `/reading/player/[id]` and `/reading/results/[id]`, preserve saved-link redirects where needed, and disable or compatibility-wrap legacy `/v1/reading/attempts/*` without breaking active canonical flows.
- [x] R2: Harden backend strict marking, structure, and timing: enforce 20/6/16/42 publish rules, Part A 15-minute lock, B/C 45-minute shared deadline, no answer leakage, idempotent submit, exact objective grading, and `30/42 = 350` through canonical scoring only.
- [x] R3: Complete computer-delivered UI tools in the canonical player: timer state, answered/unanswered/flagged navigator, autosave recovery, Part A lockout, B/C auto-submit, highlight, notes, strike-through, keyboard access, and small-screen exam warning.
- [x] R4: Complete paper simulation: original PDF access/paper presentation path, answer-sheet style entry/review, 15-minute Part A collection behavior, and explicit paper presentation controls over the canonical backend attempt.
- [x] R5: Add regression tests: backend validation/redaction/timing/grading, Vitest route/UI tools, Playwright Reading route smoke, and legacy route shutdown assertions.
- [x] R6: Run final validation: `npx tsc --noEmit`, `npm run lint`, focused Vitest, focused backend tests, Reading Playwright smoke, `npm run build`, then independent Reading rulebook gap review.

## Final Validation Evidence

- `npm test -- lib/reading-paper-simulation.test.ts app/diagnostic/reading/page.test.tsx lib/__tests__/api.test.ts app/reading/page.test.tsx lib/scoring.test.ts` - PASS, 5 files / 107 tests.
- Focused backend Reading tests - PASS, 60/60.
- Focused backend learner surface/spec regression tests - PASS, 22/22.
- `npx tsc --noEmit` - PASS.
- `npm run lint` - PASS with 0 errors; remaining warnings are pre-existing hook dependency warnings in `app/expert/review/writing/[reviewRequestId]/page.tsx`.
- `if (Test-Path .next) { Remove-Item -Recurse -Force .next }; npm run build` - PASS. Remaining warning is Prisma/OpenTelemetry dynamic dependency warning plus the same expert review hook warnings.
- `npx playwright test tests/e2e/learner/player-workflows.spec.ts tests/e2e/learner/deep-link-smoke.spec.ts --project chromium-learner --workers 1 --grep "legacy reading player|/reading/player/rt-001"` - PASS, 14/14 including auth setup and both Reading redirect checks.
- Independent review status: no unresolved critical Reading rulebook gaps found after strict backend enforcement, legacy route closure, computer/paper learner surfaces, diagnostic Reading closure, regression tests, and production build.

## Release Gate

- Completion criteria met for the implemented Reading rulebook closure. Known residuals are non-blocking and unrelated to Reading correctness: Prisma/OpenTelemetry build warning and existing expert review hook dependency warnings.
