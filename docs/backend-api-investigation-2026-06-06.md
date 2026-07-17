# Backend/API Investigation - 2026-06-06

## Executive Summary

- Confirmed issues in this pass: 10.
- Fixed issues in this pass: 8.
- Remaining issues: 2 confirmed follow-ups plus the broader existing backend test backlog.
- Highest risk fixed: tutor/expert horizontal access paths for Writing marking surfaces and Speaking review voice notes.
- Areas touched: ASP.NET Core Minimal API auth policies/endpoints, EF-backed service authorization checks, frontend API client/proxy usage, production deployment configuration, env examples, and focused xUnit/Vitest coverage.

## Issue Register

| ID | Severity | Category | File(s) | Endpoint | Symptom | Root Cause | Fix Strategy | Test Plan | Status |
| -- | -- | -- | -- | -- | -- | -- | -- | -- | -- |
| API-001 | Critical | Authorization / BOLA | `WritingMarkingEndpoints.cs`, `WritingTutorReviewService.cs` | `/v1/writing/marking-surface/**` | Any expert/tutor could access or submit marking-surface data for unrelated Writing submissions. | Endpoint group had expert auth but no assignment/object-level ownership gate. | Require expert auth and enforce active claimed/submitted `WritingTutorReviewAssignment` before context, pre-assessment, annotations, submit, and moderation actions. | `WritingWave6ServiceTests` assignment denial and claimed-tutor submit tests; focused backend test filter passed. | Fixed |
| API-002 | High | Authorization / media access | `SpeakingReviewVoiceNoteService.cs`, `SpeakingReviewVoiceNoteEndpoints.cs` | `/v1/speaking/reviews/{reviewId}/voice-notes` | Expert voice notes were scoped by media uploader but not by assigned speaking review ownership. | Service accepted review/media IDs without validating current expert assignment and review state. | Require active assignment, ready audio media, same uploader, and non-terminal review before create/list. | Added `SpeakingReviewVoiceNoteServiceTests`; focused backend test filter passed. | Fixed |
| API-003 | High | Admin RBAC | `AuthEntities.cs`, `Program.cs` | Admin notification endpoints using `AdminNotifications` | Endpoint policy referenced a permission that was not in the canonical admin permission set. | `notifications` permission constant and policy registration were missing. | Add `AdminPermissions.Notifications` and register the policy. | Endpoint registration coverage; focused backend tests passed. | Fixed |
| API-004 | High | Frontend/backend contract | `AdminBillingEndpoints.cs` | `/v1/admin/billing/analytics` | Admin billing analytics page called a route the backend did not expose. | Billing admin group lacked the analytics read endpoint. | Add read-protected analytics endpoint preserving frontend shape. | Added `AnalyticsEndpoint_ReturnsFrontendContractShape`; admin billing focused tests passed. | Fixed |
| API-005 | Medium | Frontend API integration | `lib/api.ts` | Admin audio preview endpoints | Admin audio preview helpers bypassed shared auth/error/base-url handling with raw fetches. | Blob-producing helpers did not use the typed API client path. | Add `apiBlobRequest` and move admin TTS/Qwen/voice-design preview calls through it. | `pnpm exec tsc --noEmit`, `pnpm run lint`, frontend tests. | Fixed |
| API-006 | Medium | Realtime/proxy integration | `app/conversation/[sessionId]/page.tsx` | `/v1/conversations/hub` | Browser SignalR setup could bypass the Next backend proxy/base-url behavior. | Client built hub URL from environment directly. | Route through `/api/backend/v1/conversations/hub`. | TypeScript, lint, frontend tests. | Fixed |
| API-007 | High | Deployment/config safety | `appsettings.Production.json`, `docker-compose*.yml`, `auto-deploy-ghcr.sh`, `.env*.example` | Production/staging runtime | Production-like compose defaults allowed auto-migration and deploy script could skip env validation. | Unsafe defaults and missing validator call in GHCR pull-only deploy path. | Default auto-migrate to false, validate `.env.production`, restore safe env examples, and set hotreload persistent storage root. | `bash -n` for deploy scripts; validator fails on placeholder example as expected; backend build passed. | Fixed |
| API-008 | Medium | EF/test reliability | `WritingEvaluationPipeline.cs` | Background Writing evaluation job | SQLite tests failed on `DateTimeOffset` ordering while resolving latest learner writing profile. | Query ordered `DateTimeOffset` server-side; SQLite cannot translate that expression. | Match the existing learner-goal pattern: filter in SQL, materialize, then sort client-side. | `WritingEvaluationPipelineTests` passed, 4 tests. | Fixed |
| API-009 | Medium | Frontend/backend contract | `app/practice/quick-session/page.tsx` | `/v1/learner/quick-session` | Page probes a backend route that does not exist, then falls back to local questions. | No confirmed backend quick-session service/endpoint contract. | Do not invent backend semantics; keep fallback and track route contract decision. | Static proof by route/API search. | Open |
| API-010 | Medium | Test/runtime reliability | Backend test suite | N/A | Full backend suite currently fails 14 of 772 tests and then reports a host crash warning. | Failures span billing checkout/catalog, learner surface contract, pronunciation auth, content publish gates, writing pathway/rulebook coverage/parity, and AI settings warning. | Triage each failing cluster separately; focused tests for files touched here pass. | `pnpm run backend:test` captured exact failing test names. | Open |

## Important API Contract Summary

| Method | Endpoint | Auth | Role/Tenant Rule | Status |
| -- | -- | -- | -- | -- |
| GET | `/health/live` | Public | None | Existing |
| GET | `/health/ready` | Public | Readiness dependencies | Existing |
| GET | `/v1/admin/billing/analytics` | Required | Admin + billing read permission | Added |
| GET/POST/DELETE | `/v1/writing/marking-surface/submissions/{submissionId}/**` | Required | Expert + assigned tutor review only | Hardened |
| GET/POST | `/v1/speaking/reviews/{reviewId}/voice-notes` | Required | Expert + active review assignment only | Hardened |
| GET | `/v1/cart` | Required | Current learner/user cart | Existing registered route |
| POST | `/v1/checkout/sessions` | Required | Current learner/user checkout | Existing registered route |
| GET | `/v1/subscriptions/me` | Required | Current learner/user subscription | Existing registered route |
| POST | `/v1/promo-codes/validate` | Required | Current learner/user checkout context | Existing registered route |
| SignalR | `/v1/conversations/hub` via `/api/backend/v1/conversations/hub` | Required | Current conversation user auth | Client aligned |
| GET | `/v1/learner/quick-session` | Required? | Undecided | Missing/open |

## Validation

- `dotnet build backend/src/OetWithDrHesham.Api/OetWithDrHesham.Api.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -p:RunAnalyzers=false -v:minimal`: passed with warnings only.
- `dotnet build backend/tests/OetWithDrHesham.Api.Tests/OetWithDrHesham.Api.Tests.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -p:RunAnalyzers=false -v:minimal -m:1`: passed with warnings only.
- `dotnet test backend/tests/OetWithDrHesham.Api.Tests/OetWithDrHesham.Api.Tests.csproj --no-build --filter "FullyQualifiedName~AdminBillingEndpointsTests|FullyQualifiedName~EndpointRegistrationTests|FullyQualifiedName~WritingWave6ServiceTests|FullyQualifiedName~SpeakingReviewVoiceNoteServiceTests" --logger "console;verbosity=normal"`: passed, 67 tests.
- `pnpm exec tsc --noEmit`: passed.
- `pnpm run lint`: passed with 0 errors and 364 existing warnings.
- `pnpm test`: passed, 280 files and 1,866 tests.
- `pnpm run backend:build`: passed with existing warnings only.
- `dotnet test backend/tests/OetWithDrHesham.Api.Tests/OetWithDrHesham.Api.Tests.csproj --no-build --filter "FullyQualifiedName~Writing.WritingEvaluationPipelineTests" --logger "console;verbosity=normal"`: passed, 4 tests after the SQLite-friendly profile-country fix.
- `pnpm run backend:test`: failed, 14 failed / 758 passed / 772 total, then test host crash warning.
- `Test-NetConnection -ComputerName 127.0.0.1 -Port 5432`: TCP succeeded.
- `bash -n scripts/deploy/auto-deploy-ghcr.sh` and `bash -n scripts/deploy/validate-production-env.sh`: passed.
- `bash scripts/deploy/validate-production-env.sh .env.production.example`: failed as expected because example placeholders are intentionally not deployable secrets.

## Backend Test Failures Still Requiring Triage

The full suite failures were outside the focused files changed in this pass. The failing groups were:

- Billing checkout E2E and billing catalog sync.
- Learner surface objective evaluation contract.
- Pronunciation upload role rejection.
- Content paper publish gates.
- Writing learner pathway plus rulebook coverage/parity.
- Production-provider safety warning during test host shutdown.

## Files Changed In This Pass

- Backend auth/RBAC and services: `AuthEntities.cs`, `Program.cs`, `WritingMarkingEndpoints.cs`, `WritingTutorReviewService.cs`, `SpeakingReviewVoiceNoteService.cs`, `SpeakingReviewVoiceNoteEndpoints.cs`, `AdminBillingEndpoints.cs`, `WritingEvaluationPipeline.cs`.
- Backend tests: `TestWebApplicationFactory.cs`, `EndpointRegistrationTests.cs`, `WritingWave6ServiceTests.cs`, `SpeakingReviewVoiceNoteServiceTests.cs`, `AdminBillingEndpointsTests.cs`.
- Frontend API integration: `lib/api.ts`, `app/conversation/[sessionId]/page.tsx`.
- Config/deploy examples: `.env.example`, `.env.production.example`, `.env.staging.example`, `.env.docker-local.example`, `appsettings.Production.json`, `docker-compose.production.yml`, `docker-compose.vps.yml`, `docker-compose.staging.yml`, `docker-compose.hotreload.yml`, `scripts/deploy/auto-deploy-ghcr.sh`.

## Remaining Risks

- This is not yet a 100% clean backend acceptance state because `pnpm run backend:test` still fails in broader clusters.
- Backend runtime `/health/live` and `/health/ready` were not exercised after the patches; PostgreSQL TCP readiness was confirmed, but app-level readiness still needs a clean local run with safe local env.
- `/v1/learner/quick-session` remains an open contract decision because the frontend fallback is intentional and no existing backend semantics were found.
- A deeper pass should prioritize the 23 backend test failures before any claim of full A-Z completion.
