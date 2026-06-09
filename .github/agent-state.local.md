# Agent State - Billing Checkout Return Hardening

Last updated: 2026-06-09

## Goal

Harden the learner purchase journey across billing quote creation, order review, hosted checkout, payment return polling, legacy cart status lookup, and course/add-on/AI package entry points.

## Implemented

- Added a quote-backed `/checkout/review` route for plan purchases, add-ons, and AI package orders.
- Added deterministic `/billing/payment-return` handling with polling states for confirming, completed, failed, cancelled, expired, and timeout-safe processing.
- Added learner `GET /v1/billing/payment-status` to report normalized quote/payment status by quote id or provider checkout session id.
- Extended quote checkout to support `plan_purchase` and return to `/billing/payment-return` with quote/session context.
- Routed product detail course buys, add-on modal purchases, `/ai-packages`, and billing AI package cards into the review flow.
- Fixed legacy generic cart checkout status lookup to accept either the local checkout GUID or Stripe `cs_...` session id, and removed the route GUID constraint.
- Fixed legacy generic cart checkout creation to honor frontend `successUrl`/`cancelUrl` and append Stripe `{CHECKOUT_SESSION_ID}` when needed.
- Added focused backend and frontend tests for payment status, Stripe session status lookup, payment return rendering, and add-on review routing.

## Validation

- `pnpm exec vitest run app/billing/payment-return/page.test.tsx components/billing/addon-purchase-modal.test.tsx --reporter=dot`: passed, 3 files / 6 tests.
- `pnpm exec tsc --noEmit`: passed.
- `pnpm run backend:build`: passed with existing repo warnings.
- `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --no-build --filter "FullyQualifiedName~BillingCheckoutSessionGuardTests|FullyQualifiedName~CheckoutSessionStatusOwnershipTests" --nologo`: passed, 7 tests.
- `pnpm exec eslint app/checkout/review/page.tsx app/billing/payment-return/page.tsx app/ai-packages/page.tsx app/billing/page.tsx app/marketplace/packages/[id]/page.tsx components/billing/addon-purchase-modal.tsx app/billing/payment-return/page.test.tsx components/billing/addon-purchase-modal.test.tsx lib/api.ts lib/billing-types.ts`: passed with 6 existing `react-hooks/set-state-in-effect` warnings in `app/ai-packages/page.tsx` and `app/billing/page.tsx`.
- `git diff --check`: passed.

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

## Known Risks / Next Step

- Full hosted Stripe/PayPal webhook E2E was not run locally; the focused tests pin the return/status contracts and existing fulfillment tests cover idempotent entitlement application.
- Broad lint still has existing warnings in large billing/AI pages; no new warning remains in the new payment-return page.
