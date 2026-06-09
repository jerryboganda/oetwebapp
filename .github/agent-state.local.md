# Agent State - AI Packages

Last updated: 2026-06-09

## Goal

Implement the AI Packages PDF as a first-class billing, credit, checkout, grading, mock, learner, and admin system using the existing OET billing/webhook/AI-gateway patterns.

## Implemented

- Added AI package credit ledger tables/entities: `AiPackageCreditAccount`, `AiPackageCreditTransaction`, and `LearnerExamOutcome`.
- Added `AiPackageCreditService` for package grants, idempotent Stripe-session fulfillment, top-up expiry max rule, pool priority, objective/mock allowance deduction, refunds, admin corrections, and pass-expiry.
- Added production migration `20260702090000_AddAiPackageCreditsAndSeedCatalog` to create ledger/outcome tables and seed all 18 GBP AI package add-ons and versions with `AddonKind = ai_package`.
- Exposed public `GET /v1/billing/ai-packages`, learner `GET /v1/me/ai-package-credits`, and admin AI package credit lookup/adjust/pass-recording endpoints.
- Wired checkout fulfillment so `ai_package` add-ons grant the new package ledger instead of legacy review credits.
- Wired Writing/Speaking grading queue creation to deduct AI package credits and pipeline failure paths to refund debited package credits.
- Wired deterministic Listening/Reading submission to consume finite package allowances while preserving no-package course behavior.
- Wired full-shape mock attempt creation to consume separate mock allowance without spending flexible/writing/speaking credits.
- Narrowed package mock debit to package-owned full/final-readiness mocks so legacy diagnostic/LRW mock entitlements keep their existing gate.
- Added pool-specific legacy bypasses for Writing/Speaking grading and mock debits when a learner has no positive package grant in that pool, preserving pre-package subscription/course behavior while still blocking exhausted package allowances.
- Added public `/ai-packages` with Full Packages, Separate Packages, Mock Packages, login redirect preservation, Stripe checkout, and learner balance display.
- Added dashboard `?purchase=success` AI package balance refresh/banner.
- Added focused backend tests in `AiPackageCreditServiceTests`.

## Validation

- `pnpm run backend:build`: passed. Existing warnings remained, including NU1510 and pre-existing nullability warnings.
- `pnpm exec tsc --noEmit`: passed.
- `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter AiPackageCreditServiceTests --nologo`: passed, 5 tests.
- `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter "FullyQualifiedName~MockV2EndpointTests" --nologo`: passed, 5 tests.
- `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter "FullyQualifiedName~SpeakingMockSetTests" --nologo`: passed, 14 tests.
- `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter "FullyQualifiedName=OetLearner.Api.Tests.ProductionReadinessTests.SpeakingUploadPipeline_StoresBinary_AndStreamsItToExperts" --nologo`: passed, 1 test.
- `pnpm exec eslint app/ai-packages/page.tsx app/page.tsx lib/api.ts lib/billing-types.ts`: passed with warnings only from the repo's `react-hooks/set-state-in-effect` rule on the new package page effects.
- `git diff --check`: passed with a line-ending warning for `SpeakingEvaluationPipeline.cs`.
- Production deploy for commit `1d44ebbcd402f9d235a0ddcc765d8730dd4408ec` succeeded via GitHub Actions/GHCR pull-only rollout; follow-up compatibility fix is being committed and redeployed.

## Touched Files

- `.github/agent-state.local.md`
- `PROGRESS.md`
- `app/ai-packages/page.tsx`
- `app/page.tsx`
- `backend/src/OetLearner.Api/Data/LearnerDbContext.cs`
- `backend/src/OetLearner.Api/Data/Migrations/20260702090000_AddAiPackageCreditsAndSeedCatalog.cs`
- `backend/src/OetLearner.Api/Domain/AiPackageCreditEntities.cs`
- `backend/src/OetLearner.Api/Endpoints/AiPackageCreditEndpoints.cs`
- `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs`
- `backend/src/OetLearner.Api/Program.cs`
- `backend/src/OetLearner.Api/Services/Billing/AiPackageCreditService.cs`
- `backend/src/OetLearner.Api/Services/LearnerService.cs`
- `backend/src/OetLearner.Api/Services/MockService.cs`
- `backend/src/OetLearner.Api/Services/SpeakingEvaluationPipeline.cs`
- `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs`
- `backend/tests/OetLearner.Api.Tests/AiPackageCreditServiceTests.cs`
- `lib/api.ts`
- `lib/billing-types.ts`

## Known Workspace Notes

- Pre-existing/unrelated untracked `.worktrees/` remains untouched.
- Existing unrelated local modifications in `app/page.tsx` were preserved while adding only the purchase-success balance banner.
