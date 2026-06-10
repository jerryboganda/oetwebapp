# Agent State - Production Deploy Main

Last updated: 2026-06-10

## Goal

Commit all completed billing/checkout/runtime hardening work to `main`, push it, deploy through GitHub Actions/GHCR, and close every deploy/QA issue encountered without running heavy builds on the VPS.

## Implemented

- Added a quote-backed `/checkout/review` route for plan purchases, add-ons, and AI package orders.
- Added deterministic `/billing/payment-return` handling with polling states for confirming, completed, failed, cancelled, expired, and timeout-safe processing.
- Added learner `GET /v1/billing/payment-status` to report normalized quote/payment status by quote id or provider checkout session id.
- Extended quote checkout to support `plan_purchase` and return to `/billing/payment-return` with quote/session context.
- Routed product detail course buys, add-on modal purchases, `/ai-packages`, and billing AI package cards into the review flow.
- Fixed legacy generic cart checkout status lookup to accept either the local checkout GUID or Stripe `cs_...` session id, and removed the route GUID constraint.
- Fixed legacy generic cart checkout creation to honor frontend `successUrl`/`cancelUrl` and append Stripe `{CHECKOUT_SESSION_ID}` when needed.
- Added focused backend and frontend tests for payment status, Stripe session status lookup, payment return rendering, and add-on review routing.
- Fixed production startup ordering so PostgreSQL migrations run before runtime-settings startup guards read newly added payment/Soketi columns.
- Narrowed the early startup migration/runtime-settings self-heal to PostgreSQL only; SQLite desktop/test runtimes use the existing compatibility bootstrapper because the production migration chain contains PostgreSQL-specific DDL.
- Made the historical `ExactAuthSocialPort` migration provider-aware so its additive auth/expert columns use PostgreSQL idempotent SQL on Npgsql and portable EF column operations elsewhere.

## Validation

- `pnpm exec vitest run app/billing/payment-return/page.test.tsx components/billing/addon-purchase-modal.test.tsx --reporter=dot`: passed, 3 files / 6 tests.
- `pnpm exec tsc --noEmit`: passed.
- `pnpm run backend:build`: passed with existing repo warnings.
- `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --no-build --filter "FullyQualifiedName~BillingCheckoutSessionGuardTests|FullyQualifiedName~CheckoutSessionStatusOwnershipTests" --nologo`: passed, 7 tests.
- `pnpm exec eslint app/checkout/review/page.tsx app/billing/payment-return/page.tsx app/ai-packages/page.tsx app/billing/page.tsx app/marketplace/packages/[id]/page.tsx components/billing/addon-purchase-modal.tsx app/billing/payment-return/page.test.tsx components/billing/addon-purchase-modal.test.tsx lib/api.ts lib/billing-types.ts`: passed with 6 existing `react-hooks/set-state-in-effect` warnings in `app/ai-packages/page.tsx` and `app/billing/page.tsx`.
- `git diff --check`: passed.
- `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter "FullyQualifiedName~DisableAntiforgeryUploadEndpointAuthorizationTests" --nologo --logger "console;verbosity=minimal"`: passed, 18 tests.
- `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter "FullyQualifiedName~AdminDashboard_AndContentList_RemainQueryable_WhenSqliteBacksDesktopRuntime|FullyQualifiedName~ExpertDashboard_RemainsQueryable_WhenSqliteBacksDesktopRuntime|FullyQualifiedName~FeedEndpoint_RemainsQueryable_WhenSqliteBacksDesktopRuntime" --nologo --logger "console;verbosity=minimal"`: passed, 3 tests.
- `dotnet build backend/src/OetLearner.Api/OetLearner.Api.csproj --nologo`: passed with existing warnings.
- Latest completed deploy evidence before this handoff: Build & Deploy run `27247997404` passed on `dfd030c5`; QA Smoke run `27247997413` failed backend shards on SQLite migration/startup issues now fixed locally.
- `pnpm exec vitest run app/admin/non-editor-pages.test.tsx --reporter=dot`: passed, 5 discovered files / 85 tests, after making the webhook event assertion wait for the async table row that the Speaking workflow frontend Vitest hit.

## Touched Files

- `.github/agent-state.local.md`
- `PROGRESS.md`
- `app/checkout/review/page.tsx`
- `app/billing/payment-return/page.tsx`
- `app/billing/payment-return/page.test.tsx`
- `app/marketplace/packages/[id]/page.tsx`
- `app/ai-packages/page.tsx`
- `app/billing/page.tsx`
- `components/billing/addon-purchase-modal.tsx`
- `components/billing/addon-purchase-modal.test.tsx`
- `lib/api.ts`
- `lib/billing-types.ts`
- `backend/src/OetLearner.Api/Contracts/BillingContracts.cs`
- `backend/src/OetLearner.Api/Contracts/Requests.cs`
- `backend/src/OetLearner.Api/Endpoints/BillingCheckoutEndpoints.cs`
- `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs`
- `backend/src/OetLearner.Api/Services/Billing/CheckoutService.cs`
- `backend/src/OetLearner.Api/Services/Billing/ICheckoutService.cs`
- `backend/src/OetLearner.Api/Services/LearnerService.cs`
- `backend/tests/OetLearner.Api.Tests/BillingCheckoutSessionGuardTests.cs`
- `backend/tests/OetLearner.Api.Tests/CheckoutSessionStatusOwnershipTests.cs`
- `backend/src/OetLearner.Api/Program.cs`
- `backend/src/OetLearner.Api/Data/Migrations/20260329204416_ExactAuthSocialPort.cs`
- `app/admin/non-editor-pages.test.tsx`

## Known Risks / Next Step

- Commit and push the PostgreSQL-only startup migration fix to `main`.
- Watch the new GitHub Actions Build & Deploy, QA Smoke, Speaking CI, and SBOM/SCA runs to completion.
- After deploy succeeds, verify production health endpoints and report any residual warnings.
