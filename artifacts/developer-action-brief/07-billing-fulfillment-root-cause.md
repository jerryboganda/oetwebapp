# Billing / invoice / fulfilment root cause + fix log

## A. Failed/pending payment invoice (candidate-visible invoice + download + notification)
- Symptom: failed/pending orders could surface a Paid candidate invoice (list + direct download).
- Root causes:
  1. `LearnerService.ApplyCheckoutCompletionAsync` minted `Status="Paid"` for every completed checkout, including `PendingVerification`/`PendingManual` orders parked for admin verification/hand-over.
  2. `LearnerService.EnsureSubscriptionInvoiceAsync` (learner billing-page backfill) minted `Paid` for ANY non-Draft `PriceAmount>0` subscription with no payment-evidence check and no Active check.
  3. `BillingExpansionEndpoints.EnsureSubscriptionInvoiceCoreAsync` minted `Paid` even for unevidenced admin grants (`Source=AdminGrant`).
  4. Learner list (`GetInvoicesAsync`) returned ALL statuses; download (`GetInvoiceDownloadAsync`) had no status gate (direct-URL bypass).
- Fix: paid-only gate end to end.
  - Checkout mints `Paid` only when the subscription lands `Active` (Listening Recalls auto + instant AI/addon paths); parked orders mint `Pending` (internal only). Replay promotes `Pending→Paid` exactly once, never downgrades.
  - Approval (`ManualPaymentService.ApproveAsync`) and fulfilment (`MarkSubscriptionFulfilled`) promote `Pending→Paid` on activation (web-access release only; external-only hand-overs untouched).
  - Backfill requires real evidence (`Gateway`/`ManualProof`, never `AdminGrant`) AND `Status==Active`; admin mint returns null for `AdminGrant`.
  - Learner list filters `Status.ToLower()=="paid"`; download 404s non-Paid ids.
  - Failed path (`MarkCheckoutFailedAsync`) still mints nothing, cancels the quote, voids reservations, sends only `LearnerPaymentFailed` (no invoice notification). Successful webhook replay early-returns on `Quote.Completed` (same invoice/entitlement reused; single `LearnerPaymentSucceeded`).
- Files: `Services/LearnerService.cs` (checkout invoice status + replay promotion + list/download/backfill gates), `Services/Billing/ManualPaymentService.cs` (approval promotion), `Endpoints/BillingExpansionEndpoints.cs` (fulfilment promotion + AdminGrant null).

## B. Failed-payment alarm ("Billing failures need attention" with Failed Invoices = 0)
- Symptom: owner/admin email "Billing failures need attention / Failed invoices crossed the configured alert threshold" while Billing Ops showed Failed Invoices = 0.
- Root cause: `AiBudgetAlertService.EvaluateAfterCommitAsync` reused `NotificationEventKey.AdminBillingFailureAlert` for AI provider-budget thresholds. Every AI-budget ladder hit (50/75/90/100%) therefore sent the billing-failure email; `GetDashboardSummaryAsync` correctly counted `Invoices.Status=="Failed"` (=0). Wrong metric/query/state: alert source was AI occupancy, not invoices or failed PaymentTransactions.
- Fix: separate signal. New `NotificationEventKey.AdminAiBudgetAlert` (+ catalog title "AI budget threshold reached", body with scope/period/threshold, URL `/admin/ai-usage`, policy entry) and `AiBudgetAlertService` now fires that key. `AdminBillingFailureAlert` is reserved for genuine invoice failures (currently never fires spuriously; no failed-invoice path was inventing rows).
- Files: `Domain/NotificationEntities.cs`, `Services/NotificationCatalog.cs`, `Services/Ai/AiBudgetAlertService.cs`.

## C. Incorrect near-one-year fulfilment access (02/09/2026 → 28/08/2027)
- Symptom: one Mark as Fulfilled produced ~360 days instead of six months.
- Root cause: `SubscriptionBundleInitializer.ResolveExpiry` anchored on a future `ExpiresAt` (`anchor = ExpiresAt>now ? ExpiresAt : now; return anchor+days`). Checkout completion set `now+180`; approval re-applied the bundle and anchored on that future value → `now+360`. Mark Fulfilled left the doubled value in place. Separately, `AdminService.CreateSubscriptionCoreAsync` never stamped the bundle at all (`ExpiresAt` null; `NextRenewalAt=+DurationMonths` only).
- Canonical source: `BillingPlan.AccessDurationDays` / `BillingPlanVersion.AccessDurationDays`, resolved via `ResolveAccessDurationDays` → `MaxAccessDurationDays = 180` (owner directive 2026-08-31; `SubscriptionBundleInitializer.cs:208`). No competing definition introduced.
- Fix: `ResolveExpiry` anchors on `StartedAt` (purchase instant; future-clamped to now) → idempotent set; repeats converge. Create path now calls `ApplyBundle` (counters + `AccessDurationDays` + `ExpiresAt`) and mirrors one-time expiry into `NextRenewalAt`. Fresh test: one fulfilled course = one entitlement + `StartedAt+180` (e.g. 02/09/2026 → 01/03/2027), never +360.
- Files: `Services/Billing/SubscriptionBundleInitializer.cs`, `Services/AdminService.cs`.

## D. Duplicate course entitlements (one fulfilment → seven rows)
- See `09-duplicate-entitlement-analysis.md` (identity, implementation, concurrency, repair). Backend: atomic fulfilment claim + idempotent replays (Ok, same DTO) + `(SubscriptionId,ItemCode,QuoteId)` unique partial index + migration `20261215090000_AddSubscriptionItemQuoteIdempotency`. No global `(UserId,PlanId)` uniqueness (renewals preserved).

## E. Listening Recalls missing automatic access
- Symptom: standalone `Listening Recalls` (code `listening-recalls`, 180 days, ~£17) required manual Approve/Mark Fulfilled; candidates waited for admin.
- Root cause: `ApplyCheckoutCompletionAsync` parked EVERY plan purchase at `PendingVerification`/`Pending` (verification flow), including `AutomaticWeb` plans. No exception existed for the standalone recalls product.
- Fix: `ListeningRecallsPolicy` (stable `PlanCode="listening-recalls"`; never price/label; bundled packages with different codes unaffected) + checkout exception: standalone recalls sets `FulfilmentStatus=Auto` and transitions `Draft→Active` immediately on verified `completed` payment, stamping the canonical bundle (`StartedAt+180`), minting the `Paid` invoice + gateway receipt + notifications idempotently (quote-`Completed` guard; replay reuses). Pending/processing/failed/cancelled/abandoned grant nothing; new purchasers grant once the quote's user association exists.
- Files: `Services/Billing/ListeningRecallsPolicy.cs` (new), `Services/LearnerService.cs` (checkout exception).

## F. Writing classification inconsistencies
- No AI classifier exists; `Other` (156/210 document tasks, 74%) is the deterministic fallback when no folder/filename rule matches — dominated by reference PDFs (Grammar Rules/Criteria/Booklets), videos excluded separately (75), and case notes without a letter hint. Duplicated filenames across professions (`grammar rules 1.pdf` ×5, `writing criteria.pdf` ×6, `mr james greenbaum.pdf` ×2) and `medicine-ar` vs `medicine` profession aliasing need admin disambiguation before publish. See `04-writing-classification-logic.md` + export notes. No data was silently cleaned.

## G. Video visibility: entitlement-based (not per-user)
- `VideoEntitlementService` + `EffectiveEntitlementResolver` derive visibility per request from Active subscriptions (plan Videos module / legacy node / add-on) × subtest/profession/scope/tag gates. `UserVideoAccess` is an optional restriction (null = unrestricted, fail-open) and is never required to expose a new video. Newly mapped Published+Ready videos are instantly visible to existing + future entitled learners. See `05-video-upload-visibility-sop.md`, `06-video-hierarchy.md`.
