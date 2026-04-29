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
2. Admin subscriptions and invoices are read-only. Admins can retry only verified failed webhook processing attempts from the webhook monitoring surface, but cannot yet cancel a subscription, issue or track refunds, resend receipts, export invoices, or reconcile payment transactions from the billing page.
3. Coupon reservations are tied to quote creation. A quote currently creates a reserved redemption, so abandoned quotes must be expired and released or coupon limits can be consumed by previews.
4. Recurring subscription lifecycle is local, not provider-authoritative. Stripe checkout uses one-time payment mode, while local subscriptions carry renewal dates and intervals. Renewal, dunning, cancellation, tax, provider invoices, and refunds are not yet mature.
5. Catalog changes mutate plans and prices in place. Existing subscriptions and historical invoices can drift if a code, price, interval, or entitlement is edited after purchase. Versioned catalog snapshots are needed.
6. Entitlement logic is fragmented. Content, grammar, AI quotas, and sponsor access should eventually use one entitlement resolver that combines subscription state, add-ons, free-tier rules, freeze state, and sponsor seats.
7. Webhook retry is now a bounded verified replay path, not a full inbox processor. Failed legacy or unverified rows are intentionally non-retryable; bulk replay, provider redelivery orchestration, and reconciliation workflows still need to be explicit and observable.
8. Invoice evidence is partially improved. Admins can now inspect local invoice, quote, payment, coupon, subscription-item, catalog-anchor, and billing-event evidence from the invoice list, but production billing still needs provider invoice IDs, line items, tax/VAT fields, receipt URLs, PDF artifacts, and sponsor ownership.
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

Status: Phase 2A admin catalog validation hardening, Phase 2B-1 quote-time fulfillment snapshots, Phase 2B-2A immutable billing catalog version anchors, Phase 2B-2B admin catalog version history, and Phase 2B-2C admin invoice evidence inspection are implemented. The current model creates immutable plan, add-on, and coupon version rows, backfills baseline v1 rows during migration, stores active/latest version pointers on mutable catalog rows, writes version references onto quotes, subscriptions, subscription items, invoices, payment transactions, and coupon redemptions, exposes read-only admin history timelines for catalog operators, and exposes a read-only invoice evidence drawer for local checkout/payment/coupon/event correlation. Fulfillment still treats the quote snapshot as the source of truth; version references are audit/reporting anchors and correlation evidence, not the authority for what gets granted after checkout.

Implemented scope:

- Add typed server validation for plan, add-on, coupon, and entitlement payloads.
- Add immutable plan, add-on, and coupon version tables with baseline backfill.
- Store catalog version references on quote, subscription, invoice, payment transaction, subscription item, and coupon redemption records.
- Append a new catalog version on each admin catalog update while preserving prior version rows.
- Treat catalog codes as immutable after creation; mutable display, pricing, entitlement, and status fields move forward through appended versions.
- Keep checkout completion fulfillment based on `BillingQuote.SnapshotJson`, with version IDs retained for audit and downstream reporting.
- Expose read-only plan, add-on, and coupon version-history endpoints plus an admin drawer timeline that lazy-loads the selected catalog item's history.
- Expose read-only invoice evidence from the invoice list, including curated invoice, quote, payment transaction, coupon redemption, subscription item, catalog anchor, and event timeline sections. Raw payment metadata, billing-event payloads, and webhook payloads are not returned.

Migration/deploy preflight:

- Apply and inspect `20260428120000_AddBillingImmutableCatalogVersioning` against a production-like database before deploy.
- Confirm existing plans, add-ons, and coupons receive v1 version rows and matching `ActiveVersionId` / `LatestVersionId` values.
- Confirm new nullable version-reference columns and `{}` JSON defaults do not break existing subscriptions, invoices, payment transactions, or coupon redemptions.
- Apply and inspect `20260428133000_WidenBillingCheckoutReferences` so local checkout/session evidence can hold provider transaction identifiers up to 256 characters.
- Treat rollback of `20260428133000_WidenBillingCheckoutReferences` as restore-or-forward-fix once longer provider identifiers are written, because narrowing those columns back to 64 characters may fail if long values exist.
- Take a database backup before production migration, then run backend tests and a checkout smoke path after deploy.

Acceptance criteria:

- Historical subscriptions and invoices remain stable after catalog edits.
- Invalid catalog data returns structured 400 responses.
- Admins can archive catalog entries without deleting business evidence.
- New catalog edits append immutable versions rather than mutating prior version evidence.
- New checkout, payment, invoice, subscription, and coupon redemption rows retain quote/version audit anchors.
- Admin billing operators can inspect catalog version history newest-first, including active/latest markers and compact catalog-field summaries, without mixing in mutable payment or redemption evidence.
- Admin billing operators can inspect the local evidence behind an invoice without mutating billing state or exposing raw provider/webhook payloads.

Residual risk after Phase 2B-2C:

- Provider price mapping, provider invoice/receipt evidence, refund/dispute visibility, webhook inbox processing, and version-aware reporting/backfill are still incomplete.
- Database-level immutability hardening is not yet enforced with triggers, restrictive permissions, or append-only database constraints; immutability is currently application-enforced.
- Coupon usage limits are improved with parent coupon/version references, but true race-safe coupon inventory still needs a larger transactional design, provider event reconciliation, and concurrency tests.

### Phase 3: Payment Ledger And Webhook Inbox

Status: Phase 3A read-only admin payment transaction ledger and Phase 3B bounded verified webhook retry are implemented. The Billing Ops page now exposes sanitized local `PaymentTransaction` activity with filters for status, gateway, transaction type, and search. The Webhook Monitoring page now shows retry eligibility and can synchronously reprocess failed local webhook fulfillment only when the original webhook was signature-verified at ingestion, parsed by the supported parser version, and carries durable payload-hash and payment-status evidence. These surfaces are operational controls, not revenue recognition or provider reconciliation, and they intentionally exclude raw payment metadata and webhook payloads.

Implemented scope:

- Add `/v1/admin/billing/payment-transactions` behind `AdminBillingRead`.
- Return a strict allow-list of payment activity fields: learner reference, gateway transaction ID, type, status, amount, product/quote/version anchors, and timestamps.
- Paginate and clamp page sizes server-side for the higher-volume payment activity table.
- Add the read-only Payment Transactions panel to `/admin/billing` with compact desktop and mobile views.
- Add regression coverage for filtering, paging clamps, newest-first ordering, learner-name resolution, and raw metadata/webhook payload non-exposure.
- Persist retry evidence on payment webhook rows: verification status, verified timestamp, parser version, payload hash, parsed gateway transaction ID, parsed normalized payment status, attempt count, retry count, and retry audit fields.
- Reuse the provider-verified parsed webhook result for admin retry instead of reparsing or trusting stored raw payloads.
- Mark legacy, failed-verification, missing-parser, missing-hash, missing-transaction, missing-status, and pending/unknown-status webhook rows as not retryable.
- Reprocess one retryable failed webhook synchronously, with local fulfillment wrapped in a transaction and wallet-credit fulfillment protected by a database idempotency index.
- Make the webhook admin UI refresh from the backend after retry and report actual outcomes instead of optimistic `queued` status.
- Add backend regression coverage for legacy non-retryability and verified wallet top-up replay without double crediting, plus frontend coverage for retry eligibility and post-retry refresh.

Remaining scope:

- Add a first-class webhook inbox processor for bulk/backoff retry, leasing, provider redelivery orchestration, and reconciliation warnings beyond the current one-row verified admin replay.
- Add relational/PostgreSQL retry-path coverage for `jsonb` payload persistence, transactions, filtered unique indexes, and concurrent retry/redelivery behavior.
- Add provider customer, provider subscription, provider invoice, invoice line, refund, and dispute read models.
- Move recurring subscriptions to provider-authoritative lifecycle handling where available.

Acceptance criteria:

- Admins can answer who paid from local payment activity; grant/coupon/provider-event causality is completed only when invoice evidence and webhook monitoring are used together.
- Verified failed webhook processing can be retried, audited, and replayed without trusting raw stored payloads.
- Legacy or unverified failures are clearly blocked and must be redelivered through the live provider webhook endpoint.
- Refunds and cancellations are provider-first operations with clear local state transitions.

### Phase 4: Entitlement Resolver

Status: Phase 4 Slice 1 backend effective entitlement resolver is implemented. The first slice is intentionally code-only because EF migration snapshot drift makes avoidable schema churn risky. It centralizes the current direct-subscription rule for compact quota gates: the latest learner subscription is eligible only when its status is `Active` or `Trial`; inactive latest rows fall back to free-tier behavior. The resolver also returns normalized plan code, active add-on codes, and active freeze overlay facts for follow-on slices, but this first slice does not yet change content package, media, review, sponsor, or provider lifecycle behavior.

Implemented scope:

- Add an internal `IEffectiveEntitlementResolver` that resolves latest subscription status, billing plan by ID or code, active subscription-item add-ons, and active/due account freeze overlays without adding new tables or migrations.
- Wire `GrammarEntitlementService`, `PronunciationEntitlementService`, and `ConversationEntitlementService` through the resolver while preserving their existing free-tier window counters and learner-facing messages.
- Wire `AiQuotaService` through the resolver so cancelled, expired, pending, suspended, or past-due subscriptions no longer unlock paid AI quota plans.
- Add regression coverage for resolver subscription precedence, plan code normalization, active add-on visibility, freeze overlay visibility, AI free-plan fallback, and existing module entitlement behavior.

Remaining scope:

- Extend the resolver into `ContentEntitlementService`, video lessons, strategy guides, media access, and review-credit flows after reconciling package rules versus plan entitlement JSON.
- Add explicit sponsor billing/seat grants before treating sponsor links as paid access inheritance; current sponsor entities do not carry a beneficiary-scoped paid plan or add-on allocation.
- Add provider-authoritative subscription cancellation/paid-through semantics before modelling cancelled-but-paid-through access.
- Decide how freeze `EntitlementPauseMode` affects rolling quota windows, AI counters, and recurring entitlement clocks.

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
