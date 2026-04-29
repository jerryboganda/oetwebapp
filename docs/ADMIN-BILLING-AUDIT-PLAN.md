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

The admin billing page is a live operational surface, not a mock. It can create and update billing plans, add-ons, and coupons through `/v1/admin/billing/*` endpoints. It also lists subscriptions, coupon redemptions, invoices, payment transactions, and audited billing operations. Catalog mutation remains controlled; operational money movement still follows explicit support/finance workflows before provider execution.

Learner billing is quote-driven. The learner page loads the current subscription, plans, add-ons, invoices, wallet state, and freeze status. It creates server-priced quotes, opens Stripe or PayPal checkout sessions, and relies on payment webhooks to complete the local subscription, add-on, coupon, invoice, wallet, and billing-event records.

The backend already has useful primitives: `BillingPlan`, `BillingAddOn`, `BillingCoupon`, `BillingCouponRedemption`, `BillingQuote`, `BillingEvent`, `SubscriptionItem`, `PaymentTransaction`, and `PaymentWebhookEvent`. Admin permissions are split into `AdminBillingRead` and `AdminBillingWrite`.

## Business Workflow Gaps

1. Sponsor billing is now a read-only local ledger, but it is not yet a complete B2B revenue workflow. `SponsorService.GetBillingAsync` derives spend, invoices, downloads, and seat usage from existing sponsorship/link/cohort evidence; sponsor-owned invoices, paid seat packs, checkout, payment methods, and provider reconciliation remain open.
2. Admin subscriptions and invoices are read-only. Admins can retry only verified failed webhook processing attempts from the webhook monitoring surface and can now record audited billing operations, but cannot yet execute provider refunds, cancel provider subscriptions, resend receipts, export invoices, or complete automated payment reconciliation from the billing page.
3. Coupon reservations are tied to quote creation. A quote currently creates a reserved redemption, so abandoned quotes must be expired and released or coupon limits can be consumed by previews.
4. Recurring subscription lifecycle is local, not provider-authoritative. Stripe checkout uses one-time payment mode, while local subscriptions carry renewal dates and intervals. Renewal, dunning, cancellation, tax, provider invoices, and refunds are not yet mature.
5. Catalog changes mutate plans and prices in place. Existing subscriptions and historical invoices can drift if a code, price, interval, or entitlement is edited after purchase. Versioned catalog snapshots are needed.
6. Entitlement logic is now centralised through `ILearnerEntitlementResolver` for grammar, content papers, protected media, pronunciation, conversation, AI quota plan resolution, vocabulary premium access, strategy guides, video lessons, and private-speaking session coverage (sponsor-seat / add-on bookings skip Stripe and are marked covered with audit evidence). Tutoring booking now derives price server-side from `ExpertOnboardingProgress.RatesJson` and ignores any client-supplied price. The `AiCreditRenewalWorker` intentionally renews only direct active paid subscriptions (trial / sponsor / add-on / cancelled / past-due explicitly do not), and that divergence from the resolver is documented in source. Whisper ASR calls now propagate the learner id through `AsrRequest.UserId` so AI usage records are attributed correctly. Freeze/status enforcement, review, and remaining teacher-service access policies are the next resolver-adoption candidates.
7. Webhook retry is now a bounded verified replay path, not a full inbox processor. Failed legacy or unverified rows are intentionally non-retryable; bulk replay, provider redelivery orchestration, and reconciliation workflows still need to be explicit and observable.
8. Invoice evidence is partially improved. Admins can now inspect local invoice, quote, payment, coupon, subscription-item, catalog-anchor, and billing-event evidence from the invoice list, but production billing still needs provider invoice IDs, line items, tax/VAT fields, receipt URLs, PDF artifacts, and sponsor ownership.
9. Admin validation is still too loose. JSON entitlement fields, coupon windows, discount caps, interval/currency values, compatible plan references, and usage limits need stronger server-side validation and focused tests.
10. Test coverage is thin around money-moving behavior. Existing tests cover route shape and hosted checkout configuration, but not enough successful webhook application, coupon limits, add-on compatibility, sponsor billing, or admin mutation behavior.

## Requirements Coverage Matrix

This matrix tracks the full Billing & Subscription product scope for the OET academy. It is intentionally broader than the currently implemented code so future slices do not lose revenue, access-control, compliance, or teacher-cost requirements.

| Capability | Current status | Notes / next phase |
|---|---|---|
| Free, basic, premium, intensive, annual, course, mock, and lifetime plan families | Partial | Billing plans support price, interval, visibility, renewability, trial days, credits, subtests, and entitlements. Seeded plans cover basic/premium/intensive/monthly/yearly, but course/mock/lifetime/package-specific selling rules need catalog policy and entitlement work. |
| One-time purchases and add-ons | Partial | Add-ons and checkout quotes exist for review credits and priority handling. Writing/speaking/mock/private tutoring/service-specific product taxonomy still needs typed credits and fulfillment rules. |
| Hosted card checkout | Partial | Stripe/PayPal rails exist with hosted checkout and sandbox fallback controls. Recurring provider subscriptions, provider customers, provider payment methods, provider invoices, and provider reconciliation are not complete. |
| Local/manual payments | Partial | Admin billing operations can record manual-payment work with evidence references. Student proof upload, unique payment references, admin verification checklist, approval-to-access workflow, and receipt generation remain open. |
| Subscriptions and renewals | Partial | Local subscriptions track plan/status/renewal date. Provider-authoritative renewal, dunning, pause, cancel-at-period-end, proration, and lifecycle reconciliation remain open. |
| Free trials | Partial | `BillingPlan.TrialDays` exists, but trial issuance, one-trial-per-user/device/phone controls, trial reminders, trial entitlements, and trial expiry downgrade are not yet a full workflow. |
| Coupons and promotion codes | Partial | Coupons support percentage/fixed discounts, windows, limits, minimum subtotal, plan/add-on scopes, stackability, redemptions, and admin CRUD/version history. Country/new-user/existing-user/affiliate/auto-apply/race-safe inventory and abuse analytics remain open. |
| Coupon campaign reporting | Missing | Coupon redemptions are listed, but campaign revenue, discount sacrifice, conversion, abuse attempts, best campaigns, and expired campaign cleanup need reporting views and exports. |
| Entitlement-based access | Partial | The central resolver now backs grammar, content-paper access, protected media, pronunciation, conversation, AI quota direct-plan resolution, and vocabulary premium access. Strategy/video package access, review, teacher-service flows, and explicit freeze/suspension read-only enforcement still need policy work. |
| Credit system for teacher services | Partial | Wallet credits and wallet transactions exist. The academy still needs typed ledgers for writing, speaking, full mock, private tutoring, review, monthly reset, purchased-credit expiry, trial-credit expiry, refund removal, and admin-only transfers. |
| Invoices and receipts | Partial | Local invoices and learner/sponsor text downloads exist. Sequential invoice numbering, line items, tax/VAT/GST fields, billing addresses, tax IDs, provider receipt URLs, PDF artifacts, sponsor ownership, and B2B invoice polish remain open. |
| Refunds | Partial | Admin operations can record refund requests, and gateway abstraction has a refund method placeholder. Real provider refund execution, full/partial/credit refunds, usage checks, access/credit adjustment, commission reversal, dispute evidence, and notification workflows remain open. |
| Failed payments and dunning | Missing | Webhook failures are visible/retryable in a bounded way, but renewal invoice failure, retry schedules, grace periods, reminders, access limiting, card update links, and win-back coupons need a dedicated lifecycle. |
| Cancellation, pause, upgrade, downgrade, and proration | Partial | Learner quote/change preview and local subscription updates exist. Provider subscriptions, immediate upgrade/prorated charges, next-cycle downgrades, pause, cancellation reason capture, and cancellation deflection remain open. |
| Student billing portal | Partial | `/billing` shows current billing data, plans, add-ons, invoices, wallet, quote checkout, and downloads. It still needs cancellation/pause, payment-method updates, refund requests, tax/billing details, explicit renewal terms, and credit-type balances. |
| Admin billing dashboard | Partial | `/admin/billing` manages catalog, redemptions, subscriptions, invoices/evidence, payment transactions, webhooks, and operations. It still needs CSV exports, failed-invoice queue, refund/cancel/resend actions, revenue/churn/MRR/LTV, coupon analytics, taxes, and teacher payout liability. |
| Sponsor billing | Partial | Phase 5A now replaces placeholder sponsor spend/invoices with a scoped read-only sponsor billing view, invoice download, seat usage, consent-aware link invoice visibility, and tracked capacity enforcement for invites. Sponsor billing accounts, seat packs, sponsor checkout, sponsor-owned invoices, consent UX, and full revocation/audit behavior remain open. |
| Scholarships and grants | Missing | Do not model scholarships as random coupons. Need a controlled scholarship workflow with approval reason, approver, duration, access level, usage limits, notes, and audit trail. |
| Referrals and affiliates | Partial | Basic referral entities exist. Affiliate codes, cookie periods, commissions, payout thresholds, refund reversal, dashboards, and partner reporting remain open. |
| Teacher commissions and payouts | Missing | No first-class payout ledger exists for writing corrections, speaking mocks, live classes, hourly work, revenue share, salary, bonuses, approval, payment, and reversal. |
| Billing notifications | Partial | Notification infrastructure exists elsewhere, but billing triggers need explicit events for trial started/ending/expired, payment success/failure, renewal, cancellation, upgrade/downgrade, credits low/added, refunds, and coupon usage. |
| Taxes and compliance invoices | Missing | Stripe Tax/Merchant-of-Record style tax handling, VAT/GST/sales-tax rules, tax reports, invoice tax lines, B2B tax IDs, and regional compliance policy remain open. |
| Gateway-agnostic architecture | Partial | `IPaymentGateway` abstracts Stripe/PayPal hosted checkout. Safepay/Paymob/JazzCash/manual bank transfer/mobile wallet/app-store rails, tokenized payment methods, and provider-specific reconciliation read models remain open. |
| Mobile app billing | Missing | Apple/Google in-app subscriptions and entitlement sync are not implemented. Need duplicate-subscription prevention across web, iOS, Android, manual grants, and scholarships. |
| Security and audit | Partial | Hosted checkout, webhook signature verification, admin permissions, audit events, and sanitized ledgers exist. Finance action 2FA, refund permission splits, export controls, provider secret hygiene reviews, and raw-card-data avoidance policy checks should be explicit launch gates. |
| Reports and exports | Partial | Admin lists exist. Sales, subscription, coupon, refund, tax, invoice, credit usage, teacher payout, affiliate commission, churn, failed payment, CSV/PDF/Excel/API exports remain open. |

## Extended Delivery Roadmap

The first six phases harden the current checkout and admin surfaces. The following phases cover the remaining academy-grade billing requirements from the matrix.

### Phase 7: Trials, Free Plan, And Cancellation Lifecycle

- Implement no-card trial issuance, trial entitlements, trial expiry, trial reminder notifications, and one-trial-per-user controls.
- Add self-service cancellation, cancel-at-period-end, pause, downgrade scheduling, cancellation reason capture, and clear renewal/cancellation disclosures.
- Add failed-payment state, grace period policy, dunning retry schedule, and payment-method update links.

### Phase 8: Typed Credits And Teacher-Service Fulfillment

- Replace generic review-credit-only semantics with typed credit ledgers: writing, speaking, full mock, private tutoring, review, and goodwill.
- Add monthly subscription credit grants, purchased-credit expiry, trial-credit expiry, refund removal, duplicate-submission restoration, and admin transfer controls.
- Connect credit consumption to teacher work queues so teacher time is not exposed as unlimited access.

### Phase 9: Refunds, Manual Payments, And Finance Operations

- Turn billing operations into idempotent workflows for manual payment approval, provider refund execution, credit refund, access adjustment, and receipt generation.
- Add manual payment proof upload, unique references, duplicate-proof detection, verification checklist, and bank/wallet reconciliation fields.
- Add refund policy evidence: reason, usage snapshot, teacher work started, admin notes, access after refund, coupon reversal, and commission reversal.

### Phase 10: Provider And Tax Reconciliation

- Add provider customer, payment method, provider subscription, provider invoice, invoice line, refund, dispute, and tax read models.
- Add tax/VAT/GST fields, billing addresses, tax IDs, receipt URLs, PDF invoice artifacts, tax reports, and provider/Merchant-of-Record decision records.
- Add recurring provider lifecycle handling for Stripe or another supported subscription rail before relying on auto-renewing subscription revenue.

### Phase 11: Sponsor Seats, Scholarships, Referrals, Affiliates, And Teacher Payouts

- Add sponsor billing accounts, seat packs, sponsor checkout, sponsor-owned invoices, cohort seat ledgers, learner consent, sponsor entitlements, and revocation rules.
- Add separate scholarship/grant workflow with approval, reason, duration, access level, usage limits, and audit trail.
- Add affiliate commission ledgers, payout thresholds, refund reversal, dashboards, and teacher payout ledgers for corrections, speaking mocks, live classes, and private sessions.

### Phase 12: Executive Reporting, Exports, And Launch QA

- Add MRR, ARR, ARPU, LTV, churn, trial conversion, failed-payment recovery, refund rate, coupon performance, credits sold/used, teacher payout liability, gateway fees, taxes, and net revenue reports.
- Add CSV/PDF/Excel/API exports with permission gates and export audit logs.
- Add launch test scenarios for trial expiry, coupon rejection, payment failure, cancellation, refund, manual approval, credit consumption/restoration, teacher payout generation, invoice download, and webhook replay idempotency.

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

### Phase 3C: Admin Billing Operations Ledger

Status: implemented in this branch.

- Add an admin-only `BillingOperation` ledger for manual payments, refund requests, credit adjustments, and reconciliation notes.
- Expose list, create, and resolve endpoints under `/v1/admin/billing/operations` behind `AdminBillingRead` and `AdminBillingWrite`.
- Keep new operations open at creation, then resolve them as `approved`, `rejected`, `completed`, or `cancelled` with resolution notes.
- Link operations to local payment transaction, invoice, subscription, quote, gateway reference, and evidence URL fields when available.
- Write sanitized `BillingEvent` rows and admin audit events without exposing raw provider/webhook payloads.
- Add focused backend coverage for permissions, validation, paging/filtering, sanitized events, and resolution audit trails.

Acceptance criteria:

- Billing operators can record support and finance decisions without direct database edits.
- Refund and manual-payment work can be tracked locally before provider-mutating actions are wired.
- Every operation has an actor, learner, reason, timestamp, status, and immutable evidence trail.
- Resolved operations cannot be resolved again.

Remaining scope:

- Provider refund execution and provider subscription cancellation remain separate, explicit Phase 6+ actions.
- Credit adjustments are recorded in the ledger but should only mutate wallet credits once a dedicated, idempotent application workflow is added.
- Manual payment approval still needs provider/bank evidence reconciliation before revenue recognition.

### Phase 4: Entitlement Resolver

Status: Phase 4A resolver foundation, grammar/content/media sponsor-seat inheritance, pronunciation/conversation resolver adoption, AI quota direct-plan resolution, and vocabulary premium resolver adoption implemented/partial.

Implemented scope:

- Add a backend `ILearnerEntitlementResolver` foundation that resolves learner entitlement facts for a resource without changing the billing data model.
- Centralize current evidence for anonymous/free users, direct active subscriptions, direct trial subscriptions, active add-on rows, current active freeze records, active sponsorship rows, and consented sponsor links tied to active sponsor accounts.
- Treat sponsor-seat evidence as valid only when `Sponsorship.Status == "Active"` with a matching learner user ID, or when a `SponsorLearnerLink` is learner-consented and belongs to an active `SponsorAccount`.
- Wire grammar entitlement through the resolver so direct active/trial subscriptions and valid sponsor seats receive unlimited grammar lessons while the free 3-lessons-per-rolling-7-days quota remains unchanged.
- Wire content-paper entitlement through the resolver so valid sponsor seats can access premium papers while existing free-paper and direct-plan bundle restrictions remain intact.
- Wire protected media access through the content entitlement gate so published paper media inherits direct plan bundle restrictions and valid sponsor-seat access while admin, uploader, free-preview, and profession checks remain intact.
- Wire pronunciation and conversation entitlement through the resolver so direct paid/trial, sponsor-seat, add-on, and current-freeze evidence bypasses free quotas while anonymous/free users keep existing limits.
- Wire AI quota plan resolution through the resolver's current direct subscription evidence so cancelled, expired, pending, suspended, or past-due subscriptions cannot select paid AI quota plans.
- Wire vocabulary premium access through the resolver so premium quiz formats and list-size bypasses no longer trust caller-supplied claim heuristics.
- Update AI credential policy so pronunciation scoring/feedback, conversation evaluation, and admin pronunciation/conversation/vocabulary draft features use platform-only credential routing where required.
- Add focused backend tests for direct subscription, trial, free, sponsor-seat, pending/revoked sponsorship, consented/unconsented sponsor links, inactive sponsor accounts, add-on/freeze evidence, grammar sponsor-seat/free-quota behavior, content-paper sponsor access, protected media sponsor access, pronunciation/conversation sponsor access, AI quota invalid-subscription fallback, vocabulary premium access, and AI credential platform-only policy.

Remaining resolver consumers:

- Replace remaining module-specific subscription/package checks in strategy guide, video lesson/package access, freeze/status policy, review, video-adjacent media flows not yet covered by protected media, and future teacher-service flows.
- Define resource-specific add-on semantics before any add-on evidence unlocks paid behavior outside modules that explicitly opt in.
- Define frozen/suspended read-only behavior per resource before the resolver becomes an enforcement layer for freeze and account-status policy.

Acceptance criteria:

- Every paid access decision can be traced to one resolver decision.
- Add-ons and sponsor seats affect access consistently across modules.
- Frozen or suspended accounts remain read-only where required.

### Phase 5: Sponsor Billing

Status: Phase 5A read-only sponsor billing, seat usage, consent scoping, and invite capacity slice implemented/partial.

Implemented scope:

- Replace `SponsorService.GetBillingAsync` placeholder with real read-only response derived from existing ledgers.
- For authenticated sponsor user, find `SponsorAccount` and non-revoked `Sponsorships`.
- Compute total/pending/active sponsorship counts from `Sponsorship` status values.
- Derive sponsored learner IDs from non-revoked `Sponsorship.LearnerUserId` values and learner-consented `SponsorLearnerLink` rows for the `SponsorAccount.Id`.
- Pull invoices for sponsored learner IDs from existing `Invoice` rows, newest first, bounded to recent 25 max, and only from the date the learner entered the sponsor scope. Link-based invoice visibility starts at consent time when `ConsentedAt` is available.
- Return sanitized invoice fields only: id/invoiceId, learnerUserId, learnerEmail, description, amount, currency, status, issuedAt, quoteId, checkoutSessionId, download availability boolean.
- Add a sponsor-scoped invoice download endpoint that verifies the invoice belongs to a currently sponsored learner before returning a simple invoice file.
- Compute `totalSpend` from paid/succeeded invoices for sponsored learner IDs; compute `currentMonthSpend` for current UTC month.
- Include billingCycle, currency, invoiceCount, paidInvoiceCount, lastInvoiceAt, totalSponsorships, activeSponsorships, pendingSponsorships, sponsoredLearnerCount.
- Include a sponsor seat-usage read model derived from existing active cohorts, non-revoked sponsorships, pending invitations, and consented sponsor links: capacity, assigned, active, pending, consented, remaining, and capacity-tracked state.
- Block new sponsor invitations when active cohort capacity is tracked and assigned seats already meet or exceed capacity; pending invitations reserve seats. For relational providers, perform the duplicate check, capacity check, and pending invite creation inside a serializable transaction and map PostgreSQL serialization/deadlock conflicts to a 409 retryable business outcome.
- Update `GetDashboardAsync` totalSpend to use same paid sponsored invoice sum logic instead of placeholder 0.
- Add focused backend test coverage for no linked learners (zero/empty), invoices for sponsored learners included and others excluded, revoked sponsorships excluded, current-month and paid-only total calculation, sanitized invoice shape, dashboard consistency.
- Add focused backend test coverage for cohort-backed seat capacity, pending invitation reservation, consented sponsor-link entitlement evidence, and duplicate sponsorship/link learner de-duplication.
- Add focused backend test coverage for unconsented sponsor-link invoice exclusion, consent-start invoice scoping, consented link invoice inclusion/download, full tracked-capacity invite blocking, and invite allowance when capacity is untracked or remaining.

Remaining scope:

- Add sponsor billing accounts, paid seat-pack checkout, payment methods, sponsor-owned invoice generation, and learner consent UX beyond the current cohort/link-backed read model.
- Add provider-backed sponsor reconciliation, B2B invoice tax details, and paid seat-pack lifecycle states beyond the read-only local evidence slice.
- Add sponsor audit events for invite, capacity rejection, activation, revocation, billing, and entitlement grants.

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
