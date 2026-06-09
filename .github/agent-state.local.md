# Agent State - Payment Options, Subscription Timer, Freezing

Last updated: 2026-06-09

## Goal

Implement the three PDF demands into the existing OET billing/subscription system with the existing `Subscription` model as source of truth and no parallel `course_subscriptions` table.

## Implemented

- Payment options page at `app/billing/manual-payment/page.tsx` now shows the PDF method order:
  InstaPay QR/link, Vodafone Cash/Fawry, QNB Egypt, Stripe card, PayPal Business, UK Monzo transfer, International Monzo transfer.
- Only the InstaPay QR was extracted from the payment guide into `public/payment/instapay-qr.jpg`; no Monzo QR/admin-only QR material was published.
- Manual payment submit now requires candidate full name, email, WhatsApp number, selected course, amount, method, transaction reference, and proof bytes.
- Manual proof storage uses `IFileStorage` through `ManualPaymentService`, not raw file APIs.
- Admin manual-payment dashboard now exposes candidate identity, course, payment category, proof key, and supports `pending`, `needs_review`, `approved`, `paid`, `rejected`.
- Existing `Subscription` entity now carries access timer and per-subscription freeze fields.
- Added append-only `SubscriptionFreeze` entity/table and partial unique pending-request index per subscription.
- Added `FreezeRequested` and `Frozen` subscription states, learner request/resume endpoints, admin approve/reject/direct-freeze/resume endpoints, and admin table actions.
- Subscription access uses `StartedAt`/`ExpiresAt`, returns server-computed timer/freeze fields, and renewal/activation extends from `max(now, current ExpiresAt)`.
- Added `SubscriptionExpiryWorker` to expire active/trial/freeze-requested subscriptions whose `ExpiresAt` is in the past; frozen subscriptions are skipped.
- Content entitlement denial now returns `subscription_expired` or `subscription_frozen` where applicable.

## Validation

- `pnpm run backend:build`: passed. Existing warnings remained: NU1510 on `System.Text.Encoding.CodePages` plus pre-existing nullability warnings.
- `pnpm exec tsc --noEmit`: passed.
- `pnpm exec eslint app/billing/manual-payment/page.tsx app/admin/billing/manual-payments/page.tsx app/admin/billing/page.tsx components/billing/SubscriptionCard.tsx lib/api.ts lib/api/billing-expansion.ts lib/types/admin.ts`: passed with warnings only.
- `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter BillingExpansionServiceTests`: passed, 13 tests.
- Full `pnpm run lint` still exits non-zero because the repo currently has unrelated lint warnings/errors outside this change set; scoped lint for touched frontend files has no errors.

## Touched Files

- `.github/agent-state.local.md`
- `app/admin/billing/manual-payments/page.tsx`
- `app/admin/billing/page.tsx`
- `app/billing/manual-payment/page.tsx`
- `backend/src/OetLearner.Api/Data/LearnerDbContext.cs`
- `backend/src/OetLearner.Api/Data/Migrations/20260701090000_AddSubscriptionTimerFreezingAndManualPaymentFields.cs`
- `backend/src/OetLearner.Api/Domain/Entities.cs`
- `backend/src/OetLearner.Api/Domain/Enums.cs`
- `backend/src/OetLearner.Api/Domain/ManualPaymentRequest.cs`
- `backend/src/OetLearner.Api/Endpoints/AdminEndpoints.cs`
- `backend/src/OetLearner.Api/Endpoints/BillingExpansionEndpoints.cs`
- `backend/src/OetLearner.Api/Endpoints/BillingSubscriptionEndpoints.cs`
- `backend/src/OetLearner.Api/Program.cs`
- `backend/src/OetLearner.Api/Services/AdminService.cs`
- `backend/src/OetLearner.Api/Services/Billing/ManualPaymentService.cs`
- `backend/src/OetLearner.Api/Services/Billing/SubscriptionBundleInitializer.cs`
- `backend/src/OetLearner.Api/Services/Billing/SubscriptionExpiryWorker.cs`
- `backend/src/OetLearner.Api/Services/Billing/SubscriptionStateMachine.cs`
- `backend/src/OetLearner.Api/Services/Content/ContentEntitlementService.cs`
- `backend/src/OetLearner.Api/Services/Entitlements/EffectiveEntitlementResolver.cs`
- `backend/tests/OetLearner.Api.Tests/BillingExpansionServiceTests.cs`
- `components/billing/SubscriptionCard.tsx`
- `lib/api.ts`
- `lib/api/billing-expansion.ts`
- `lib/types/admin.ts`
- `public/payment/instapay-qr.jpg`

## Known Workspace Notes

- Unrelated dirty files existed before this task: `app/page.tsx`, `app/page.test.tsx`, and `lib/learner-surface.ts`. They were not intentionally modified for this task.
- Untracked repo-local PDFs and `.worktrees/` also existed before this task and were left alone.
