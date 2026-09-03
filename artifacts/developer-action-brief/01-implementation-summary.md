# Implementation summary (what changed, why)

## Item 01 — Standalone Listening Recalls automatic access
- New `backend/src/OetLearner.Api/Services/Billing/ListeningRecallsPolicy.cs`: stable `PlanCode="listening-recalls"` + `IsStandaloneListeningRecalls()` (case-insensitive, trimmed; never price/label).
- `Services/LearnerService.cs` (`ApplyCheckoutCompletionAsync` plan block): standalone recalls bypasses the verification park (`FulfilmentStatus=Auto`, `Draft→Active` on verified `completed`), stamps the canonical bundle (`AccessDurationDays` 180 → `ExpiresAt=StartedAt+180`), mints `Paid` invoice + gateway receipt, sends the single payment-succeeded notification. All other plans unchanged; bundles containing recalls content keep their own codes and still require verification/hand-over. Replay safe via quote-`Completed` guard.

## Item 04A — Paid-only invoices + no unwanted alarm
- Checkout invoices: `Paid` only when the order lands `Active`, else `Pending` (internal); replay promotes `Pending→Paid` once.
- Approval + Mark Fulfilled promote `Pending→Paid` on web-access release (external-only untouched).
- Learner list filters Paid-only (case-insensitive); download 404s non-Paid.
- Backfills require real evidence (`Gateway`/`ManualProof`, never `AdminGrant`) and `Active`; admin mint returns null for `AdminGrant`.
- Alarm: new `AdminAiBudgetAlert` key + catalog; `AiBudgetAlertService` no longer raises `AdminBillingFailureAlert`.

## Item 04B — Six months only
- `SubscriptionBundleInitializer.ResolveExpiry` now anchors on `StartedAt` (idempotent) with the existing 180-day ceiling (`MaxAccessDurationDays`).
- `AdminService.CreateSubscriptionCoreAsync` now applies the canonical bundle (counters + duration + `ExpiresAt`) and mirrors one-time expiry into `NextRenewalAt`.

## Item 04C — One fulfilment → one entitlement
- Fulfilment paths already keyed on quote/proof/event identity; kept and documented.
- `MarkSubscriptionFulfilled` stays idempotent (Fulfilled→Ok same DTO; atomic `processing` claim; Serializable + concurrency catch).
- New partial unique index `(SubscriptionId, ItemCode, QuoteId)` where `QuoteId IS NOT NULL` + migration `20261215090000_AddSubscriptionItemQuoteIdempotency` (dedupes same-quote replays; different quotes = legitimate repurchases, untouched). No global user+plan uniqueness (renewals preserved).
- Tests: `TestG` corrected to idempotent-Ok semantics; `TestH` given realistic payment evidence; new `Billing/ListeningRecallsAndBillingHardeningTests` (12 tests) for LR/BILL rules.

## Item 02 — Writing QA
- `scripts/writing-qa-export.mjs` + `pnpm run writing:qa-export` → `artifacts/developer-action-brief/02-writing-qa-export.csv` (210 document tasks; 75 lesson videos explicitly excluded; reconciliation printed).
- `03-writing-backend-file-manifest.md` (relevant files only), `04-writing-classification-logic.md` (rule-based, no AI).

## Item 03 — Video
- `06-video-hierarchy.md` (actual Storage→Video→Category→Plan→Subscription→Visibility model), `05-video-upload-visibility-sop.md` (Bunny → admin pages → profession/package/category → publish → verify, with A/B/C examples). No code change needed: visibility is already entitlement-based (`VideoEntitlementService`); per-user assignment is never required.

## Files changed (source)
- `backend/src/OetLearner.Api/Services/Billing/ListeningRecallsPolicy.cs` (new)
- `backend/src/OetLearner.Api/Services/Billing/SubscriptionBundleInitializer.cs`
- `backend/src/OetLearner.Api/Services/AdminService.cs`
- `backend/src/OetLearner.Api/Services/LearnerService.cs`
- `backend/src/OetLearner.Api/Services/Billing/ManualPaymentService.cs`
- `backend/src/OetLearner.Api/Endpoints/BillingExpansionEndpoints.cs`
- `backend/src/OetLearner.Api/Data/LearnerDbContext.cs`
- `backend/src/OetLearner.Api/Data/Migrations/20261215090000_AddSubscriptionItemQuoteIdempotency.cs` (new)
- `backend/src/OetLearner.Api/Data/Migrations/LearnerDbContextModelSnapshot.cs`
- `backend/src/OetLearner.Api/Domain/NotificationEntities.cs`
- `backend/src/OetLearner.Api/Services/NotificationCatalog.cs`
- `backend/src/OetLearner.Api/Services/Ai/AiBudgetAlertService.cs`
- `backend/tests/OetLearner.Api.Tests/AdminPaymentQueueFulfillmentTests.cs` (TestG/TestH)
- `backend/tests/OetLearner.Api.Tests/Billing/ListeningRecallsAndBillingHardeningTests.cs` (new)
- `scripts/writing-qa-export.mjs` (new), `scripts/billing-dedupe-subscriptions.sql` (new)
- `package.json` (`writing:qa-export`)
