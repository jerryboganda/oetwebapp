# Admin Billing Audit And Implementation Plan

## Scope

This audit covers the admin billing workspace at `/admin/billing` and the business workflow it depends on across learner checkout, coupons, subscriptions, invoices, sponsor billing, content entitlements, permissions, and payment webhooks.

Key files reviewed:

- `app/admin/billing/page.tsx`
- `app/billing/page.tsx`
- `app/sponsor/billing/page.tsx`
- `lib/api.ts`
- `lib/admin.ts`
- `lib/types/admin.ts`
- `backend/src/OetLearner.Api/Endpoints/AdminEndpoints.cs`
- `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs`
- `backend/src/OetLearner.Api/Services/AdminService.cs`
- `backend/src/OetLearner.Api/Services/LearnerService.cs`
- `backend/src/OetLearner.Api/Services/SponsorService.cs`
- `backend/src/OetLearner.Api/Services/Content/ContentEntitlementService.cs`
- `backend/src/OetLearner.Api/Services/PaymentGatewayService.cs`

## Current Implementation Map

The admin billing page is a live operational surface, not a mock. It can create and update billing plans, add-ons, and coupons through `/v1/admin/billing/*` endpoints. It also lists subscriptions, coupon redemptions, and invoices, but those sections are read-only.

Learner billing is quote-driven. The learner page loads the current subscription, plans, add-ons, invoices, wallet state, and freeze status. It creates server-priced quotes, opens Stripe or PayPal checkout sessions, and relies on payment webhooks to complete the local subscription, add-on, coupon, invoice, wallet, and billing-event records.

The backend already has useful primitives: `BillingPlan`, `BillingAddOn`, `BillingCoupon`, `BillingCouponRedemption`, `BillingQuote`, `BillingEvent`, `SubscriptionItem`, `PaymentTransaction`, and `PaymentWebhookEvent`. Admin permissions are split into `AdminBillingRead` and `AdminBillingWrite`.

## Business Workflow Gaps

1. Sponsor billing is placeholder-only. `SponsorService.GetBillingAsync` returns zero spend and empty invoices, so sponsor-paid learner workflows do not yet have a real billing account, seat ledger, invoice model, or entitlement grant.
2. Admin subscriptions and invoices are read-only. Admins cannot safely retry failed payment application, cancel a subscription, issue or track refunds, resend receipts, export invoices, or reconcile payment transactions from the billing page.
3. Coupon reservations are tied to quote creation. A quote currently creates a reserved redemption, so abandoned quotes must be expired and released or coupon limits can be consumed by previews.
4. Recurring subscription lifecycle is local, not provider-authoritative. Stripe checkout uses one-time payment mode, while local subscriptions carry renewal dates and intervals. Renewal, dunning, cancellation, tax, provider invoices, and refunds are not yet mature.
5. Catalog changes mutate plans and prices in place. Existing subscriptions and historical invoices can drift if a code, price, interval, or entitlement is edited after purchase. Versioned catalog snapshots are needed.
6. Entitlement logic is fragmented. Content, grammar, AI quotas, and sponsor access should eventually use one entitlement resolver that combines subscription state, add-ons, free-tier rules, freeze state, and sponsor seats.
7. Webhook retry is not a full inbox processor. Failed webhook rows can be marked for retry, but the operational processor and reconciliation workflow need to be explicit and observable.
8. Invoice evidence is minimal. Invoices are local records with plain text downloads; production billing needs provider invoice IDs, line items, tax/VAT fields, receipt URLs, PDF artifacts, and sponsor ownership.
9. Admin validation is still too loose. JSON entitlement fields, coupon windows, discount caps, interval/currency values, compatible plan references, and usage limits need stronger server-side validation and focused tests.
10. Test coverage is thin around money-moving behavior. Existing tests cover route shape and hosted checkout configuration, but not enough successful webhook application, coupon limits, add-on compatibility, sponsor billing, or admin mutation behavior.

## Phase Plan

### Phase 1: Safety Seal

Status: implemented in this branch.

- Enforce active, purchasable plans and add-ons in quote creation.
- Enforce add-on compatibility against the learner's current plan.
- Persist resolved add-on codes in quotes so add-on-scoped coupons cannot be bypassed by omitting client add-on codes.
- Release expired coupon reservations before checking coupon usage limits.
- Align plan/add-on checkout return URLs with `/billing`.
- Pass the selected checkout gateway from the learner billing UI to the backend.
- Let content entitlement lookup match subscriptions that store billing plan codes, not only billing plan IDs.

Acceptance criteria:

- Hidden, inactive, archived, or incompatible catalog items cannot be quoted for checkout.
- Add-on-scoped coupons only apply when the resolved purchased add-on is allowed.
- Expired coupon reservations do not permanently exhaust coupon limits.
- Checkout returns to the active learner billing page.

Known residual risk:

- Coupon limit race-safety still needs a larger transactional design with provider event reconciliation and concurrency tests. Phase 1 removes abandoned-reservation leakage but does not claim serializable coupon inventory.

### Phase 2: Catalog Validation And Versioning

Status: Phase 2A admin catalog validation hardening is implemented after Phase 1. Phase 2B-1 now starts the historical-stability work by making new checkout completion fulfill from quote-time catalog snapshots. Full immutable version tables remain Phase 2B-2 because they require schema changes, backfill, and checkout/webhook/subscription history rewiring.

- Add typed server validation for plan, add-on, coupon, and entitlement payloads.
- Add immutable plan, add-on, price, and coupon version snapshots.
- Store version references on quotes, subscriptions, invoices, and subscription items.
- Add before/after audit diffs for catalog changes.

Acceptance criteria:

- Historical subscriptions and invoices remain stable after catalog edits.
- Invalid catalog data returns structured 400 responses.
- Admins can archive catalog entries without deleting business evidence.

Phase 2B-1 scope:

- Persist the plan/add-on/coupon facts needed for fulfillment inside `BillingQuote.SnapshotJson`, then make payment webhook completion prefer those quote-time facts over mutable current catalog rows. This protects new in-flight checkouts from price, duration, credit, and display-name drift between quote creation and payment completion.

Residual risk after Phase 2B-1:

- Historical rows created before quote-time catalog snapshots still need fallback behavior, and full immutable catalog version tables are still required for durable reporting, backfill, provider price mapping, and admin version history.

### Phase 3: Payment Ledger And Webhook Inbox

- Add a first-class webhook inbox processor with retry, idempotency, replay protection, and operator visibility.
- Add provider customer, provider subscription, provider invoice, invoice line, refund, and dispute read models.
- Move recurring subscriptions to provider-authoritative lifecycle handling where available.

Acceptance criteria:

- Admins can answer who paid, what was granted, which coupon applied, and which provider event caused the state change.
- Failed webhook processing is retryable, audited, and idempotent.
- Refunds and cancellations are provider-first operations with clear local state transitions.

### Phase 4: Entitlement Resolver

- Introduce one entitlement resolver for learner plan, add-ons, free tier, trial, sponsor seat, freeze state, and resource scope.
- Replace module-specific subscription checks in content, grammar, AI quota, pronunciation, conversation, and review flows.

Acceptance criteria:

- Every paid access decision can be traced to one resolver decision.
- Add-ons and sponsor seats affect access consistently across modules.
- Frozen or suspended accounts remain read-only where required.

### Phase 5: Sponsor Billing

- Add sponsor billing accounts, seat packs, sponsor checkout, payment methods, sponsor invoices, and learner consent-aware sponsor entitlements.
- Replace placeholder sponsor spend and invoice values with real data.
- Add sponsor audit events for invite, activation, revocation, billing, and entitlement grants.

Acceptance criteria:

- Sponsors can pay for seats or learner cohorts and see spend/invoices.
- Sponsored learners receive correct access without direct personal payment.
- Revoked sponsorships stop future sponsor-paid access without deleting history.

### Phase 6: Admin Operations And Reporting

- Add admin views for payment transactions, webhook status, reconciliation warnings, failed invoices, coupon abuse signals, and exports.
- Add safe actions behind permissions and confirmations: retry webhook, export CSV, resend receipt, cancel subscription, refund, and provider sync where business-approved.
- Split the admin billing page into focused components if maintenance requires it.

Acceptance criteria:

- Billing operators can manage support cases from admin without direct database edits.
- Risky actions are permission-gated, audited, idempotent, and reversible through business workflows.
- Reporting is paginated and does not load unbounded billing data.

## Verification Strategy

- Backend tests for quote eligibility, add-on compatibility, coupon reservations, webhook completion, idempotency, and entitlement resolution.
- Frontend tests for admin CRUD forms, billing table rendering, gateway selection, API normalization, and validation errors.
- E2E smoke for `/admin/billing`, `/billing`, sponsor billing once implemented, and safe checkout quote paths.
- Build gates before merge: `npx tsc --noEmit`, `npm run lint`, `npm test`, `npm run build`, `npm run backend:build`, and `npm run backend:test` as scope allows.

## Safe GitHub Workflow

The user is working from another computer on the same repository, so every upload must avoid broad staging and force pushes.

Required flow:

1. Inspect `git status` and changed files.
2. Run focused tests and static checks.
3. Fetch the remote branch.
4. Rebase or merge remote changes if needed; never force-push.
5. Stage explicit files only.
6. Review `git diff --cached`.
7. Commit with a focused message.
8. Push normally.

Do not delete Docker volumes, reset production data, or run destructive VPS commands as part of this billing module work.
